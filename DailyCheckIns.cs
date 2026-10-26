using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace StandReminder
{
    [Flags]
    internal enum DailyHabit { None=0, Pelvic=1, Water=2 }

    public sealed class DailyCheckInRecord
    {
        public string Date="";
        public bool PelvicDone;
        public bool WaterDone;
        public int WaterTargetMl=1500;
        public bool Reminded;
    }

    public sealed class DailyCheckInData
    {
        public int Version=1;
        public List<DailyCheckInRecord> Days=new List<DailyCheckInRecord>();
    }

    /// <summary>One self-reported completion per habit per local calendar date.</summary>
    internal sealed class DailyCheckInEngine
    {
        private readonly Dictionary<string,DailyCheckInRecord> days=new Dictionary<string,DailyCheckInRecord>();
        internal DailyCheckInRecord Today { get; private set; }
        internal bool HasChanges { get; private set; }
        internal DailyCheckInEngine(DailyCheckInData data,DateTime date,int defaultTarget)
        {
            ValidateData(data);
            foreach (DailyCheckInRecord record in data.Days) days.Add(record.Date,Copy(record));
            SetDate(date,defaultTarget);
        }
        internal bool SetDate(DateTime date,int defaultTarget)
        {
            ValidateTarget(defaultTarget);
            string key=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            if (Today!=null && Today.Date==key) return false;
            DailyCheckInRecord record;
            if (!days.TryGetValue(key,out record))
            {
                record=new DailyCheckInRecord(); record.Date=key; record.WaterTargetMl=defaultTarget;
                days.Add(key,record); HasChanges=true;
            }
            Today=record; return true;
        }
        internal void SetCompleted(DailyHabit habit,bool completed)
        {
            if (habit==DailyHabit.Pelvic)
            { if (Today.PelvicDone==completed) return; Today.PelvicDone=completed; }
            else if (habit==DailyHabit.Water)
            { if (Today.WaterDone==completed) return; Today.WaterDone=completed; }
            else throw new ArgumentOutOfRangeException("habit");
            HasChanges=true;
        }
        internal void SetWaterTarget(int millilitres)
        {
            ValidateTarget(millilitres);
            if (Today.WaterTargetMl==millilitres) return;
            Today.WaterTargetMl=millilitres;
            // A confirmation for the old target does not prove the new target was reached.
            Today.WaterDone=false; HasChanges=true;
        }
        internal DailyHabit GetReminder(DateTime now,int minuteOfDay,bool pelvicEnabled,bool waterEnabled)
        {
            if (minuteOfDay<0 || minuteOfDay>=1440) throw new ArgumentOutOfRangeException("minuteOfDay");
            if (Today.Date!=now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) || Today.Reminded || now.Hour*60+now.Minute<minuteOfDay)
                return DailyHabit.None;
            DailyHabit pending=DailyHabit.None;
            if (pelvicEnabled && !Today.PelvicDone) pending|=DailyHabit.Pelvic;
            if (waterEnabled && !Today.WaterDone) pending|=DailyHabit.Water;
            return pending;
        }
        internal void MarkReminded() { if (!Today.Reminded) { Today.Reminded=true; HasChanges=true; } }
        internal void AcceptSaved() { HasChanges=false; }
        internal int Import(DailyCheckInData data)
        {
            ValidateData(data); int additional=0;
            foreach (DailyCheckInRecord item in data.Days) if (!days.ContainsKey(item.Date)) additional++;
            if (days.Count+additional>50000) throw new InvalidDataException("Too many imported days.");
            int added=0;
            foreach (DailyCheckInRecord item in data.Days)
            {
                DailyCheckInRecord existing;
                if (days.TryGetValue(item.Date,out existing))
                {
                    // Only an untouched current day may take an old file's confirmation.
                    if (existing!=Today || existing.PelvicDone || existing.WaterDone || existing.Reminded || (!item.PelvicDone && !item.WaterDone)) continue;
                }
                DailyCheckInRecord imported=Copy(item); days[item.Date]=imported;
                if (Today.Date==item.Date) Today=imported;
                HasChanges=true; added++;
            }
            return added;
        }
        internal int Count(DailyHabit habit)
        {
            int total=0;
            foreach (DailyCheckInRecord day in days.Values)
                if ((habit==DailyHabit.Pelvic && day.PelvicDone) || (habit==DailyHabit.Water && day.WaterDone) ||
                    (habit==(DailyHabit.Pelvic|DailyHabit.Water) && day.PelvicDone && day.WaterDone)) total++;
            return total;
        }
        internal List<DailyCheckInRecord> History()
        {
            List<DailyCheckInRecord> result=new List<DailyCheckInRecord>();
            foreach (DailyCheckInRecord record in days.Values) result.Add(Copy(record));
            result.Sort(delegate(DailyCheckInRecord a,DailyCheckInRecord b) { return String.CompareOrdinal(b.Date,a.Date); });
            return result;
        }
        internal DailyCheckInData Snapshot()
        { DailyCheckInData data=new DailyCheckInData(); data.Days=History(); return data; }
        internal static void ValidateTarget(int value)
        {
            // Storage/input bounds, not a medically recommended drinking range.
            if (value<100 || value>10000) throw new ArgumentOutOfRangeException("value");
        }
        internal static void ValidateData(DailyCheckInData data)
        {
            if (data==null || data.Version!=1 || data.Days==null || data.Days.Count>50000) throw new InvalidDataException("Invalid check-in data.");
            HashSet<string> seen=new HashSet<string>();
            foreach (DailyCheckInRecord record in data.Days)
            {
                DateTime date;
                if (record==null || !DateTime.TryParseExact(record.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date) || !seen.Add(record.Date))
                    throw new InvalidDataException("Invalid or duplicate check-in date.");
                ValidateTarget(record.WaterTargetMl);
            }
        }
        private static DailyCheckInRecord Copy(DailyCheckInRecord record)
        {
            return new DailyCheckInRecord { Date=record.Date, PelvicDone=record.PelvicDone, WaterDone=record.WaterDone,
                WaterTargetMl=record.WaterTargetMl, Reminded=record.Reminded };
        }
    }

    internal sealed class DailyCheckInStore
    {
        private readonly string path;
        private bool damagedPrimary;
        internal string LastError { get; private set; }
        internal DailyCheckInStore(string filePath) { path=filePath; LastError=""; }
        internal DailyCheckInData Load()
        {
            if (!File.Exists(path) && !File.Exists(path+".bak")) return new DailyCheckInData();
            try { return Read(path); }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                damagedPrimary=File.Exists(path);
                try { DailyCheckInData data=Read(path+".bak"); LastError="主记录无法读取，已恢复备份。"; return data; }
                catch (Exception backupError)
                {
                    if (!Expected(backupError)) throw;
                    LastError="历史记录无法读取；本次显示新记录，原文件会保留备份。";
                    return new DailyCheckInData();
                }
            }
        }
        internal bool Save(DailyCheckInData data)
        {
            try
            {
                DailyCheckInEngine.ValidateData(data);
                if (damagedPrimary && File.Exists(path))
                    File.Copy(path,path+".damaged-"+Guid.NewGuid().ToString("N"),false);
                using (FileStream stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
                { new XmlSerializer(typeof(DailyCheckInData)).Serialize(stream,data); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(path+".tmp",path,damagedPrimary?null:path+".bak");
                else File.Move(path+".tmp",path);
                if (damagedPrimary || !File.Exists(path+".bak")) File.Copy(path,path+".bak",true);
                damagedPrimary=false; LastError=""; return true;
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                LastError="打卡记录保存失败，仅在本次运行有效；请检查文件夹写入权限。";
                return false;
            }
        }
        internal static DailyCheckInData Read(string file)
        {
            if (new FileInfo(file).Length>16*1024*1024) throw new InvalidDataException("Check-in file too large.");
            XmlReaderSettings settings=new XmlReaderSettings(); settings.DtdProcessing=DtdProcessing.Prohibit; settings.XmlResolver=null; settings.MaxCharactersInDocument=16*1024*1024;
            using (XmlReader reader=XmlReader.Create(file,settings))
            {
                DailyCheckInData data=(DailyCheckInData)new XmlSerializer(typeof(DailyCheckInData)).Deserialize(reader);
                DailyCheckInEngine.ValidateData(data); return data;
            }
        }
        private static bool Expected(Exception error)
        { return StorageErrors.Expected(error); }
    }
}
