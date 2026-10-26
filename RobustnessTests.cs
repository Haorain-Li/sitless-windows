using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using System.Xml.Serialization;
using StandReminder;

internal static class RobustnessTests
{
    private static int assertions;
    private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly DateTime Day=new DateTime(2026,10,26);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process,int flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
    private static void Equal<T>(T expected,T actual,string message)
    { assertions++; if (!Object.Equals(expected,actual)) throw new Exception(message+": expected "+expected+", actual "+actual); }
    private static void Reject(Action action,string message)
    {
        assertions++;
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidDataException) { return; }
        throw new Exception(message+": invalid input accepted");
    }
    private static object Field(object owner,string name) { return owner.GetType().GetField(name,Hidden).GetValue(owner); }
    private static void WriteXml<T>(string path,T data)
    { using (FileStream stream=File.Create(path)) new XmlSerializer(typeof(T)).Serialize(stream,data); }
    private sealed class StartupFixture : IStartupEntry
    {
        internal string Value;
        internal int Writes;
        internal bool DenyRead,DenyWrite,IgnoreWrite;
        public string Read() { if (DenyRead) throw new SecurityException(); return Value; }
        public void Write(string command) { if (DenyWrite) throw new UnauthorizedAccessException(); Writes++; if (!IgnoreWrite) Value=command; }
    }
    private static Control Find(Control parent,string text)
    { foreach (Control child in parent.Controls) { if (child.Text==text) return child; Control nested=Find(child,text); if (nested!=null) return nested; } return null; }
    [STAThread]
    public static int Main()
    {
        string output=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        string root=Path.Combine(output,"robustness-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Application.EnableVisualStyles();
            string legacy=Path.Combine(root,"old app"), user=Path.Combine(root,"fixed-user-data"); Directory.CreateDirectory(legacy);
            Preferences old=new Preferences(); old.StartWithWindows=false; old.DailyWaterTargetMl=1700;
            WriteXml(Path.Combine(legacy,"sitless.settings.xml"),old);
            DailyCheckInEngine original=new DailyCheckInEngine(new DailyCheckInData(),Day,1700); original.SetCompleted(DailyHabit.Pelvic,true);
            DailyCheckInStore oldStore=new DailyCheckInStore(Path.Combine(legacy,"sitless.checkins.xml")); Equal(true,oldStore.Save(original.Snapshot()),"Legacy fixture saved");
            UserDataLocation location=new UserDataLocation(user); location.Initialize(legacy);
            Equal("",location.Error,"Migration succeeds");
            Equal(false,new PreferencesStore(location.SettingsPath).Load().StartWithWindows,"Migration preserves disabled startup preference");
            Equal(1,new DailyCheckInEngine(new DailyCheckInStore(location.CheckInsPath).Load(),Day,1500).Count(DailyHabit.Pelvic),"Migrated history preserves count");
            Equal(true,File.Exists(Path.Combine(legacy,"sitless.checkins.xml")),"Migration retains original");
            Equal(true,File.Exists(location.CheckInsPath+".bak"),"Migration retains backup");
            string moved=Path.Combine(root,"new binary folder"); Directory.CreateDirectory(moved);
            location.Initialize(moved);
            Equal(1,new DailyCheckInEngine(new DailyCheckInStore(location.CheckInsPath).Load(),Day,1500).Count(DailyHabit.Pelvic),"Moving executable retains fixed history");
            old.DailyWaterTargetMl=1800; WriteXml(Path.Combine(legacy,"sitless.settings.xml"),old); location.Initialize(legacy);
            Equal(1700,new PreferencesStore(location.SettingsPath).Load().DailyWaterTargetMl,"Old portable data cannot overwrite canonical settings");

            string lockedOld=Path.Combine(root,"locked-old"); Directory.CreateDirectory(lockedOld);
            WriteXml(Path.Combine(lockedOld,"sitless.settings.xml"),old);
            UserDataLocation retry=new UserDataLocation(Path.Combine(root,"retry-user"));
            using (FileStream locked=new FileStream(Path.Combine(lockedOld,"sitless.settings.xml"),FileMode.Open,FileAccess.Read,FileShare.None))
            { retry.Initialize(lockedOld); Equal(true,retry.Error.Length>0,"Locked migration source reported"); }
            retry.Initialize(lockedOld); Equal("",retry.Error,"Migration retries after unlocking");
            string notDirectory=Path.Combine(root,"not-directory"); File.WriteAllText(notDirectory,"file");
            UserDataLocation denied=new UserDataLocation(notDirectory); denied.Initialize(legacy); Equal(true,denied.Error.Length>0,"Invalid data directory reported without crash");

            string settings=Path.Combine(root,"preferences.xml"); PreferencesStore preferences=new PreferencesStore(settings);
            Equal(true,preferences.Load().StartWithWindows,"Fresh preference enables startup");
            Equal(true,preferences.Save(new Preferences()),"Preferences first atomic save");
            Preferences disabled=new Preferences(); disabled.StartWithWindows=false;
            Equal(true,preferences.Save(disabled),"Preferences replace atomically");
            Equal(false,new PreferencesStore(settings).Load().StartWithWindows,"Disabled startup survives restart");
            File.WriteAllText(settings,"damaged"); preferences=new PreferencesStore(settings);
            Preferences recovered=preferences.Load(); Equal(true,recovered.StartWithWindows,"Preferences recover previous backup"); Equal(true,preferences.Error.Length>0,"Recovery warning available");
            Equal(true,preferences.Save(disabled),"Recovered preferences save");
            Equal(1,Directory.GetFiles(root,"preferences.xml.damaged-*").Length,"Damaged preferences preserved");
            using (FileStream locked=new FileStream(settings+".tmp",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
                Equal(false,preferences.Save(new Preferences()),"Locked temp prevents settings save safely");
            Equal(false,PreferencesStore.Read(settings).StartWithWindows,"Failed save does not change existing settings");
            Equal(true,preferences.Save(disabled),"Settings save retry succeeds");
            string malformed=Path.Combine(root,"malformed.xml");
            File.WriteAllText(malformed,"<Preferences xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:nil='true'/>");
            Equal(true,new PreferencesStore(malformed).Load().StartWithWindows,"Null XML falls back instead of crashing");
            File.WriteAllText(malformed,"<Preferences><WorkMinutes>-1</WorkMinutes><BreakMinutes>999</BreakMinutes><Completed>-1</Completed><TodaySeconds>NaN</TodaySeconds><DailyWaterTargetMl>-4</DailyWaterTargetMl></Preferences>");
            Preferences sanitized=PreferencesStore.Read(malformed); Equal(30,sanitized.WorkMinutes,"Invalid interval sanitized"); Equal(3,sanitized.BreakMinutes,"Invalid break sanitized");
            Equal(0,sanitized.Completed,"Negative completion sanitized"); Equal(0d,sanitized.TodaySeconds,"NaN time sanitized"); Equal(1500,sanitized.DailyWaterTargetMl,"Invalid water target sanitized");
            File.WriteAllText(malformed,"<!DOCTYPE x [<!ENTITY a 'unsafe'>]><Preferences>&a;</Preferences>");
            Equal(true,new PreferencesStore(malformed).Load().StartWithWindows,"DTD settings rejected safely");
            Equal(false,new PreferencesStore(Path.Combine(notDirectory,"settings.xml")).Save(new Preferences()),"Unwritable data path does not crash");

            StartupFixture startup=new StartupFixture(); string executable="D:\\测试 app\\SitLess.exe";
            Equal("\"D:\\测试 app\\SitLess.exe\" --startup",StartupRegistration.Command(executable),"Quoted Unicode executable command");
            Equal("",StartupRegistration.Apply(startup,true,executable),"Enable startup"); Equal(1,startup.Writes,"Startup written once");
            Equal("",StartupRegistration.Apply(startup,true,executable),"Idempotent startup update"); Equal(1,startup.Writes,"Matching startup command untouched");
            Equal("",StartupRegistration.Apply(startup,true,"D:\\moved\\SitLess.exe"),"Moved executable updates startup path"); Equal(2,startup.Writes,"Relocation written");
            Equal("",StartupRegistration.Apply(startup,false,executable),"Disable startup"); Equal(null,startup.Value,"Disable removes only app entry");
            Equal("",StartupRegistration.Apply(startup,false,executable),"Repeated disable no-op"); Equal(3,startup.Writes,"No redundant disable write");
            Equal(true,StartupRegistration.Apply(new StartupFixture { DenyRead=true },true,executable).Length>0,"Read permission denial surfaced");
            Equal(true,StartupRegistration.Apply(new StartupFixture { DenyWrite=true },true,executable).Length>0,"Write permission denial surfaced");
            Equal(true,StartupRegistration.Apply(new StartupFixture { IgnoreWrite=true },true,executable).Length>0,"Readback catches ignored startup writes");
            Preferences transaction=new Preferences(); StartupFixture transactional=new StartupFixture();
            Equal(true,StartupRegistration.Commit(transactional,transaction,false,executable,delegate { return false; }).Length>0,"Failed preference save aborts startup change");
            Equal(0,transactional.Writes,"Failed preference save does not touch startup entry"); Equal(true,transaction.StartWithWindows,"Failed save restores in-memory preference");
            transactional.DenyWrite=true; int transactionSaves=0;
            Equal(true,StartupRegistration.Commit(transactional,transaction,true,executable,delegate { transactionSaves++; return true; }).Length>0,"Startup permission failure reported after save");
            Equal(2,transactionSaves,"Startup failure restores saved preference");
            transactional.DenyWrite=false;
            Equal("",StartupRegistration.Commit(transactional,transaction,false,executable,delegate { return true; }),"Startup transaction can commit after failure");
            Equal(false,transaction.StartWithWindows,"Startup transaction commits desired preference");
            Reject(delegate { StartupRegistration.Command("relative.exe"); },"Relative startup path rejected");
            Reject(delegate { StartupRegistration.Command("D:\\bad\"name.exe"); },"Injected quote rejected");
            Reject(delegate { StartupRegistration.Command("D:\\bad\nname.exe"); },"Injected newline rejected");

            foreach (int year in new int[]{1,1900,2000,2024,2026,2100,9999})
            for (int month=1;month<=12;month++)
            {
                DateTime first=new DateTime(year,month,1); HashSet<int> seen=new HashSet<int>(); bool correct=true;
                for (int cell=0;cell<42;cell++)
                {
                    DateTime? date=CalendarDates.DayAt(first,cell); if (!date.HasValue) continue;
                    correct &= date.Value.Month==month && date.Value.Year==year && seen.Add(date.Value.Day) && ((int)date.Value.DayOfWeek+6)%7==cell%7;
                }
                Equal(true,correct,"Calendar places unique dates under correct weekday"); Equal(DateTime.DaysInMonth(year,month),seen.Count,"Calendar covers full month including leap years");
            }
            Equal(new DateTime(1,1,1),CalendarDates.MoveMonth(DateTime.MinValue,int.MinValue),"Calendar clamps minimum month");
            Equal(new DateTime(9999,12,1),CalendarDates.MoveMonth(DateTime.MaxValue,int.MaxValue),"Calendar clamps maximum month");
            Equal(null,CalendarDates.DayAt(Day,-1),"Negative calendar cell ignored"); Equal(null,CalendarDates.DayAt(Day,42),"Outside calendar cell ignored");
            using (DailyCalendar calendar=new DailyCalendar(1))
            {
                calendar.SelectDate(new DateTime(2024,2,29));
                typeof(DailyCalendar).GetMethod("OnKeyDown",Hidden).Invoke(calendar,new object[]{new KeyEventArgs(Keys.Right)});
                Equal(new DateTime(2024,3,1),calendar.SelectedDate,"Keyboard crosses leap month boundary");
                calendar.SelectDate(DateTime.MinValue);
                typeof(DailyCalendar).GetMethod("OnKeyDown",Hidden).Invoke(calendar,new object[]{new KeyEventArgs(Keys.Left)});
                Equal(DateTime.MinValue,calendar.SelectedDate,"Keyboard guards earliest date");
                calendar.SelectDate(DateTime.MaxValue.Date);
                typeof(DailyCalendar).GetMethod("OnKeyDown",Hidden).Invoke(calendar,new object[]{new KeyEventArgs(Keys.Right)});
                Equal(DateTime.MaxValue.Date,calendar.SelectedDate,"Keyboard guards latest date");
            }
            DailyCheckInEngine imported=new DailyCheckInEngine(new DailyCheckInData(),Day,1500);
            Equal(1,imported.Import(original.Snapshot()),"Import restores untouched current day");
            Equal(1,imported.Count(DailyHabit.Pelvic),"Imported completion counted once");
            Equal(0,imported.Import(original.Snapshot()),"Repeated import idempotent");
            DailyCheckInData invalid=original.Snapshot(); invalid.Days.Add(new DailyCheckInRecord { Date="bad" });
            Reject(delegate { imported.Import(invalid); },"Invalid import rejected before mutation"); Equal(1,imported.Count(DailyHabit.Pelvic),"Invalid import preserves original counts");
            DailyCheckInData huge=new DailyCheckInData();
            for (int i=0;i<50000;i++) huge.Days.Add(new DailyCheckInRecord { Date=new DateTime(1000,1,1).AddDays(i).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture) });
            Reject(delegate { imported.Import(huge); },"Combined history capacity enforced before import");
            Equal(1,imported.History().Count,"Over-capacity import does not partially modify history");
            string invalidHistory=Path.Combine(root,"invalid-history.xml");
            WriteXml(invalidHistory,invalid); WriteXml(invalidHistory+".bak",original.Snapshot());
            DailyCheckInStore repaired=new DailyCheckInStore(invalidHistory);
            Equal(1,new DailyCheckInEngine(repaired.Load(),Day,1500).Count(DailyHabit.Pelvic),"Semantically invalid XML recovers backup instead of crashing");

            Rectangle initial=new Rectangle(-500,100,344,390); Size minimum=new Size(320,360), maximum=new Size(860,975);
            for (int hit=10;hit<=17;hit++)
            foreach (Point delta in new Point[]{new Point(-700,-700),new Point(900,900),new Point(30,-20)})
            {
                Rectangle changed=WindowSizing.DragBounds(initial,Point.Empty,delta,hit,minimum,maximum);
                Equal(true,changed.Width>=320 && changed.Width<=860 && changed.Height>=360 && changed.Height<=975,"Drag clamps dimensions");
                if (hit==10 || hit==13 || hit==16) Equal(initial.Right,changed.Right,"Left resize keeps right anchor");
                if (hit==12 || hit==13 || hit==14) Equal(initial.Bottom,changed.Bottom,"Top resize keeps bottom anchor");
            }
            Equal(new Rectangle(-470,80,344,390),WindowSizing.DragBounds(initial,Point.Empty,new Point(30,-20),2,minimum,maximum),"Move keeps size across negative monitor coordinates");
            DateTime realSettings=File.GetLastWriteTimeUtc(Path.Combine(UserDataLocation.DefaultDirectory,"sitless.settings.xml"));
            DateTime realHistory=File.GetLastWriteTimeUtc(Path.Combine(UserDataLocation.DefaultDirectory,"sitless.checkins.xml"));
            foreach (float dpi in new float[]{1,1.5f,2})
            using (FloatWindow window=new FloatWindow(false,true,dpi))
            {
                window.Show(); CreateParams native=(CreateParams)window.GetType().GetProperty("CreateParams",Hidden).GetValue(window,null);
                Equal(0,native.Style&(0x40000|0xC00000),"No native caption or sizing frame"); Equal(0,native.ClassStyle&0x20000,"No native class shadow");
                Rectangle before=window.Bounds;
                for (int i=0;i<30;i++) { SendMessage(window.Handle,0x86,new IntPtr(i%2),IntPtr.Zero); SendMessage(window.Handle,0x85,new IntPtr(1),IntPtr.Zero); }
                Equal(before,window.Bounds,"Repeated activation cannot introduce extra frame size");
                window.BeginPointerDrag(17,Point.Empty); window.ContinuePointerDrag(new Point(25,20));
                Equal(Math.Min(window.MaximumSize.Width,before.Width+25),window.Width,"Client drag actually changes width");
                Equal(Math.Min(window.MaximumSize.Height,before.Height+20),window.Height,"Client drag actually changes height"); window.EndPointerDrag();
                int fonts=((Dictionary<Control,Font>)Field(window,"ownedFonts")).Count;
                int gdiBefore; using (Process process=Process.GetCurrentProcess()) gdiBefore=GetGuiResources(process.Handle,0);
                for (int i=0;i<200;i++)
                {
                    window.BeginPointerDrag(17,Point.Empty); window.ContinuePointerDrag(new Point(i%2==0?25:-25,i%2==0?20:-20)); window.EndPointerDrag();
                    Equal(false,window.Capture,"Drag releases capture"); Equal(0,(int)Field(window,"pointerHit"),"Drag state cleared");
                }
                window.BeginPointerDrag(17,Point.Empty); window.Capture=false;
                Equal(0,(int)Field(window,"pointerHit"),"Lost capture terminates drag safely");
                Equal(fonts,((Dictionary<Control,Font>)Field(window,"ownedFonts")).Count,"Resize does not accumulate owned fonts");
                GC.Collect(); GC.WaitForPendingFinalizers();
                int gdiAfter; using (Process process=Process.GetCurrentProcess()) gdiAfter=GetGuiResources(process.Handle,0);
                Equal(true,gdiAfter<=gdiBefore+64,"Repeated resizing has bounded GDI resources");
                window.BeginPointerDrag(17,Point.Empty); window.Dispose();
                Equal(false,window.Capture,"Disposal releases an active drag"); Equal(0,(int)Field(window,"pointerHit"),"Disposal clears drag state");
            }
            Equal(realSettings,File.GetLastWriteTimeUtc(Path.Combine(UserDataLocation.DefaultDirectory,"sitless.settings.xml")),"Rendering tests do not write actual user preferences");
            Equal(realHistory,File.GetLastWriteTimeUtc(Path.Combine(UserDataLocation.DefaultDirectory,"sitless.checkins.xml")),"Rendering tests do not write actual health history");
            foreach (float dpi in new float[]{1,1.5f,2})
            {
                bool deniedSave=true; int calls=0; bool desired=true;
                using (SettingsWindow settingsWindow=new SettingsWindow(new Preferences(),dpi,"",delegate(bool value) { calls++; desired=value; return deniedSave?"模拟：无自启动写入权限":""; }))
                {
                    settingsWindow.Show(); Equal(true,settingsWindow.StartOn,"Startup switch defaults on in UI");
                    ((CheckBox)Field(settingsWindow,"startup")).Checked=false;
                    ((Button)Find(settingsWindow,"保存设置")).PerformClick();
                    Equal(DialogResult.None,settingsWindow.DialogResult,"Startup failure keeps settings open"); Equal(false,desired,"Disable choice passed to startup registration");
                    deniedSave=false; ((Button)Find(settingsWindow,"保存设置")).PerformClick();
                    Equal(DialogResult.OK,settingsWindow.DialogResult,"Startup retry can commit"); Equal(2,calls,"Only explicit saves update startup");
                }
            }
            Console.WriteLine("PASS: "+assertions+" robustness assertions; no real startup entry or user data modified."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: "+error.GetBaseException()); return 1; }
        finally
        {
            string resolved=Path.GetFullPath(root);
            if (!resolved.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("robustness-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe fixture cleanup path.");
            Directory.Delete(resolved,true);
        }
    }
}
