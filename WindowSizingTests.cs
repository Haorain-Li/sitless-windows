using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using StandReminder;

internal static class WindowSizingTests
{
    private static int assertions;
    private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
    private static void Equal<T>(T expected,T actual,string label)
    { assertions++; if (!Object.Equals(expected,actual)) throw new Exception(label+": expected "+expected+", actual "+actual); }
    private static object Field(object owner,string name) { return owner.GetType().GetField(name,Hidden).GetValue(owner); }
    private static void Call(object owner,string name) { owner.GetType().GetMethod(name,Hidden).Invoke(owner,null); }
    private static IntPtr Pack(Point point)
    { return new IntPtr(unchecked((int)((uint)(ushort)point.X|((uint)(ushort)point.Y<<16)))); }
    private static void CheckLayout(FloatWindow window)
    {
        foreach (Control control in window.Controls)
        {
            if (!control.Visible) continue;
            Equal(true,window.ClientRectangle.Contains(control.Bounds),"Visible control remains inside resized client: "+control.Text+" "+control.Bounds+" in "+window.ClientRectangle+" outer "+window.Size);
            Equal(true,control.Font.Size>0,"Resized font remains usable");
        }
        foreach (string name in new string[]{"primary","secondary","compactToggle","options"})
        {
            Control control=(Control)Field(window,name);
            Point center=new Point(control.Left+control.Width/2,control.Top+control.Height/2);
            int edge=(int)Math.Round(7*(float)Field(window,"scale"));
            Equal(1,WindowSizing.HitTest(center,window.ClientSize,edge,edge*3),"Button center remains clickable");
        }
    }
    [STAThread]
    public static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Size work=new Size(1920,1080);
            Equal(new Size(344,390),WindowSizing.Restore(new Size(344,390),false,1,work),"Default full size");
            Equal(new Size(420,132),WindowSizing.Restore(new Size(280,88),true,1.5f,work),"DPI-aware compact size");
            Equal(new Size(344,500),WindowSizing.Restore(new Size(int.MaxValue,500),false,1,work),"Invalid width falls back independently");
            Equal(new Size(440,390),WindowSizing.Restore(new Size(440,-1),false,1,work),"Invalid height falls back independently");
            Equal(new Size(300,200),WindowSizing.Restore(new Size(344,390),false,2,new Size(300,200)),"Small work area caps both constraints");
            Equal(new Size(300,200),WindowSizing.Minimum(false,2,new Size(300,200)),"Minimum cannot exceed work area");
            Equal(new Size(1,1),WindowSizing.Maximum(false,1,Size.Empty),"Degenerate work area stays valid");
            Equal(new Size(440,500),WindowSizing.ToLogical(WindowSizing.ToPixels(new Size(440,500),1.5f),1.5f),"Logical size survives DPI roundtrip");
            foreach (Point point in new Point[]{new Point(-1920,-1080),new Point(-1,400),new Point(500,-1),new Point(32767,-32768),Point.Empty})
                Equal(point,WindowSizing.ScreenPoint(Pack(point)),"Signed multi-monitor coordinates decoded");
            Size size=new Size(344,390);
            Point[] points={new Point(3,195),new Point(340,195),new Point(172,3),new Point(172,386),new Point(5,5),new Point(338,5),new Point(5,384),new Point(338,384)};
            int[] hits={10,11,12,15,13,14,16,17};
            for (int i=0;i<points.Length;i++) Equal(hits[i],WindowSizing.HitTest(points[i],size,7,17),"Eight native resize directions");
            Equal(13,WindowSizing.HitTest(new Point(3,14),size,7,17),"Extended corner target along left edge");
            Equal(17,WindowSizing.HitTest(new Point(330,386),size,7,17),"Extended corner target along bottom edge");
            Equal(1,WindowSizing.HitTest(new Point(172,195),size,7,17),"Client area is not a resize target");
            Equal(0,WindowSizing.HitTest(new Point(-1,50),size,7,17),"Outside left excluded");
            Equal(0,WindowSizing.HitTest(new Point(344,50),size,7,17),"Outside right excluded");

            using (StringReader reader=new StringReader("<Preferences><Compact>true</Compact></Preferences>"))
            {
                Preferences migrated=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(reader);
                Equal(344,migrated.FullWidth,"Old XML gets full width default"); Equal(390,migrated.FullHeight,"Old XML gets full height default");
                Equal(280,migrated.CompactWidth,"Old XML gets compact width default"); Equal(88,migrated.CompactHeight,"Old XML gets compact height default");
            }
            foreach (float dpi in new float[]{1,1.5f,2})
            {
                using (FloatWindow window=new FloatWindow(false,true,dpi))
                {
                    window.Show();
                    Preferences prefs=(Preferences)Field(window,"prefs");
                    TimerEngine timer=(TimerEngine)Field(window,"engine"); timer.Tick(TimeSpan.FromSeconds(2));
                    DailyCheckInEngine daily=(DailyCheckInEngine)Field(window,"checkIns"); daily.SetCompleted(DailyHabit.Pelvic,true);
                    Equal(window.Size,window.ClientSize,"Sizing frame adds no visible native border");
                    foreach (Size logical in new Size[]{new Size(320,360),new Size(440,500),new Size(540,360),new Size(320,600)})
                    {
                        window.ClientSize=WindowSizing.ToPixels(logical,dpi); CheckLayout(window);
                        Size expected=WindowSizing.ToLogical(WindowSizing.Restore(logical,false,dpi,Screen.FromRectangle(window.Bounds).WorkingArea.Size),dpi);
                        Equal(expected.Width,prefs.FullWidth,"Resizing remembers full width"); Equal(expected.Height,prefs.FullHeight,"Resizing remembers full height");
                    }
                    window.ClientSize=window.MaximumSize;
                    int cornerEdge=Math.Max((int)Math.Round(7*dpi),(int)Math.Round(7*(float)Field(window,"contentScale")));
                    Point cornerPoint=new Point(cornerEdge-1,cornerEdge-1);
                    Equal(true,window.Region.IsVisible(cornerPoint),"Large rounded corner retains a visible drag target");
                    Equal(1,SendMessage(window.Handle,0x84,IntPtr.Zero,Pack(window.PointToScreen(cornerPoint))).ToInt32(),"Large rounded corner remains client-side");
                    window.ClientSize=WindowSizing.ToPixels(new Size(440,500),dpi);
                    Point testPoint=window.PointToScreen(new Point(2,(int)(100*dpi)));
                    Equal(1,SendMessage(window.Handle,0x84,IntPtr.Zero,Pack(testPoint)).ToInt32(),"Resize edge avoids native activation frame");
                    testPoint=window.PointToScreen(new Point(window.Width-(int)Math.Round(5*dpi),window.Height-(int)Math.Round(5*dpi)));
                    Equal(1,SendMessage(window.Handle,0x84,IntPtr.Zero,Pack(testPoint)).ToInt32(),"Corner resizing avoids native activation frame");
                    Call(window,"ToggleCompact");
                    Equal(WindowSizing.ToPixels(new Size(280,88),dpi),window.ClientSize,"Compact starts with independent default");
                    window.ClientSize=WindowSizing.ToPixels(new Size(380,120),dpi); CheckLayout(window);
                    Equal(380,prefs.CompactWidth,"Compact width remembered separately"); Equal(120,prefs.CompactHeight,"Compact height remembered separately");
                    Call(window,"ToggleCompact"); Equal(WindowSizing.ToPixels(new Size(440,500),dpi),window.ClientSize,"Expanded size restored after mode switch");
                    Call(window,"ToggleCompact"); Equal(WindowSizing.ToPixels(new Size(380,120),dpi),window.ClientSize,"Compact size restored after mode switch");
                    Equal(2d,timer.WorkElapsed.TotalSeconds,"Resize and mode changes preserve elapsed work");
                    // A due reminder temporarily expands; it must not overwrite the compact preference.
                    timer.Configure(TimeSpan.FromSeconds(2),TimeSpan.FromSeconds(3)); Call(window,"LayoutWindow");
                    Equal(WindowSizing.ToPixels(new Size(440,500),dpi),window.ClientSize,"Reminder expands to remembered full size");
                    Equal(380,prefs.CompactWidth,"Temporary expansion preserves compact size");
                    timer.ResetSession(); Call(window,"LayoutWindow");
                    Equal(WindowSizing.ToPixels(new Size(380,120),dpi),window.ClientSize,"Handling reminder restores custom compact size");
                    Call(window,"ResetWindowSize"); Equal(WindowSizing.ToPixels(new Size(280,88),dpi),window.ClientSize,"Reset current mode to default");
                    Equal(440,prefs.FullWidth,"Compact reset does not change full width");
                    Call(window,"ToggleCompact"); Call(window,"ResetWindowSize");
                    Equal(WindowSizing.ToPixels(new Size(344,390),dpi),window.ClientSize,"Full reset restores default");
                    Equal(false,timer.IsPaused,"Resize leaves pause state unchanged"); Equal(1,daily.Count(DailyHabit.Pelvic),"Resize preserves daily check-ins");
                    using (StringWriter writer=new StringWriter())
                    {
                        prefs.FullWidth=440; prefs.FullHeight=500; prefs.CompactWidth=380; prefs.CompactHeight=120;
                        new XmlSerializer(typeof(Preferences)).Serialize(writer,prefs);
                        using (StringReader reader=new StringReader(writer.ToString()))
                        {
                            Preferences restored=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(reader);
                            Equal(440,restored.FullWidth,"Saved full width survives serialization");
                            Equal(120,restored.CompactHeight,"Saved compact height survives serialization");
                        }
                    }
                }
            }
            Console.WriteLine("PASS: "+assertions+" window sizing assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: "+error.GetBaseException()); return 1; }
    }
}
