using System;
using System.IO;
using System.Security;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace StandReminder
{
    internal sealed class UserDataLocation
    {
        internal readonly string DirectoryPath;
        internal string Error { get; private set; }
        internal string SettingsPath { get { return Path.Combine(DirectoryPath,"sitless.settings.xml"); } }
        internal string CheckInsPath { get { return Path.Combine(DirectoryPath,"sitless.checkins.xml"); } }
        internal static string DefaultDirectory
        { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SitLess"); } }
        internal UserDataLocation(string directory) { DirectoryPath=Path.GetFullPath(directory); Error=""; }
        internal void Initialize(string legacyDirectory)
        {
            Error="";
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (String.Equals(Path.GetFullPath(legacyDirectory).TrimEnd(Path.DirectorySeparatorChar),DirectoryPath.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)) return;
                foreach (string name in new string[]{"sitless.settings.xml","sitless.checkins.xml"})
                {
                    string target=Path.Combine(DirectoryPath,name), source=Path.Combine(legacyDirectory,name);
                    // A new binary must never overwrite the established per-user history.
                    if (File.Exists(target) || File.Exists(target+".bak")) continue;
                    if (File.Exists(source+".bak")) CopyOnce(source+".bak",target+".bak");
                    if (File.Exists(source)) CopyOnce(source,target);
                }
            }
            catch (Exception error)
            { if (!StorageErrors.Expected(error)) throw; Error="用户数据目录准备或旧记录迁移失败。请检查目录权限；原文件未删除。"; }
        }
        private static void CopyOnce(string source,string target)
        {
            string temp=target+".migration-"+Guid.NewGuid().ToString("N");
            try { File.Copy(source,temp,false); File.Move(temp,target); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    internal static class StorageErrors
    {
        internal static bool Expected(Exception error)
        { return error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is InvalidOperationException || error is XmlException || error is ArgumentException || error is SecurityException; }
    }

    internal sealed class PreferencesStore
    {
        private readonly string path;
        private bool damaged;
        internal string Error { get; private set; }
        internal PreferencesStore(string file) { path=file; Error=""; }
        internal Preferences Load()
        {
            if (!File.Exists(path) && !File.Exists(path+".bak")) return new Preferences();
            try { return Read(path); }
            catch (Exception error)
            {
                if (!StorageErrors.Expected(error)) throw;
                damaged=File.Exists(path);
                try { Preferences result=Read(path+".bak"); Error="设置主文件无法读取，已恢复备份。"; return result; }
                catch (Exception backupError)
                { if (!StorageErrors.Expected(backupError)) throw; Error="设置文件无法读取，暂用默认值；原文件会保留。"; return new Preferences(); }
            }
        }
        internal static Preferences Read(string file)
        {
            if (new FileInfo(file).Length>32768) throw new InvalidDataException("Settings too large.");
            XmlReaderSettings options=new XmlReaderSettings(); options.DtdProcessing=DtdProcessing.Prohibit; options.XmlResolver=null; options.MaxCharactersInDocument=32768;
            using (XmlReader reader=XmlReader.Create(file,options))
            {
                Preferences p=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(reader);
                if (p==null) throw new InvalidDataException("Null preferences.");
                if (p.WorkMinutes<5 || p.WorkMinutes>120) p.WorkMinutes=30;
                if (p.BreakMinutes<1 || p.BreakMinutes>20) p.BreakMinutes=3;
                if (Double.IsNaN(p.TodaySeconds) || Double.IsInfinity(p.TodaySeconds) || p.TodaySeconds<0 || p.TodaySeconds>86400) p.TodaySeconds=0;
                if (p.Completed<0 || p.Completed>100000) p.Completed=0;
                if (p.CheckInReminderMinute<0 || p.CheckInReminderMinute>=1440) p.CheckInReminderMinute=1200;
                if (p.DailyWaterTargetMl<100 || p.DailyWaterTargetMl>10000) p.DailyWaterTargetMl=1500;
                return p;
            }
        }
        internal bool Save(Preferences preferences)
        {
            try
            {
                if (damaged && File.Exists(path)) File.Copy(path,path+".damaged-"+Guid.NewGuid().ToString("N"),false);
                using (FileStream stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
                { new XmlSerializer(typeof(Preferences)).Serialize(stream,preferences); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(path+".tmp",path,damaged?null:path+".bak"); else File.Move(path+".tmp",path);
                if (damaged || !File.Exists(path+".bak")) File.Copy(path,path+".bak",true);
                damaged=false; Error=""; return true;
            }
            catch (Exception error)
            { if (!StorageErrors.Expected(error)) throw; Error="设置保存失败，本次修改尚未保存到用户数据目录。"; return false; }
        }
    }

    internal interface IStartupEntry
    {
        string Read();
        void Write(string command);
    }
    internal sealed class RegistryStartupEntry : IStartupEntry
    {
        private const string KeyPath="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        public string Read()
        { using (RegistryKey key=Registry.CurrentUser.OpenSubKey(KeyPath,false)) return key==null?null:key.GetValue("SitLess") as string; }
        public void Write(string command)
        {
            if (command==null)
            { using (RegistryKey key=Registry.CurrentUser.OpenSubKey(KeyPath,true)) if (key!=null) key.DeleteValue("SitLess",false); }
            else using (RegistryKey key=Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue("SitLess",command,RegistryValueKind.String);
        }
    }
    internal static class StartupRegistration
    {
        internal static string Commit(IStartupEntry entry,Preferences preferences,bool enabled,string executable,Func<bool> save)
        {
            bool previous=preferences.StartWithWindows;
            string previousCommand;
            try { previousCommand=entry.Read(); }
            catch (Exception error) { if (!StorageErrors.Expected(error)) throw; return "无法读取自启动项，未更改设置。"; }
            preferences.StartWithWindows=enabled;
            if (!save()) { preferences.StartWithWindows=previous; return "设置保存失败，自启动项未更改，请重试。"; }
            string problem=Apply(entry,enabled,executable);
            if (problem.Length==0) return "";
            preferences.StartWithWindows=previous;
            try { if (entry.Read()!=previousCommand) entry.Write(previousCommand); }
            catch (Exception error) { if (!StorageErrors.Expected(error)) throw; problem+=" 启动项回滚失败，请检查权限。"; }
            if (!save()) problem+=" 原偏好恢复保存失败，请重试。";
            return problem;
        }
        internal static string Command(string executable)
        {
            if (String.IsNullOrEmpty(executable) || !Path.IsPathRooted(executable) || executable.IndexOfAny(new char[]{'"','\r','\n'})>=0 || !String.Equals(Path.GetExtension(executable),".exe",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid executable path.");
            return "\""+executable+"\" --startup";
        }
        internal static string Apply(IStartupEntry entry,bool enabled,string executable)
        {
            try
            {
                string expected=enabled?Command(executable):null;
                if (!String.Equals(entry.Read(),expected,StringComparison.OrdinalIgnoreCase)) entry.Write(expected);
                if (!String.Equals(entry.Read(),expected,StringComparison.OrdinalIgnoreCase)) return "自启动设置未生效，请检查 Windows 启动项权限。";
                return "";
            }
            catch (Exception error)
            { if (!StorageErrors.Expected(error)) throw; return "无法更新当前用户的自启动项，请检查权限后重试。"; }
        }
    }
}
