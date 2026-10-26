using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace StandReminder
{
    internal enum QuietTransition { None, Entered, Exited }

    /// <summary>Mute notifications, without changing the timer's manual pause state.</summary>
    internal sealed class QuietModeState
    {
        private TimeSpan clearElapsed;
        internal bool IsActive { get; private set; }

        internal QuietTransition Observe(bool enabled, bool? fullscreen, TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException("elapsed");
            if (!enabled)
            {
                clearElapsed=TimeSpan.Zero;
                if (!IsActive) return QuietTransition.None;
                IsActive=false; return QuietTransition.Exited;
            }
            if (fullscreen==true)
            {
                clearElapsed=TimeSpan.Zero;
                if (IsActive) return QuietTransition.None;
                IsActive=true; return QuietTransition.Entered;
            }
            if (!IsActive) return QuietTransition.None;
            // A failed sample or a sleep/UI stall cannot prove three clear seconds.
            if (!fullscreen.HasValue || elapsed>TimeSpan.FromSeconds(15))
            { clearElapsed=TimeSpan.Zero; return QuietTransition.None; }
            clearElapsed+=elapsed;
            if (clearElapsed<TimeSpan.FromSeconds(3)) return QuietTransition.None;
            clearElapsed=TimeSpan.Zero; IsActive=false; return QuietTransition.Exited;
        }
    }

    internal static class FullscreenDetector
    {
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int length);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window,ref Point point);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        private static readonly uint OwnProcessId=(uint)Process.GetCurrentProcess().Id;

        // null means temporarily unknown, so an existing quiet session is retained.
        internal static bool? Detect()
        {
            int state;
            if (SHQueryUserNotificationState(out state)==0 && state==3) return true;
            IntPtr window=GetForegroundWindow();
            if (window==IntPtr.Zero) return null;
            uint processId;
            if (GetWindowThreadProcessId(window,out processId)==0) return null;
            if (processId==OwnProcessId || !IsWindowVisible(window) || IsIconic(window)) return false;
            StringBuilder className=new StringBuilder(256);
            if (GetClassName(window,className,className.Capacity)==0) return null;
            if (IsDesktopClass(className.ToString())) return false;
            Rect client;
            Point origin=new Point();
            MonitorInfo info=new MonitorInfo(); info.Size=Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetClientRect(window,out client) || !ClientToScreen(window,ref origin) ||
                !GetMonitorInfo(MonitorFromWindow(window,2),ref info)) return null;
            // Client area excludes title bars: ordinary maximized windows do not qualify.
            return CoversMonitor(new Rectangle(origin.X,origin.Y,client.Right-client.Left,client.Bottom-client.Top),
                Rectangle.FromLTRB(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right,info.Monitor.Bottom));
        }

        internal static bool IsDesktopClass(string name)
        { return name=="Progman" || name=="WorkerW" || name=="Shell_TrayWnd" || name=="Shell_SecondaryTrayWnd"; }

        internal static bool CoversMonitor(Rectangle client,Rectangle monitor)
        {
            const int tolerance=2;
            if (client.Width<=0 || client.Height<=0 || monitor.Width<=0 || monitor.Height<=0) return false;
            return client.Left<=monitor.Left+tolerance && client.Top<=monitor.Top+tolerance &&
                client.Right>=monitor.Right-tolerance && client.Bottom>=monitor.Bottom-tolerance;
        }
    }
}
