using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;
using StandReminder;

internal static class FullscreenQuietModeTests
{
    private static int assertions;
    private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Equal<T>(T expected,T actual,string message)
    {
        assertions++;
        if (!Object.Equals(expected,actual)) throw new Exception(message+": expected "+expected+", actual "+actual);
    }
    private static QuietTransition Sample(QuietModeState mode,bool? fullscreen,double seconds)
    { return mode.Observe(true,fullscreen,TimeSpan.FromSeconds(seconds)); }
    private static object Field(object target,string name)
    { return target.GetType().GetField(name,Hidden).GetValue(target); }
    private static void Call(object target,string name)
    { target.GetType().GetMethod(name,Hidden).Invoke(target,null); }

    [STAThread]
    public static int Main()
    {
        try
        {
            Rectangle screen=new Rectangle(0,0,1920,1080);
            Equal(true,FullscreenDetector.CoversMonitor(screen,screen),"Borderless full screen");
            Equal(false,FullscreenDetector.CoversMonitor(new Rectangle(0,30,1920,1010),screen),"Maximized client with title and taskbar excluded");
            Equal(false,FullscreenDetector.CoversMonitor(new Rectangle(100,100,1280,720),screen),"Windowed game excluded");
            Equal(true,FullscreenDetector.CoversMonitor(new Rectangle(-1920,0,1920,1080),new Rectangle(-1920,0,1920,1080)),"Monitor with negative coordinates");
            Equal(true,FullscreenDetector.CoversMonitor(new Rectangle(1920,0,2560,1440),new Rectangle(1920,0,2560,1440)),"Secondary monitor");
            Equal(true,FullscreenDetector.CoversMonitor(new Rectangle(0,0,3840,1080),screen),"Fullscreen spanning screens");
            Equal(true,FullscreenDetector.CoversMonitor(new Rectangle(1,1,1918,1078),screen),"Two pixel edge tolerance");
            Equal(false,FullscreenDetector.CoversMonitor(new Rectangle(0,0,1900,1080),screen),"Partial coverage excluded");
            Equal(false,FullscreenDetector.CoversMonitor(Rectangle.Empty,screen),"Empty client excluded");
            Equal(false,FullscreenDetector.CoversMonitor(screen,Rectangle.Empty),"Empty monitor excluded");
            foreach (string name in new string[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd"})
                Equal(true,FullscreenDetector.IsDesktopClass(name),"Desktop or taskbar excluded");
            Equal(false,FullscreenDetector.IsDesktopClass("GameWindow"),"Game class accepted");

            QuietModeState mode=new QuietModeState();
            Equal(QuietTransition.None,Sample(mode,false,1),"Normal desktop remains normal");
            Equal(QuietTransition.None,mode.Observe(false,true,TimeSpan.Zero),"Disabled mode ignores fullscreen");
            Equal(false,mode.IsActive,"Disabled mode stays inactive");
            Equal(QuietTransition.Entered,Sample(mode,true,0),"Enter immediately including startup");
            Equal(true,mode.IsActive,"Quiet mode active");
            Equal(QuietTransition.None,Sample(mode,true,1),"Repeated fullscreen does not reenter");
            Equal(QuietTransition.None,Sample(mode,false,1),"Brief Alt Tab does not restore");
            Equal(QuietTransition.None,Sample(mode,null,1),"Failed query retains mode");
            Equal(true,mode.IsActive,"Unknown sample cannot release quiet mode");
            Equal(QuietTransition.None,Sample(mode,false,2),"Unknown sample resets exit delay");
            Equal(QuietTransition.None,Sample(mode,true,1),"Returning to fullscreen resets exit delay");
            Equal(QuietTransition.None,Sample(mode,false,1),"First clear second");
            Equal(QuietTransition.None,Sample(mode,false,1),"Second clear second");
            Equal(QuietTransition.Exited,Sample(mode,false,1),"Restore after three stable seconds");
            Equal(false,mode.IsActive,"Quiet mode ended");
            Equal(QuietTransition.None,Sample(mode,false,1),"No repeated exit");
            Equal(QuietTransition.Entered,Sample(mode,true,1),"Next fullscreen session works");
            Equal(QuietTransition.None,Sample(mode,false,3600),"Sleep gap cannot prove stable exit");
            Equal(true,mode.IsActive,"Sleep gap retains mode");
            Equal(QuietTransition.None,Sample(mode,false,2),"Fresh delay after sleep");
            Equal(QuietTransition.Exited,mode.Observe(false,true,TimeSpan.Zero),"Switching off restores immediately");
            assertions++;
            try { Sample(mode,false,-1); throw new Exception("Negative delta accepted"); }
            catch (ArgumentOutOfRangeException) { }

            TimerEngine engine=new TimerEngine(TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(3),new DateTime(2026,10,26));
            Sample(mode,true,0);
            Equal(TimerSignal.ReminderDue,engine.Tick(TimeSpan.FromSeconds(10)),"Work still reaches reminder during quiet mode");
            Equal(10d,engine.TodayWorkElapsed.TotalSeconds,"Quiet time remains tracked desk time");
            engine.Pause();
            Sample(mode,false,3);
            Equal(true,engine.IsPaused,"Leaving quiet mode preserves manual pause");

            XmlSerializer serializer=new XmlSerializer(typeof(Preferences));
            using (StringReader reader=new StringReader("<Preferences><Sound>true</Sound></Preferences>"))
                Equal(true,((Preferences)serializer.Deserialize(reader)).AutoQuietFullscreen,"Old settings opt into fullscreen quiet mode");
            using (StringWriter writer=new StringWriter())
            {
                Preferences settings=new Preferences(); settings.AutoQuietFullscreen=false;
                serializer.Serialize(writer,settings);
                using (StringReader reader=new StringReader(writer.ToString()))
                    Equal(false,((Preferences)serializer.Deserialize(reader)).AutoQuietFullscreen,"Disabled preference survives restart");
            }

            using (FloatWindow window=new FloatWindow(false,true))
            {
                QuietModeState uiMode=(QuietModeState)Field(window,"quiet");
                TimerEngine uiEngine=(TimerEngine)Field(window,"engine");
                Sample(uiMode,true,0);
                uiEngine.Configure(TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(3));
                Equal(TimerSignal.ReminderDue,uiEngine.Tick(TimeSpan.FromSeconds(10)),"UI reaches a real overdue state");
                Call(window,"NotifyDue");
                Equal(true,(bool)Field(window,"deferredNotification"),"Reminder queued silently");
                Equal(false,window.Visible,"Reminder cannot show window during fullscreen");
                Call(window,"NotifyCompleted");
                Equal(true,(bool)Field(window,"deferredNotification"),"Completion also defers");
                Equal(false,window.Visible,"Completion cannot show window during fullscreen");
                Call(window,"FlushQuietRequests");
                Equal(true,(bool)Field(window,"deferredNotification"),"No replay during fullscreen");
                window.GetType().GetField("restoreAfterQuiet",Hidden).SetValue(window,true);
                Call(window,"HideFloat");
                Equal(false,(bool)Field(window,"restoreAfterQuiet"),"Manual hide is preserved on exit");
                uiEngine.Pause();
                Sample(uiMode,false,3);
                Call(window,"FlushQuietRequests");
                Equal(true,(bool)Field(window,"deferredNotification"),"Manually paused overdue reminder waits for resume");
                Equal(false,window.Visible,"Manual pause does not replay a popup");
                uiEngine.ResetSession();
                Call(window,"FlushQuietRequests");
                Equal(false,(bool)Field(window,"deferredNotification"),"Handled or reset reminder is discarded");
                Equal(false,window.Visible,"No stale popup after reset");
            }
            // Read-only native smoke check: no hooks, focus changes, or fullscreen app launch.
            FullscreenDetector.Detect();
            Console.WriteLine("PASS: "+assertions+" fullscreen quiet mode assertions; native probe succeeded");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: "+error.GetBaseException().Message);
            return 1;
        }
    }
}
