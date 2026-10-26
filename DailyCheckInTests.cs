using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;
using StandReminder;

internal static class DailyCheckInTests
{
    private static int assertions;
    private static readonly DateTime Day=new DateTime(2026,10,26);
    private const DailyHabit Both=DailyHabit.Pelvic|DailyHabit.Water;
    private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
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
    private static object Field(object target,string name) { return target.GetType().GetField(name,Hidden).GetValue(target); }
    private static void Set(object target,string name,object value) { target.GetType().GetField(name,Hidden).SetValue(target,value); }
    private static void Call(object target,string name,params object[] args) { target.GetType().GetMethod(name,Hidden).Invoke(target,args); }
    private static DailyCheckInEngine NewEngine() { return new DailyCheckInEngine(new DailyCheckInData(),Day,1500); }

    [STAThread]
    public static int Main()
    {
        string prefix="daily-checkin-test-"+Guid.NewGuid().ToString("N");
        string folder=AppDomain.CurrentDomain.BaseDirectory;
        string path=Path.Combine(folder,prefix+".xml");
        try
        {
            DailyCheckInEngine engine=NewEngine();
            Equal("2026-10-26",engine.Today.Date,"Local calendar key");
            Equal(false,engine.Today.PelvicDone,"Pelvic starts unchecked"); Equal(false,engine.Today.WaterDone,"Water starts unchecked");
            Equal(1500,engine.Today.WaterTargetMl,"New day snapshots target"); Equal(true,engine.HasChanges,"New day needs persistence");
            engine.AcceptSaved(); engine.SetCompleted(DailyHabit.Pelvic,true);
            Equal(1,engine.Count(DailyHabit.Pelvic),"Completion counts one day"); Equal(true,engine.HasChanges,"Completion is saved");
            engine.AcceptSaved(); engine.SetCompleted(DailyHabit.Pelvic,true);
            Equal(1,engine.Count(DailyHabit.Pelvic),"Repeated completion cannot inflate count"); Equal(false,engine.HasChanges,"Idempotent completion stays clean");
            engine.SetCompleted(DailyHabit.Water,true); Equal(1,engine.Count(Both),"Both counts complete days");
            engine.SetCompleted(DailyHabit.Pelvic,false); Equal(0,engine.Count(DailyHabit.Pelvic),"Undo subtracts count"); Equal(0,engine.Count(Both),"Undo adjusts both count");
            engine.SetCompleted(DailyHabit.Pelvic,true); engine.MarkReminded();
            Equal(true,engine.SetDate(Day.AddDays(1),1700),"Midnight changes active record");
            Equal(false,engine.Today.WaterDone,"Water resets tomorrow"); Equal(false,engine.Today.PelvicDone,"Pelvic resets tomorrow");
            Equal(false,engine.Today.Reminded,"Reminder resets tomorrow"); Equal(1700,engine.Today.WaterTargetMl,"Tomorrow gets new default");
            Equal(1,engine.Count(DailyHabit.Water),"History count survives midnight");
            engine.SetCompleted(DailyHabit.Water,true); engine.SetWaterTarget(1800);
            Equal(false,engine.Today.WaterDone,"Changing target requires reconfirmation"); Equal(1800,engine.Today.WaterTargetMl,"Today's target changes");
            Equal(1500,engine.History()[1].WaterTargetMl,"Past target remains unchanged");
            engine.SetCompleted(DailyHabit.Water,true); engine.AcceptSaved(); engine.SetWaterTarget(1800);
            Equal(true,engine.Today.WaterDone,"Unchanged target preserves confirmation"); Equal(false,engine.HasChanges,"No-op target is clean");
            DailyCheckInEngine restored=new DailyCheckInEngine(engine.Snapshot(),Day.AddDays(1),1500);
            Equal(true,restored.Today.WaterDone,"Restart restores check"); Equal(2,restored.Count(DailyHabit.Water),"Restart restores count");
            restored.SetDate(Day,1700); Equal(true,restored.Today.PelvicDone,"Clock rollback reuses recorded date");
            Equal(true,restored.Today.Reminded,"Clock rollback preserves reminder"); Equal(1500,restored.Today.WaterTargetMl,"Clock rollback preserves old target");
            restored.SetDate(Day.AddDays(4),1700); Equal(3,restored.History().Count,"Missing days do not fabricate history");
            DailyCheckInData snapshot=restored.Snapshot(); snapshot.Days[1].WaterDone=false;
            Equal(2,restored.Count(DailyHabit.Water),"Snapshot cannot mutate live record");
            restored.History()[0].PelvicDone=true; Equal(false,restored.Today.PelvicDone,"History view cannot mutate live record");

            engine=NewEngine();
            Equal(DailyHabit.None,engine.GetReminder(Day.AddHours(19).AddMinutes(59),1200,true,true),"No early reminder");
            Equal(Both,engine.GetReminder(Day.AddHours(20),1200,true,true),"Combined reminder at configured time");
            Equal(Both,engine.GetReminder(Day.AddHours(22),1200,true,true),"Late start catches today's reminder");
            Equal(DailyHabit.None,engine.GetReminder(Day.AddDays(1).AddHours(20),1200,true,true),"Never remind yesterday");
            Equal(DailyHabit.Water,engine.GetReminder(Day.AddHours(20),1200,false,true),"Disabled pelvic reminder excluded");
            Equal(DailyHabit.None,engine.GetReminder(Day.AddHours(20),1200,false,false),"Both toggles disabled");
            engine.SetCompleted(DailyHabit.Pelvic,true);
            Equal(DailyHabit.Water,engine.GetReminder(Day.AddHours(20),1200,true,true),"Completed habit excluded");
            engine.SetCompleted(DailyHabit.Water,true);
            Equal(DailyHabit.None,engine.GetReminder(Day.AddHours(20),1200,true,true),"All done is quiet");
            engine.SetCompleted(DailyHabit.Water,false); engine.MarkReminded();
            Equal(DailyHabit.None,engine.GetReminder(Day.AddHours(23),1200,true,true),"At most one notification per day");
            restored=new DailyCheckInEngine(engine.Snapshot(),Day,1500);
            Equal(DailyHabit.None,restored.GetReminder(Day.AddHours(21),1200,true,true),"Restart does not repeat reminder");
            restored.SetDate(Day.AddDays(1),1500);
            Equal(Both,restored.GetReminder(Day.AddDays(1).AddHours(20),1200,true,true),"Tomorrow has its own reminder");
            engine=NewEngine();
            Equal(Both,engine.GetReminder(Day,0,true,true),"Midnight reminder supported");
            Equal(Both,engine.GetReminder(Day.AddHours(23).AddMinutes(59),1439,true,true),"Last minute supported");
            Reject(delegate { engine.GetReminder(Day,-1,true,true); },"Negative time rejected");
            Reject(delegate { engine.GetReminder(Day,1440,true,true); },"Out-of-day time rejected");
            Reject(delegate { engine.SetCompleted(Both,true); },"Ambiguous habit rejected");
            Reject(delegate { engine.SetWaterTarget(0); },"Zero target rejected");
            Reject(delegate { engine.SetWaterTarget(10001); },"Excess storage target rejected");
            DailyCheckInData invalid=engine.Snapshot(); invalid.Version=2;
            Reject(delegate { new DailyCheckInEngine(invalid,Day,1500); },"Unknown schema rejected");
            invalid=engine.Snapshot(); invalid.Days.Add(invalid.Days[0]);
            Reject(delegate { new DailyCheckInEngine(invalid,Day,1500); },"Duplicate day rejected");
            invalid=engine.Snapshot(); invalid.Days[0].Date="2026-02-30";
            Reject(delegate { new DailyCheckInEngine(invalid,Day,1500); },"Invalid calendar date rejected");

            DailyCheckInStore store=new DailyCheckInStore(path);
            Equal(0,store.Load().Days.Count,"No file gives empty history");
            Equal(true,store.Save(engine.Snapshot()),"Initial atomic save"); Equal(true,File.Exists(path+".bak"),"Initial backup created");
            engine.SetCompleted(DailyHabit.Pelvic,true); Equal(true,store.Save(engine.Snapshot()),"Atomic replacement saves");
            Equal(true,new DailyCheckInEngine(new DailyCheckInStore(path).Load(),Day,1500).Today.PelvicDone,"XML restart roundtrip");
            using (FileStream blocked=new FileStream(path+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                Equal(false,store.Save(engine.Snapshot()),"Locked temp file reports save failure");
            Equal(true,store.LastError.Length>0,"Save failure shown");
            Equal(true,store.Save(engine.Snapshot()),"Retry after unlocking succeeds"); Equal("",store.LastError,"Successful retry clears warning");
            File.WriteAllText(path,"broken XML"); store=new DailyCheckInStore(path);
            DailyCheckInData recovered=store.Load();
            Equal(true,new DailyCheckInEngine(recovered,Day,1500).Today.PelvicDone,"Damaged primary recovers valid backup");
            Equal(true,store.LastError.Length>0,"Recovery is reported");
            Equal(true,store.Save(recovered),"Recovered data can be saved");
            Equal(1,Directory.GetFiles(folder,prefix+".xml.damaged-*").Length,"Damaged primary preserved");
            Equal(true,new DailyCheckInEngine(new DailyCheckInStore(path).Load(),Day,1500).Today.PelvicDone,"Recovered file reads again");
            File.WriteAllText(path,"<!DOCTYPE x [<!ENTITY a 'test'>]><DailyCheckInData>&a;</DailyCheckInData>");
            File.WriteAllText(path+".bak","broken backup"); store=new DailyCheckInStore(path);
            Equal(0,store.Load().Days.Count,"DTD and damaged backup fall back safely"); Equal(true,store.LastError.Length>0,"Unreadable history shown");
            Equal(true,File.ReadAllText(path).Contains("DOCTYPE"),"Load never overwrites damaged originals");

            using (StringReader reader=new StringReader("<Preferences><Sound>true</Sound></Preferences>"))
            {
                Preferences old=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(reader);
                Equal(1500,old.DailyWaterTargetMl,"Old settings get editable default target");
                Equal(1200,old.CheckInReminderMinute,"Old settings get 20:00 reminder");
                Equal(true,old.PelvicCheckReminder && old.WaterCheckReminder,"Old settings get reminder toggles");
            }
            DateTime settingsWritten=File.GetLastWriteTimeUtc(Path.Combine(folder,"sitless.settings.xml"));
            DateTime checkInsWritten=File.GetLastWriteTimeUtc(Path.Combine(folder,"sitless.checkins.xml"));
            using (FloatWindow window=new FloatWindow(false,true))
            {
                Preferences prefs=(Preferences)Field(window,"prefs"); prefs.CheckInReminderMinute=0;
                DailyCheckInEngine checks=(DailyCheckInEngine)Field(window,"checkIns");
                TimerEngine clock=(TimerEngine)Field(window,"engine");
                DateTime evening=DateTime.Today.AddHours(23);
                Equal(Both,window.PendingDailyCheckIns(evening),"App offers today's daily reminder");
                QuietModeState quiet=(QuietModeState)Field(window,"quiet"); quiet.Observe(true,true,TimeSpan.Zero);
                Equal(DailyHabit.None,window.PendingDailyCheckIns(evening),"Fullscreen suppresses daily reminder");
                Equal(false,checks.Today.Reminded,"Suppressed reminder stays pending");
                quiet.Observe(false,false,TimeSpan.Zero); Set(window,"locked",true);
                Equal(DailyHabit.None,window.PendingDailyCheckIns(evening),"Lock suppresses daily reminder");
                Set(window,"locked",false); Set(window,"sleeping",true);
                Equal(DailyHabit.None,window.PendingDailyCheckIns(evening),"Sleep suppresses daily reminder");
                Set(window,"sleeping",false); Set(window,"dailyOpen",true);
                Equal(DailyHabit.None,window.PendingDailyCheckIns(evening),"Open daily page suppresses balloon"); Set(window,"dailyOpen",false);
                Set(window,"completedNotice",true);
                Equal(DailyHabit.None,window.PendingDailyCheckIns(evening),"Completion notice takes priority"); Set(window,"completedNotice",false);
                clock.StartBreak(); Equal(DailyHabit.None,window.PendingDailyCheckIns(evening),"Break does not get interrupted"); clock.SkipBreak();
                for (int i=0;i<1800;i++) clock.Tick(TimeSpan.FromSeconds(1));
                Equal(DailyHabit.None,window.PendingDailyCheckIns(evening),"Activity reminder takes priority");
                clock.ResetSession(); Equal(Both,window.PendingDailyCheckIns(evening),"Eligible reminder resumes after suppression");
                Call(window,"NotifyDailyCheckIns"); Equal(false,checks.Today.Reminded,"Preview cannot emit or consume real reminder");
                Call(window,"ToggleCheckIn",DailyHabit.Pelvic); Equal(1,checks.Count(DailyHabit.Pelvic),"Floating check-in saves completion");
                Call(window,"ToggleCheckIn",DailyHabit.Pelvic); Equal(0,checks.Count(DailyHabit.Pelvic),"Floating check-in supports undo");
                checks.SetDate(DateTime.Today.AddDays(-1),1500);
                Call(window,"ToggleCheckIn",DailyHabit.Water); Equal(false,checks.Today.WaterDone,"Stale midnight click refreshes instead of completing new day");
            }
            Application.EnableVisualStyles();
            foreach (float scale in new float[]{1,1.5f,2})
            {
                Preferences prefs=new Preferences();
                engine=new DailyCheckInEngine(new DailyCheckInData(),DateTime.Today,1500);
                int saves=0;
                using (DailyCheckInWindow dialog=new DailyCheckInWindow(engine,prefs,scale,delegate { saves++; return ""; }))
                {
                    CheckBox pelvic=(CheckBox)Field(dialog,"pelvic"), water=(CheckBox)Field(dialog,"water");
                    pelvic.Checked=true; water.Checked=true;
                    Equal(1,engine.Count(Both),"Dialog checkboxes update distinct daily record");
                    pelvic.Checked=false; Equal(0,engine.Count(DailyHabit.Pelvic),"Dialog checkbox undo");
                    dialog.SelectTab(1);
                    Equal(1,((DailyCalendar)Field(dialog,"history")).RecordCount,"Calendar uses saved dates");
                    dialog.SelectTab(2);
                    NumericUpDown goal=(NumericUpDown)Field(dialog,"goal"); goal.Value=1.7m;
                    Equal(1500,prefs.DailyWaterTargetMl,"Editing target waits for save");
                    Call(dialog,"ApplySettings"); Equal(1700,engine.Today.WaterTargetMl,"Save applies new target");
                    Equal(false,water.Checked,"Changed target unchecks today's water"); Equal(true,saves>=5,"Changes invoke persistence");
                    SoftButton save=(SoftButton)Field(dialog,"saveSettings");
                    Equal(true,save.Bounds.Bottom<=dialog.ClientSize.Height,"Save button fits scaled dialog");
                    Equal(true,save.Bounds.Top>0,"Save remains accessible above bottom edge");
                    engine.SetDate(DateTime.Today.AddDays(-1),1500);
                    pelvic.Checked=true; Equal(false,engine.Today.PelvicDone,"Stale dialog midnight click only refreshes");
                }
            }
            Equal(settingsWritten,File.GetLastWriteTimeUtc(Path.Combine(folder,"sitless.settings.xml")),"Preview does not save settings");
            Equal(checkInsWritten,File.GetLastWriteTimeUtc(Path.Combine(folder,"sitless.checkins.xml")),"Preview does not save health records");
            Console.WriteLine("Daily check-in tests passed: "+assertions+" assertions."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            // Only delete uniquely named, nonrecursive fixture files in this test's output folder.
            foreach (string file in Directory.GetFiles(folder,prefix+"*")) File.Delete(file);
        }
    }
}
