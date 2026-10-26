using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("起身 · Sit Less")]
[assembly: System.Reflection.AssemblyDescription("轻量桌面久坐提醒")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace StandReminder
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        internal const int ShowMessage = 0x8001;
        internal const string WindowTitle = "起身 · Sit Less";

        [STAThread]
        private static void Main(string[] args)
        {
            bool demo = Array.IndexOf(args, "--demo") >= 0;
            bool render = Array.IndexOf(args, "--render") >= 0;
            float previewScale=0;
            if (render)
                foreach (string arg in args)
                    if (arg.StartsWith("--render-scale=",StringComparison.Ordinal))
                    {
                        float parsed;
                        if (float.TryParse(arg.Substring(15),NumberStyles.Float,CultureInfo.InvariantCulture,out parsed) && parsed>=0.75f && parsed<=3f)
                            previewScale=parsed;
                    }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\SitLess.Native.1" + (demo ? ".Demo" : render ? ".Render" : ""), out created))
            {
                if (!created)
                {
                    IntPtr previous = FindWindow(null, WindowTitle + (demo ? " · 演示" : ""));
                    if (previous != IntPtr.Zero) PostMessage(previous, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (FloatWindow window = new FloatWindow(demo, render,previewScale))
                {
                    if (render) window.RenderPreviews();
                    else Application.Run(window);
                }
            }
        }
    }

    public sealed class Preferences
    {
        public int WorkMinutes = 30;
        public int BreakMinutes = 3;
        public bool AlwaysOnTop = true;
        public bool Compact = false;
        public bool Sound = false;
        public bool AutoQuietFullscreen = true;
        public int Left = int.MinValue;
        public int Top = int.MinValue;
        public string StatsDate = "";
        public double TodaySeconds = 0;
        public int Completed = 0;
    }

    internal static class Palette
    {
        internal static readonly Color Paper = Color.FromArgb(245, 248, 244);
        internal static readonly Color Card = Color.FromArgb(255, 255, 253);
        internal static readonly Color Ink = Color.FromArgb(32, 54, 43);
        internal static readonly Color Muted = Color.FromArgb(93, 113, 101);
        internal static readonly Color Green = Color.FromArgb(39, 103, 72);
        internal static readonly Color LightGreen = Color.FromArgb(229, 239, 229);
        internal static readonly Color Amber = Color.FromArgb(166, 100, 29);
        internal static readonly Color AmberLight = Color.FromArgb(249, 236, 215);
        internal static readonly Color Line = Color.FromArgb(221, 229, 219);
        internal static Font Font(float pixels, bool bold, float scale)
        { return new Font("Microsoft YaHei UI", pixels * scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right-d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom-d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        internal static void FillRound(Graphics g, Brush b, RectangleF r, float radius)
        { using (GraphicsPath path = Round(r, radius)) g.FillPath(b, path); }
        internal static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (Brush b = new SolidBrush(Green)) g.FillEllipse(b, 1, 1, 30, 30);
                using (Pen p = new Pen(Color.White, 2.4f))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawEllipse(p, 13, 6, 4, 4); g.DrawLine(p,16,13,16,20);
                    g.DrawLine(p,10,16,16,13); g.DrawLine(p,16,13,23,15);
                    g.DrawLine(p,16,20,11,26); g.DrawLine(p,16,20,21,26);
                }
                IntPtr handle = bmp.GetHicon();
                try { using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    }

    internal sealed class SoftButton : Button
    {
        internal bool Primary;
        internal bool Amber;
        private bool hovered, pressed;
        internal SoftButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor=false;
            BackColor = Palette.Paper; ForeColor = Palette.Ink; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { pressed=false; base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (e.KeyCode==Keys.Space && e.Modifiers==Keys.None) { pressed=true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        { if (e.KeyCode==Keys.Space) { pressed=false; Invalidate(); } base.OnKeyUp(e); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed=e.Button==MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { pressed=false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            // ButtonBase is Opaque: native WM_PAINT skips OnPaintBackground, although
            // DrawToBitmap/WM_PRINT may call it. Fill the buffered surface HERE so
            // the area outside the rounded shape is initialized on every repaint.
            using (Brush surface=new SolidBrush(Parent==null ? BackColor : Parent.BackColor))
                e.Graphics.FillRectangle(surface,ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = Primary ? (Amber ? Palette.Amber : Palette.Green) : (hovered ? Palette.Line : Palette.LightGreen);
            if (Primary && hovered) bg=ControlPaint.Light(bg,0.12f);
            if (pressed) bg=ControlPaint.Dark(bg,0.1f);
            if (!Enabled) bg = Palette.Line;
            using (Brush b = new SolidBrush(bg)) Palette.FillRound(e.Graphics,b,new RectangleF(0,0,Width-1,Height-1),8*DeviceScale);
            Color fg = Primary ? Color.White : Palette.Ink;
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled ? fg : Palette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            if (Enabled && Focused && ShowFocusCues)
            {
                float inset=3*DeviceScale;
                Color focus=Primary ? (Amber ? Palette.AmberLight : Palette.LightGreen) : Palette.Green;
                using (Pen pen=new Pen(focus,1.25f*DeviceScale))
                using (GraphicsPath path=Palette.Round(new RectangleF(inset,inset,Width-1-2*inset,Height-1-2*inset),5*DeviceScale))
                    e.Graphics.DrawPath(pen,path);
            }
        }
        internal float DeviceScale = 1;
    }

    internal sealed class FloatWindow : Form
    {
        private readonly bool demo, rendering;
        private readonly string preferencePath;
        private Preferences prefs;
        private readonly TimerEngine engine;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch monotonic = Stopwatch.StartNew();
        private readonly QuietModeState quiet = new QuietModeState();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolTip tips = new ToolTip();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly Dictionary<Control,Font> ownedFonts = new Dictionary<Control,Font>();
        private readonly Label title = new Label(), tagline = new Label(), status = new Label(), clock = new Label(), detail = new Label(), stats = new Label(), tracked = new Label(), statsTitle = new Label(), trackedTitle = new Label();
        private readonly SoftButton options = new SoftButton(), hide = new SoftButton(), primary = new SoftButton(), secondary = new SoftButton();
        private float scale;
        private bool compact, locked, sleeping, completedNotice, exiting, ready, settingsOpen;
        private bool restoreAfterQuiet, deferredNotification, manualShow;
        private string saveError = "";
        private int ticks;
        private double progress;
        private Size regionSize;
        private bool Blocked { get { return locked || sleeping; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void SetVisibleCore(bool value)
        {
            if (value && quiet!=null && quiet.IsActive && !manualShow && !rendering) value=false;
            base.SetVisibleCore(value);
        }
        protected override CreateParams CreateParams
        { get { CreateParams cp=base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

        internal FloatWindow(bool isDemo, bool isRendering,float previewScale=0)
        {
            demo=isDemo; rendering=isRendering;
            preferencePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sitless.settings.xml");
            prefs=LoadPreferences();
            if (rendering || demo) prefs=new Preferences();
            engine=new TimerEngine(TimeSpan.FromMinutes(prefs.WorkMinutes),TimeSpan.FromMinutes(prefs.BreakMinutes),DateTime.Today);
            if (demo) engine.Configure(TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(5));
            DateTime date;
            if (DateTime.TryParseExact(prefs.StatsDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))
                try { engine.RestoreDailyStats(date,TimeSpan.FromSeconds(prefs.TodaySeconds),prefs.Completed); } catch (ArgumentException) { }
            using (Graphics g=CreateGraphics()) scale=g.DpiX/96f;
            if (rendering && previewScale>0) scale=previewScale;
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None;
            Text=Program.WindowTitle+(demo ? " · 演示" : ""); ShowInTaskbar=true; BackColor=Palette.Paper;
            Icon=Palette.MakeIcon(); TopMost=prefs.AlwaysOnTop; StartPosition=FormStartPosition.Manual;
            DoubleBuffered=true; KeyPreview=true;
            Controls.AddRange(new Control[]{title,tagline,status,clock,detail,stats,tracked,statsTitle,trackedTitle,options,hide,primary,secondary});
            title.Text="起身"; title.ForeColor=Palette.Ink; tagline.Text="SIT LESS"; tagline.ForeColor=Palette.Muted;
            foreach (Label label in new Label[]{title,tagline,status,clock,detail,stats,tracked,statsTitle,trackedTitle}) { label.AutoSize=false; label.BackColor=Color.Transparent; label.MouseDown+=DragWindow; }
            status.ForeColor=Palette.Muted; clock.ForeColor=Palette.Ink; detail.ForeColor=Palette.Muted;
            stats.ForeColor=tracked.ForeColor=Palette.Ink; statsTitle.ForeColor=trackedTitle.ForeColor=Palette.Muted;
            statsTitle.Text="今日活动"; trackedTitle.Text="今日桌前计时";
            options.Text="···"; options.AccessibleName="打开菜单"; hide.Text="−"; hide.AccessibleName="收起到托盘";
            primary.Primary=true;
            options.Click+=delegate { menu.Show(options,new Point(0,options.Height)); };
            hide.Click+=delegate { HideFloat(); if (!rendering && !quiet.IsActive) tray.ShowBalloonTip(3000,"起身仍在计时","双击托盘里的绿色图标，可找回浮窗。",ToolTipIcon.Info); };
            primary.Click+=delegate { PrimaryAction(); }; secondary.Click+=delegate { SecondaryAction(); };
            tips.SetToolTip(options,"设置 Ctrl+, · 精简 Ctrl+M · 暂停 Ctrl+P"); tips.SetToolTip(hide,"收起到托盘，继续提醒（Esc）");
            tips.SetToolTip(clock,"桌前计时估计，不检测坐姿；拖动这里移动浮窗");
            MouseDown+=DragWindow; Resize+=delegate { UpdateRegion(); };
            menu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { BuildMenu(); e.Cancel=false; };
            tray.Icon=Icon; tray.Text="起身 · 正在计时"; tray.ContextMenuStrip=menu; tray.Visible=!rendering;
            tray.DoubleClick+=delegate { ShowFloat(true); }; tray.BalloonTipClicked+=delegate { ShowFloat(true); };
            timer.Interval=1000; timer.Tick+=OnTick;
            SystemEvents.SessionSwitch+=SessionChanged; SystemEvents.PowerModeChanged+=PowerChanged;
            KeyDown+=delegate(object s,KeyEventArgs e)
            {
                if (e.Control && e.KeyCode==Keys.Oemcomma) { OpenSettings(); e.SuppressKeyPress=true; }
                if (e.Control && e.KeyCode==Keys.M) { ToggleCompact(); e.SuppressKeyPress=true; }
                if (e.Control && e.KeyCode==Keys.P) { TogglePause(); e.SuppressKeyPress=true; }
                if (e.KeyCode==Keys.Escape) { HideFloat(); e.Handled=true; }
                if (e.KeyCode==Keys.Space && !(ActiveControl is Button)) { SecondaryAction(); e.Handled=true; }
            };
            UpdateUi(true);
            Rectangle area=Screen.PrimaryScreen.WorkingArea;
            Location=prefs.Left==int.MinValue ? new Point(area.Right-Width-S(24),area.Bottom-Height-S(40)) : new Point(prefs.Left,prefs.Top);
            ClampToScreen(); RefreshQuietMode(TimeSpan.Zero); ready=true;
            if (!rendering) timer.Start();
        }

        private Preferences LoadPreferences()
        {
            try
            {
                if (!File.Exists(preferencePath) || new FileInfo(preferencePath).Length>32768) return new Preferences();
                XmlReaderSettings settings=new XmlReaderSettings(); settings.DtdProcessing=DtdProcessing.Prohibit; settings.XmlResolver=null;
                using (XmlReader reader=XmlReader.Create(preferencePath,settings))
                {
                    Preferences p=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(reader);
                    if (p.WorkMinutes<5 || p.WorkMinutes>120 || p.BreakMinutes<1 || p.BreakMinutes>20) return new Preferences();
                    if (double.IsNaN(p.TodaySeconds) || double.IsInfinity(p.TodaySeconds) || p.TodaySeconds<0 || p.TodaySeconds>86400) p.TodaySeconds=0;
                    return p;
                }
            }
            catch (Exception e) { if (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is XmlException) return new Preferences(); throw; }
        }

        private void SavePreferences()
        {
            if (demo || rendering) return;
            prefs.Left=Left; prefs.Top=Top; prefs.StatsDate=engine.LocalDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            prefs.TodaySeconds=engine.TodayWorkElapsed.TotalSeconds; prefs.Completed=engine.CompletedBreaksToday;
            try
            {
                string tmp=preferencePath+".tmp";
                using (FileStream stream=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)) new XmlSerializer(typeof(Preferences)).Serialize(stream,prefs);
                if (File.Exists(preferencePath)) File.Replace(tmp,preferencePath,null); else File.Move(tmp,preferencePath);
                saveError=""; tips.SetToolTip(options,"设置 Ctrl+, · 精简 Ctrl+M · 暂停 Ctrl+P");
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)) throw;
                saveError="当前文件夹不可写，设置仅在本次运行有效。"; tips.SetToolTip(options,saveError);
            }
        }

        private int S(float value) { return (int)Math.Round(value*scale); }
        private void Place(Control c,int x,int y,int w,int h,float font,bool bold)
        {
            c.SetBounds(S(x),S(y),S(w),S(h));
            SetOwnedFont(c,Palette.Font(font,bold,scale));
            SoftButton b=c as SoftButton; if (b!=null) b.DeviceScale=scale;
        }
        private void SetOwnedFont(Control c,Font font)
        {
            Font old; ownedFonts.TryGetValue(c,out old);
            // WinForms keeps its existing Font reference when a value-equal Font is assigned.
            if (old!=null && old.Equals(font)) { font.Dispose(); return; }
            c.Font=font; ownedFonts[c]=font;
            if (old!=null) old.Dispose();
        }
        private void LayoutWindow()
        {
            SuspendLayout();
            compact=prefs.Compact && engine.Phase!=TimerPhase.Reminder && !completedNotice;
            ClientSize=new Size(S(compact ? 280 : 344),S(compact ? 88 : 320));
            title.Visible=tagline.Visible=detail.Visible=stats.Visible=tracked.Visible=statsTitle.Visible=trackedTitle.Visible=hide.Visible=!compact;
            if (!compact)
            {
                Place(title,47,18,48,25,16,true); Place(tagline,96,22,80,18,9,true);
                Place(options,266,16,28,28,19,true); Place(hide,302,16,26,28,17,false);
                Place(status,23,65,296,23,12,false); Place(clock,18,88,306,66,54,true);
                SetOwnedFont(clock,new Font("Segoe UI",S(54),FontStyle.Bold,GraphicsUnit.Pixel));
                Place(detail,24,160,296,22,11,false);
                Place(statsTitle,35,211,128,18,10,false); Place(stats,35,231,128,23,15,true);
                Place(trackedTitle,190,211,128,18,10,false); Place(tracked,190,231,128,23,15,true);
                Place(secondary,22,272,98,34,12,false); Place(primary,130,272,192,34,12,true);
            }
            else
            {
                Place(clock,49,11,118,35,27,true);
                SetOwnedFont(clock,new Font("Segoe UI",S(27),FontStyle.Bold,GraphicsUnit.Pixel));
                Place(status,51,52,118,19,10,false); Place(options,244,9,24,24,16,true);
                Place(secondary,174,45,41,30,12,true); Place(primary,223,45,45,30,11,true);
            }
            ResumeLayout(); UpdateRegion(); if (ready) ClampToScreen(); Invalidate();
        }
        private void UpdateRegion()
        {
            if (Width<1 || Height<1 || regionSize==Size) return;
            // SetWindowRgn can send another resize message even when the size is unchanged.
            regionSize=Size;
            using (GraphicsPath path=Palette.Round(new RectangleF(0,0,Width,Height),S(compact ? 15 : 18)))
            { Region old=Region; Region=new Region(path); if (old!=null) old.Dispose(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            using (Pen p=new Pen(Palette.Line,scale)) using (GraphicsPath path=Palette.Round(new RectangleF(.5f,.5f,Width-1,Height-1),S(compact?15:18))) g.DrawPath(p,path);
            g.ScaleTransform(scale,scale);
            Color accent=engine.Phase==TimerPhase.Reminder ? Palette.Amber : Palette.Green;
            if (!compact)
            {
                using (Brush b=new SolidBrush(Palette.Green)) g.FillEllipse(b,23,24,15,15);
                using (Pen p=new Pen(Color.White,1.5f)) { g.DrawLine(p,27,31,30,34); g.DrawLine(p,30,34,34,28); }
                using (Brush b=new SolidBrush(Palette.Line)) Palette.FillRound(g,b,new RectangleF(23,190,298,4),2);
                if (progress>0) using (Brush b=new SolidBrush(accent)) Palette.FillRound(g,b,new RectangleF(23,190,(float)Math.Max(4,298*progress),4),2);
                using (Brush b=new SolidBrush(Palette.LightGreen)) Palette.FillRound(g,b,new RectangleF(22,204,300,57),10);
                using (Pen p=new Pen(Color.FromArgb(208,223,208))) g.DrawLine(p,172,216,172,249);
            }
            else
            {
                using (Pen p=new Pen(Palette.Line,3)) g.DrawEllipse(p,17,27,20,20);
                using (Pen p=new Pen(accent,3)) { p.StartCap=p.EndCap=LineCap.Round; g.DrawArc(p,17,27,20,20,-90,(float)Math.Max(6,360*progress)); }
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            int seconds=(int)Math.Ceiling(Math.Max(0,time.TotalSeconds));
            return (seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
        }
        private void UpdateUi(bool relayout)
        {
            bool mustCompact=prefs.Compact && engine.Phase!=TimerPhase.Reminder && !completedNotice;
            if (relayout || compact!=mustCompact) LayoutWindow();
            bool paused=engine.IsPaused || Blocked;
            bool rest=engine.Phase==TimerPhase.Break, due=engine.Phase==TimerPhase.Reminder;
            string state=Blocked ? "系统暂停 · 锁屏 / 休眠" : quiet.IsActive ? "全屏免打扰 · 提醒已静音" : completedNotice ? "活动计时完成，欢迎回来" : paused ? "已暂停 · 随时继续" : rest ? "活动中 · 走动或舒展一下" : due ? "该活动一下了" : "距离下次活动";
            status.Text=compact ? (Blocked?"系统暂停":quiet.IsActive?"全屏免打扰":paused?"计时已暂停":rest?"活动计时中":"距离下次活动") : state;
            clock.Text=completedNotice?"完成 ✓":FormatTime(rest?engine.BreakRemaining:engine.WorkRemaining);
            status.ForeColor=due?Palette.Amber:Palette.Muted;
            clock.ForeColor=due?Palette.Amber:Palette.Ink;
            if (due) detail.Text="已连续计时 "+(int)engine.WorkElapsed.TotalMinutes+" 分钟，起身走一走";
            else if (rest) detail.Text="按自身能力轻松活动；完成后等待你继续";
            else if (completedNotice) detail.Text="准备好后，点「继续计时」开始下一轮";
            else detail.Text=demo?"演示模式：10 秒提醒 / 5 秒活动": "每 "+prefs.WorkMinutes+" 分钟提醒 · 活动 "+prefs.BreakMinutes+" 分钟";
            stats.Text=engine.CompletedBreaksToday+" 次"+(demo?" · 演示":"");
            int minutes=(int)engine.TodayWorkElapsed.TotalMinutes;
            tracked.Text=minutes<60 ? minutes+" 分钟" : (minutes/60)+" 时 "+(minutes%60)+" 分";
            primary.Amber=due;
            primary.Text=compact?(rest?"结束":"活动"):(completedNotice?"继续计时  →":rest?"提前结束活动":due?"开始活动  →":"起身活动  →");
            secondary.Text=compact?(paused?"▶":"Ⅱ"):(completedNotice?"收起":due?"5 分钟后":paused?"继续":"暂停");
            primary.AccessibleName=completedNotice?"继续计时":rest?"结束本次活动（不计完成）":"开始活动";
            secondary.AccessibleName=completedNotice?"收起到托盘":due?"延后五分钟":paused?"继续计时":"暂停计时";
            tips.SetToolTip(primary,rest?"提前结束不计次数，保留已有桌前计时":completedNotice?"开始新一轮桌前计时":"开始活动倒计时，请根据自身能力走动或舒展");
            tips.SetToolTip(secondary,completedNotice?"收起到托盘，回来后可继续":due?"五分钟后再次提醒，不清空已有计时":paused?"继续计时":"暂停，暂时离开时可使用");
            primary.Enabled=secondary.Enabled=!Blocked;
            progress=rest?engine.BreakElapsed.TotalSeconds/engine.ActiveBreakDuration.TotalSeconds:Math.Min(1,engine.WorkElapsed.TotalSeconds/engine.WorkDuration.TotalSeconds);
            string trayText="起身 · "+(quiet.IsActive?"全屏免打扰（仍计时 / 保留暂停）":paused?"已暂停":due?"该活动了":rest?"活动 "+clock.Text:clock.Text+" 后活动");
            if (tray.Text!=trayText) tray.Text=trayText;
            Invalidate(new Rectangle(0,S(compact?24:188),Width,S(compact?28:8)));
        }
        private void OnTick(object sender,EventArgs e)
        {
            TimeSpan elapsed=monotonic.Elapsed; monotonic.Restart();
            engine.SetLocalDate(DateTime.Today);
            RefreshQuietMode(elapsed);
            TimerSignal signal=Blocked?TimerSignal.None:engine.Tick(elapsed);
            if (signal==TimerSignal.ReminderDue) NotifyDue();
            if (signal==TimerSignal.BreakCompleted)
            {
                completedNotice=true; engine.Pause();
                NotifyCompleted(); SavePreferences();
            }
            FlushQuietRequests();
            UpdateUi(false); if (++ticks%60==0) SavePreferences();
        }
        private void RefreshQuietMode(TimeSpan elapsed)
        {
            if (rendering) return;
            bool? fullscreen=!prefs.AutoQuietFullscreen || Blocked ? (bool?)null : FullscreenDetector.Detect();
            QuietTransition change=quiet.Observe(prefs.AutoQuietFullscreen,fullscreen,elapsed);
            if (change==QuietTransition.Entered)
            {
                restoreAfterQuiet=restoreAfterQuiet || Visible || !ready;
                if (engine.Phase==TimerPhase.Reminder || completedNotice) deferredNotification=true;
                TopMost=false;
                menu.Close();
                // Removing the icon also dismisses an already displayed balloon.
                tray.Visible=false; tray.Visible=true;
                Hide();
            }
            else if (change==QuietTransition.Exited) TopMost=prefs.AlwaysOnTop;
            // A user can open settings from the tray; hide again when the game is foreground.
            else if (quiet.IsActive && Visible && !ContainsFocus && !settingsOpen) Hide();
        }
        private void FlushQuietRequests()
        {
            if (quiet.IsActive || Blocked) return;
            if (deferredNotification)
            {
                if (completedNotice) { deferredNotification=false; NotifyCompleted(); }
                else if (engine.Phase==TimerPhase.Reminder)
                {
                    if (!engine.IsPaused) { deferredNotification=false; NotifyDue(); }
                }
                else deferredNotification=false;
            }
            if (restoreAfterQuiet) { restoreAfterQuiet=false; ShowFloat(false); }
        }
        private void NotifyCompleted()
        {
            if (quiet.IsActive || Blocked) { deferredNotification=true; return; }
            deferredNotification=false;
            if (prefs.Sound) SystemSounds.Asterisk.Play();
            if (!rendering) tray.ShowBalloonTip(4000,"活动计时完成","回来后点击「继续计时」，开始下一轮。",ToolTipIcon.Info);
            ShowFloat(false);
        }
        private void NotifyDue()
        {
            if (quiet.IsActive || Blocked) { deferredNotification=true; return; }
            deferredNotification=false;
            if (prefs.Sound) SystemSounds.Asterisk.Play();
            if (!rendering) tray.ShowBalloonTip(5000,"该活动一下了","起身走动，或按自身能力舒展。点击浮窗开始活动计时。",ToolTipIcon.Info);
            UpdateUi(false); ShowFloat(false);
        }
        private void PrimaryAction()
        {
            if (Blocked) return;
            if (completedNotice) { completedNotice=false; engine.Resume(); }
            else if (engine.Phase==TimerPhase.Break) engine.SkipBreak();
            else engine.StartBreak();
            monotonic.Restart(); UpdateUi(true); SavePreferences();
        }
        private void SecondaryAction()
        {
            if (Blocked) return;
            if (completedNotice) { HideFloat(); return; }
            if (engine.Phase==TimerPhase.Reminder) engine.Snooze(TimeSpan.FromMinutes(5));
            else { engine.TogglePause(); if (!engine.IsPaused) completedNotice=false; }
            monotonic.Restart(); UpdateUi(false); SavePreferences();
        }
        private void BuildMenu()
        {
            // Clear alone detaches items without releasing their event handlers and images.
            while (menu.Items.Count>0) { ToolStripItem item=menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose(); }
            menu.Items.Add("显示浮窗",null,delegate { ShowFloat(true); });
            ToolStripMenuItem settings=new ToolStripMenuItem("设置与说明…",null,delegate { OpenSettings(); }); settings.ShortcutKeyDisplayString="Ctrl+,"; menu.Items.Add(settings);
            ToolStripMenuItem small=new ToolStripMenuItem("精简浮窗"); small.Checked=prefs.Compact;
            small.ShortcutKeyDisplayString="Ctrl+M"; small.Click+=delegate { ToggleCompact(); }; menu.Items.Add(small);
            ToolStripMenuItem pin=new ToolStripMenuItem("浮窗置顶"); pin.Checked=prefs.AlwaysOnTop;
            pin.Click+=delegate { prefs.AlwaysOnTop=!prefs.AlwaysOnTop; TopMost=prefs.AlwaysOnTop && !quiet.IsActive; SavePreferences(); }; menu.Items.Add(pin);
            ToolStripMenuItem autoQuiet=new ToolStripMenuItem("全屏自动免打扰"); autoQuiet.Checked=prefs.AutoQuietFullscreen;
            autoQuiet.Click+=delegate { prefs.AutoQuietFullscreen=!prefs.AutoQuietFullscreen; RefreshQuietMode(TimeSpan.Zero); FlushQuietRequests(); UpdateUi(false); SavePreferences(); }; menu.Items.Add(autoQuiet);
            if (quiet.IsActive) { ToolStripMenuItem info=new ToolStripMenuItem("正在免打扰 · 提醒已静音"); info.Enabled=false; menu.Items.Add(info); }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem pause=new ToolStripMenuItem(engine.IsPaused?"继续计时":"暂停计时",null,delegate { TogglePause(); }); pause.Enabled=!Blocked; pause.ShortcutKeyDisplayString="Ctrl+P"; menu.Items.Add(pause);
            menu.Items.Add("重新开始本轮",null,delegate { if (!Blocked) { completedNotice=false; engine.ResetSession(); monotonic.Restart(); UpdateUi(true); SavePreferences(); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出起身",null,delegate { exiting=true; Close(); });
        }
        private void ToggleCompact()
        { prefs.Compact=!prefs.Compact; UpdateUi(true); SavePreferences(); }
        private void TogglePause()
        { if (Blocked) return; engine.TogglePause(); if (!engine.IsPaused) completedNotice=false; monotonic.Restart(); UpdateUi(false); SavePreferences(); }
        private void OpenSettings()
        {
            if (settingsOpen) return; settingsOpen=true;
            try
            {
                using (SettingsWindow settings=new SettingsWindow(prefs,scale,saveError))
                {
                    if (settings.ShowDialog(this)==DialogResult.OK)
                    {
                        TimerPhase previous=engine.Phase;
                        prefs.WorkMinutes=settings.WorkMinutes; prefs.BreakMinutes=settings.BreakMinutes;
                        prefs.Sound=settings.SoundOn; prefs.AlwaysOnTop=settings.PinOn; prefs.Compact=settings.CompactOn;
                        prefs.AutoQuietFullscreen=settings.AutoQuietOn;
                        engine.Configure(demo?TimeSpan.FromSeconds(10):TimeSpan.FromMinutes(prefs.WorkMinutes),demo?TimeSpan.FromSeconds(5):TimeSpan.FromMinutes(prefs.BreakMinutes));
                        RefreshQuietMode(TimeSpan.Zero); TopMost=prefs.AlwaysOnTop && !quiet.IsActive; UpdateUi(true); SavePreferences();
                        if (previous!=TimerPhase.Reminder && engine.Phase==TimerPhase.Reminder) NotifyDue();
                    }
                }
            }
            finally { settingsOpen=false; }
        }
        private void ShowFloat(bool activate)
        {
            if (IsDisposed || (!activate && quiet.IsActive)) return;
            manualShow=activate;
            try { ClampToScreen(); if (!Visible) Show(); if (activate) { WindowState=FormWindowState.Normal; Activate(); } }
            finally { manualShow=false; }
        }
        private void HideFloat()
        { restoreAfterQuiet=false; Hide(); }
        private void ClampToScreen()
        {
            Rectangle area=Screen.FromRectangle(Bounds).WorkingArea;
            Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
        }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp);
        private void DragWindow(object sender,MouseEventArgs e)
        { if (e.Button==MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg==Program.ShowMessage) { ShowFloat(true); return; }
            base.WndProc(ref m);
            if (m.Msg==0x232 && ready) { ClampToScreen(); SavePreferences(); }
            if (m.Msg==0x7E && ready) ClampToScreen();
        }
        private void Dispatch(Action action)
        { if (IsDisposed || !IsHandleCreated) return; try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        private void SessionChanged(object sender,SessionSwitchEventArgs e)
        {
            Dispatch(delegate
            {
                if (e.Reason==SessionSwitchReason.SessionLock || e.Reason==SessionSwitchReason.RemoteDisconnect) locked=true;
                if (e.Reason==SessionSwitchReason.SessionUnlock || e.Reason==SessionSwitchReason.RemoteConnect) locked=false;
                monotonic.Restart(); UpdateUi(false);
            });
        }
        private void PowerChanged(object sender,PowerModeChangedEventArgs e)
        { Dispatch(delegate { if (e.Mode==PowerModes.Suspend) sleeping=true; if (e.Mode==PowerModes.Resume) sleeping=false; monotonic.Restart(); UpdateUi(false); }); }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; HideFloat(); return; }
            SavePreferences(); base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop(); timer.Dispose(); SystemEvents.SessionSwitch-=SessionChanged; SystemEvents.PowerModeChanged-=PowerChanged;
                tray.Visible=false; tray.Dispose(); tips.Dispose(); menu.Dispose();
                if (Icon!=null) Icon.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) { foreach (Font font in ownedFonts.Values) font.Dispose(); ownedFonts.Clear(); }
        }
        internal void RenderPreviews()
        {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews"); Directory.CreateDirectory(folder);
            Show(); UpdateUi(true); RenderTo(folder,"floating.png");
            // Reveal keyboard focus cues in this preview window only.
            primary.Focus(); SendMessage(Handle,0x127,new IntPtr(0x10002),IntPtr.Zero);
            RenderTo(folder,"focused.png");
            SendMessage(Handle,0x127,new IntPtr(0x10001),IntPtr.Zero);
            engine.Pause(); UpdateUi(false); RenderTo(folder,"paused.png"); engine.Resume();
            prefs.Compact=true; UpdateUi(true); RenderTo(folder,"compact.png");
            prefs.Compact=false; for (int i=0;i<1800;i++) engine.Tick(TimeSpan.FromSeconds(1));
            UpdateUi(true); RenderTo(folder,"reminder.png");
            engine.StartBreak(); UpdateUi(true); RenderTo(folder,"break.png");
            for (int i=0;i<180;i++) engine.Tick(TimeSpan.FromSeconds(1));
            completedNotice=true; engine.Pause(); UpdateUi(true); RenderTo(folder,"completed.png");
            using (SettingsWindow settings=new SettingsWindow(prefs,scale,""))
            { settings.Show(this); settings.DrawPreview(Path.Combine(folder,"settings.png")); settings.DrawPreview(Path.Combine(folder,"guide.png"),true); settings.Hide(); }
            Hide();
        }
        private void RenderTo(string folder,string name)
        { Refresh(); using (Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,ClientRectangle); bitmap.Save(Path.Combine(folder,name),System.Drawing.Imaging.ImageFormat.Png); } }
    }

    internal sealed class RoundedCard : Panel
    {
        internal float DeviceScale=1;
        internal RoundedCard()
        { BackColor=Palette.Paper; DoubleBuffered=true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using (GraphicsPath path=Palette.Round(new RectangleF(.5f,.5f,Width-1,Height-1),12*DeviceScale))
            using (Brush brush=new SolidBrush(Palette.Card))
            using (Pen pen=new Pen(Palette.Line,DeviceScale))
            { e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(pen,path); }
        }
    }

    internal sealed class SettingsWindow : Form
    {
        private readonly NumericUpDown work=new NumericUpDown(), rest=new NumericUpDown();
        private readonly CheckBox pin=new CheckBox(), sound=new CheckBox(), small=new CheckBox(), autoQuiet=new CheckBox();
        private readonly float scale;
        private readonly List<Font> ownedFonts=new List<Font>();
        private readonly SoftButton guideTab=new SoftButton();
        private Control placement;
        internal int WorkMinutes { get { return (int)work.Value; } }
        internal int BreakMinutes { get { return (int)rest.Value; } }
        internal bool PinOn { get { return pin.Checked; } }
        internal bool SoundOn { get { return sound.Checked; } }
        internal bool CompactOn { get { return small.Checked; } }
        internal bool AutoQuietOn { get { return autoQuiet.Checked; } }
        private int S(float v) { return (int)Math.Round(v*scale); }
        private void Place(Control c,int x,int y,int w,int h,float size,bool bold)
        { c.SetBounds(S(x),S(y),S(w),S(h)); Font font=Palette.Font(size,bold,scale); c.Font=font; ownedFonts.Add(font); placement.Controls.Add(c); }
        private void TextAt(string text,int x,int y,int w,int h,float size,bool bold,Color color)
        { Label label=new Label(); label.Text=text; label.ForeColor=color; Place(label,x,y,w,h,size,bold); }
        internal SettingsWindow(Preferences p,float uiScale,string error)
        {
            scale=uiScale; placement=this; Text="起身 · 设置与说明 · v"+typeof(Program).Assembly.GetName().Version.ToString(3); BackColor=Palette.Paper; ForeColor=Palette.Ink;
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=MinimizeBox=false;
            ShowInTaskbar=false; StartPosition=FormStartPosition.CenterParent;
            int maxHeight=Screen.FromPoint(Cursor.Position).WorkingArea.Height-SystemInformation.CaptionHeight-SystemInformation.FixedFrameBorderSize.Height*2-S(24);
            ClientSize=new Size(S(480),Math.Min(S(620),maxHeight));
            TextAt("给身体一点活动时间。",24,19,432,35,23,true,Palette.Ink);
            TextAt("按自己的节奏工作，也记得偶尔起身。",25,61,430,24,12,false,Palette.Muted);
            SoftButton preferencesTab=new SoftButton();
            preferencesTab.Text="偏好设置"; guideTab.Text="使用说明";
            preferencesTab.Primary=true; preferencesTab.DeviceScale=guideTab.DeviceScale=scale;
            Place(preferencesTab,24,103,210,34,12,true); Place(guideTab,244,103,212,34,12,true);
            RoundedCard card=new RoundedCard(); card.DeviceScale=scale;
            card.SetBounds(S(24),S(151),S(432),ClientSize.Height-S(249)); Controls.Add(card);
            Panel body=new Panel(); body.BackColor=Palette.Card; body.AutoScroll=true;
            body.SetBounds(S(12),S(12),card.Width-S(24),card.Height-S(24)); card.Controls.Add(body);
            Panel preferences=new Panel(), guide=new Panel();
            preferences.BackColor=guide.BackColor=Palette.Card;
            preferences.Size=new Size(S(384),S(344)); guide.Size=new Size(S(384),S(420));
            body.Controls.Add(preferences); body.Controls.Add(guide); guide.Visible=false;
            preferencesTab.Click+=delegate { guide.Visible=false; preferences.Visible=true; preferencesTab.Primary=true; guideTab.Primary=false; body.AutoScrollPosition=Point.Empty; preferencesTab.Invalidate(); guideTab.Invalidate(); };
            guideTab.Click+=delegate { preferences.Visible=false; guide.Visible=true; preferencesTab.Primary=false; guideTab.Primary=true; body.AutoScrollPosition=Point.Empty; preferencesTab.Invalidate(); guideTab.Invalidate(); };
            placement=preferences;
            TextAt("提醒节奏",6,2,370,24,13,true,Palette.Ink);
            SoftButton regular=new SoftButton(); regular.Text="日常节奏\n30 分钟 / 3 分钟"; regular.DeviceScale=scale; Place(regular,6,34,180,50,12,false);
            SoftButton focused=new SoftButton(); focused.Text="专注办公\n50 分钟 / 5 分钟"; focused.DeviceScale=scale; Place(focused,198,34,180,50,12,false);
            regular.Click+=delegate { work.Value=30; rest.Value=3; }; focused.Click+=delegate { work.Value=50; rest.Value=5; };
            TextAt("提醒间隔 / 分钟",6,103,180,23,11,false,Palette.Muted); Place(work,6,129,180,30,15,true);
            work.Minimum=5; work.Maximum=120; work.Value=p.WorkMinutes;
            TextAt("活动时长 / 分钟",198,103,180,23,11,false,Palette.Muted); Place(rest,198,129,180,30,15,true);
            rest.Minimum=1; rest.Maximum=20; rest.Value=p.BreakMinutes;
            EventHandler highlightPreset=delegate
            {
                regular.Primary=work.Value==30 && rest.Value==3;
                focused.Primary=work.Value==50 && rest.Value==5;
                regular.Invalidate(); focused.Invalidate();
            };
            work.ValueChanged+=highlightPreset; rest.ValueChanged+=highlightPreset; highlightPreset(null,EventArgs.Empty);
            work.BorderStyle=rest.BorderStyle=BorderStyle.FixedSingle;
            work.BackColor=rest.BackColor=Palette.Paper; work.ForeColor=rest.ForeColor=Palette.Ink;
            work.AccessibleName="提醒间隔，分钟"; rest.AccessibleName="活动时长，分钟";
            TextAt("间隔 5–120 分钟 · 活动 1–20 分钟\n保留已累计计时；活动时长从下一次生效。",6,169,372,39,10,false,Palette.Muted);
            TextAt("使用偏好",6,217,370,24,13,true,Palette.Ink);
            pin.Text="浮窗始终置顶"; pin.Checked=p.AlwaysOnTop; Place(pin,6,253,187,25,12,false);
            sound.Text="播放提示音"; sound.Checked=p.Sound; Place(sound,198,253,180,25,12,false);
            small.Text="精简浮窗（到时自动展开）"; small.Checked=p.Compact; Place(small,6,286,372,25,12,false);
            autoQuiet.Text="全屏自动免打扰（游戏 / 视频 / 演示）"; autoQuiet.Checked=p.AutoQuietFullscreen; Place(autoQuiet,6,319,372,25,12,false);
            placement=guide;
            TextAt("怎样算完成一次活动？",6,2,372,24,13,true,Palette.Ink);
            TextAt("提醒后，手动开始活动倒计时；完整结束才计入次数。\n提前结束保留桌前计时。完成后，点「继续计时」开始下一轮。",6,35,372,55,11,false,Palette.Muted);
            TextAt("提醒节奏的依据",6,108,372,24,13,true,Palette.Ink);
            TextAt("30 分钟提醒参考 Mayo Clinic，3 分钟是可调预设；\n50 / 5 办公预设参考 HSE 的屏幕工作休息建议。\nWHO 建议减少久坐，未规定统一的连续久坐时限。",6,141,372,64,11,false,Palette.Muted);
            LinkLabel sources=new LinkLabel(); sources.Text="WHO 指南     Mayo Clinic     HSE 建议"; sources.LinkColor=Palette.Green;
            sources.Links.Clear(); sources.Links.Add(0,6,"https://www.ncbi.nlm.nih.gov/books/NBK566046/"); sources.Links.Add(11,11,"https://www.mayoclinic.org/healthy-lifestyle/adult-health/expert-answers/sitting/faq-20058005"); sources.Links.Add(27,6,"https://www.hse.gov.uk/msd/dse/work-routine.htm");
            sources.LinkClicked+=delegate(object s,LinkLabelLinkClickedEventArgs e) { try { Process.Start((string)e.Link.LinkData); } catch (Exception ex) { if (!(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)) throw; } };
            Place(sources,6,211,372,25,11,false);
            TextAt("快捷操作",6,260,372,24,13,true,Palette.Ink);
            TextAt("Ctrl+,  打开设置       Ctrl+M  切换精简浮窗\nCtrl+P  暂停 / 继续     Esc  收起到托盘\n锁屏、休眠时自动暂停；不会改变手动暂停状态。",6,293,372,51,10,false,Palette.Muted);
            TextAt("全屏自动免打扰",6,355,372,24,13,true,Palette.Ink);
            TextAt("计时不变，浮窗 / 声音 / 通知静默。\n退出全屏稳定 3 秒后恢复，未处理的提醒只补发一次。",6,384,372,31,10,false,Palette.Muted);
            placement=this;
            int footer=(int)Math.Round(ClientSize.Height/scale)-84;
            TextAt(error.Length>0?error:"仅本机保存 · 不检测坐姿 · 按自身能力活动",25,footer,430,29,10,false,error.Length>0?Palette.Amber:Palette.Muted);
            SoftButton cancel=new SoftButton(); cancel.Text="取消"; cancel.DeviceScale=scale; cancel.DialogResult=DialogResult.Cancel; Place(cancel,224,footer+35,92,34,12,false);
            SoftButton save=new SoftButton(); save.Text="保存设置"; save.DeviceScale=scale; save.Primary=true; save.DialogResult=DialogResult.OK; Place(save,326,footer+35,130,34,12,true);
            AcceptButton=save; CancelButton=cancel;
        }
        internal void DrawPreview(string path,bool showGuide=false)
        { if (showGuide) guideTab.PerformClick(); Refresh(); using (Bitmap bmp=new Bitmap(Width,Height)) { DrawToBitmap(bmp,new Rectangle(0,0,Width,Height)); bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png); } }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { foreach (Font font in ownedFonts) font.Dispose(); ownedFonts.Clear(); }
        }
    }
}
