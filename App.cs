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
                using (FloatWindow window = new FloatWindow(demo, render))
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
        public int Left = int.MinValue;
        public int Top = int.MinValue;
        public string StatsDate = "";
        public double TodaySeconds = 0;
        public int Completed = 0;
    }

    internal static class Palette
    {
        internal static readonly Color Paper = Color.FromArgb(247, 249, 245);
        internal static readonly Color Ink = Color.FromArgb(35, 53, 44);
        internal static readonly Color Muted = Color.FromArgb(112, 128, 117);
        internal static readonly Color Green = Color.FromArgb(49, 111, 78);
        internal static readonly Color LightGreen = Color.FromArgb(228, 237, 225);
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
        private bool hovered;
        internal SoftButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Palette.Paper; ForeColor = Palette.Ink; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = Primary ? (Amber ? Palette.Amber : Palette.Green) : (hovered ? Palette.Line : Palette.LightGreen);
            if (!Enabled) bg = Palette.Line;
            using (Brush b = new SolidBrush(bg)) Palette.FillRound(e.Graphics,b,new RectangleF(0,0,Width-1,Height-1),8*DeviceScale);
            Color fg = Primary ? Color.White : Palette.Ink;
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled ? fg : Palette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Width-8,Height-8),fg,bg);
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
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolTip tips = new ToolTip();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly Dictionary<Control,Font> ownedFonts = new Dictionary<Control,Font>();
        private readonly Label title = new Label(), tagline = new Label(), status = new Label(), clock = new Label(), detail = new Label(), stats = new Label();
        private readonly SoftButton options = new SoftButton(), hide = new SoftButton(), primary = new SoftButton(), secondary = new SoftButton();
        private float scale;
        private bool compact, locked, sleeping, completedNotice, exiting, ready, settingsOpen;
        private string saveError = "";
        private int ticks;
        private double progress;
        private Size regionSize;
        private bool Blocked { get { return locked || sleeping; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { CreateParams cp=base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }

        internal FloatWindow(bool isDemo, bool isRendering)
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
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None;
            Text=Program.WindowTitle+(demo ? " · 演示" : ""); ShowInTaskbar=true; BackColor=Palette.Paper;
            Icon=Palette.MakeIcon(); TopMost=prefs.AlwaysOnTop; StartPosition=FormStartPosition.Manual;
            DoubleBuffered=true; KeyPreview=true;
            Controls.AddRange(new Control[]{title,tagline,status,clock,detail,stats,options,hide,primary,secondary});
            title.Text="起身"; title.ForeColor=Palette.Ink; tagline.Text="SIT LESS"; tagline.ForeColor=Palette.Muted;
            foreach (Label label in new Label[]{title,tagline,status,clock,detail,stats}) { label.AutoSize=false; label.BackColor=Color.Transparent; label.MouseDown+=DragWindow; }
            status.ForeColor=Palette.Muted; clock.ForeColor=Palette.Ink; detail.ForeColor=Palette.Muted; stats.ForeColor=Palette.Muted;
            options.Text="···"; options.AccessibleName="打开菜单"; hide.Text="−"; hide.AccessibleName="收起到托盘";
            primary.Primary=true;
            options.Click+=delegate { menu.Show(options,new Point(0,options.Height)); };
            hide.Click+=delegate { Hide(); if (!rendering) tray.ShowBalloonTip(3000,"起身仍在计时","双击托盘里的绿色图标，可找回浮窗。",ToolTipIcon.Info); };
            primary.Click+=delegate { PrimaryAction(); }; secondary.Click+=delegate { SecondaryAction(); };
            tips.SetToolTip(options,"设置、精简模式和退出"); tips.SetToolTip(hide,"收起到托盘，继续提醒");
            tips.SetToolTip(clock,"桌前计时估计，不检测坐姿；拖动这里移动浮窗");
            MouseDown+=DragWindow; Resize+=delegate { UpdateRegion(); };
            menu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { BuildMenu(); e.Cancel=false; };
            tray.Icon=Icon; tray.Text="起身 · 正在计时"; tray.ContextMenuStrip=menu; tray.Visible=!rendering;
            tray.DoubleClick+=delegate { ShowFloat(true); }; tray.BalloonTipClicked+=delegate { ShowFloat(true); };
            timer.Interval=1000; timer.Tick+=OnTick;
            SystemEvents.SessionSwitch+=SessionChanged; SystemEvents.PowerModeChanged+=PowerChanged;
            KeyDown+=delegate(object s,KeyEventArgs e)
            {
                if (e.KeyCode==Keys.Escape) { Hide(); e.Handled=true; }
                if (e.KeyCode==Keys.Space && !(ActiveControl is Button)) { SecondaryAction(); e.Handled=true; }
            };
            UpdateUi(true);
            Rectangle area=Screen.PrimaryScreen.WorkingArea;
            Location=prefs.Left==int.MinValue ? new Point(area.Right-Width-S(24),area.Bottom-Height-S(40)) : new Point(prefs.Left,prefs.Top);
            ClampToScreen(); ready=true;
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
                saveError="";
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
            ClientSize=new Size(S(compact ? 248 : 316),S(compact ? 76 : 252));
            title.Visible=tagline.Visible=detail.Visible=stats.Visible=hide.Visible=!compact;
            if (!compact)
            {
                Place(title,44,15,48,25,15,true); Place(tagline,90,19,80,18,9,true);
                Place(options,244,12,27,27,19,true); Place(hide,277,12,25,27,17,false);
                Place(status,22,58,270,22,12,false); Place(clock,18,79,280,60,49,true);
                SetOwnedFont(clock,new Font("Segoe UI",S(50),FontStyle.Bold,GraphicsUnit.Pixel));
                Place(detail,23,144,270,20,11,false); Place(stats,23,184,270,18,10,false);
                Place(secondary,22,210,92,29,11,false); Place(primary,123,210,171,29,11,true);
            }
            else
            {
                Place(clock,41,12,112,32,26,true);
                SetOwnedFont(clock,new Font("Segoe UI",S(27),FontStyle.Bold,GraphicsUnit.Pixel));
                Place(status,43,47,115,18,10,false); Place(options,216,8,23,23,16,true);
                Place(secondary,164,37,32,27,12,true); Place(primary,202,37,32,27,14,true);
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
                using (Brush b=new SolidBrush(Palette.Green)) g.FillEllipse(b,22,20,14,14);
                using (Pen p=new Pen(Color.White,1.4f)) { g.DrawLine(p,26,27,29,30); g.DrawLine(p,29,30,33,24); }
                using (Brush b=new SolidBrush(Palette.Line)) Palette.FillRound(g,b,new RectangleF(22,174,272,3),1.5f);
                if (progress>0) using (Brush b=new SolidBrush(accent)) Palette.FillRound(g,b,new RectangleF(22,174,(float)Math.Max(3,272*progress),3),1.5f);
            }
            else
            {
                using (Pen p=new Pen(Palette.Line,3)) g.DrawEllipse(p,15,23,18,18);
                using (Pen p=new Pen(accent,3)) { p.StartCap=p.EndCap=LineCap.Round; g.DrawArc(p,15,23,18,18,-90,(float)Math.Max(6,360*progress)); }
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
            string state=Blocked ? "系统暂停 · 锁屏 / 休眠" : completedNotice ? "活动计时完成，欢迎回来" : paused ? "已暂停 · 随时继续" : rest ? "活动中 · 走动或舒展一下" : due ? "该活动一下了" : "距离下次活动";
            status.Text=compact ? (Blocked?"系统暂停":paused?"计时已暂停":rest?"活动计时中":"距离下次活动") : state;
            clock.Text=completedNotice?"完成 ✓":FormatTime(rest?engine.BreakRemaining:engine.WorkRemaining);
            status.ForeColor=due?Palette.Amber:Palette.Muted;
            clock.ForeColor=due?Palette.Amber:Palette.Ink;
            if (due) detail.Text="已连续计时 "+(int)engine.WorkElapsed.TotalMinutes+" 分钟，起身走一走";
            else if (rest) detail.Text="按自身能力轻松活动；完成后等待你继续";
            else if (completedNotice) detail.Text="准备好后，点「继续计时」开始下一轮";
            else detail.Text=demo?"演示模式：10 秒提醒 / 5 秒活动": "每 "+prefs.WorkMinutes+" 分钟提醒 · 活动 "+prefs.BreakMinutes+" 分钟";
            stats.Text="今日完成活动计时  "+engine.CompletedBreaksToday+" 次"+(demo?"  · 演示":"");
            primary.Amber=due;
            primary.Text=compact?(rest?"↗":"起"):(completedNotice?"继续计时":rest?"结束本次活动":due?"开始活动  →":"起身活动  →");
            secondary.Text=compact?(paused?"▶":"Ⅱ"):(completedNotice?"收起":due?"5 分钟后":paused?"继续":"暂停");
            primary.AccessibleName=completedNotice?"继续计时":rest?"结束本次活动（不计完成）":"开始活动";
            secondary.AccessibleName=completedNotice?"收起到托盘":due?"延后五分钟":paused?"继续计时":"暂停计时";
            tips.SetToolTip(primary,rest?"提前结束不计次数，保留已有桌前计时":completedNotice?"开始新一轮桌前计时":"开始活动倒计时，请根据自身能力走动或舒展");
            tips.SetToolTip(secondary,completedNotice?"收起到托盘，回来后可继续":due?"五分钟后再次提醒，不清空已有计时":paused?"继续计时":"暂停，暂时离开时可使用");
            primary.Enabled=secondary.Enabled=!Blocked;
            progress=rest?engine.BreakElapsed.TotalSeconds/engine.ActiveBreakDuration.TotalSeconds:Math.Min(1,engine.WorkElapsed.TotalSeconds/engine.WorkDuration.TotalSeconds);
            string trayText="起身 · "+(paused?"已暂停":due?"该活动了":rest?"活动 "+clock.Text:clock.Text+" 后活动");
            if (tray.Text!=trayText) tray.Text=trayText;
            Invalidate(new Rectangle(0,S(compact?18:172),Width,S(compact?30:7)));
            primary.Invalidate(); secondary.Invalidate();
        }
        private void OnTick(object sender,EventArgs e)
        {
            TimeSpan elapsed=monotonic.Elapsed; monotonic.Restart();
            engine.SetLocalDate(DateTime.Today);
            TimerSignal signal=Blocked?TimerSignal.None:engine.Tick(elapsed);
            if (signal==TimerSignal.ReminderDue) NotifyDue();
            if (signal==TimerSignal.BreakCompleted)
            {
                completedNotice=true; engine.Pause();
                if (prefs.Sound) SystemSounds.Asterisk.Play();
                if (!rendering) tray.ShowBalloonTip(4000,"活动计时完成","回来后点击「继续计时」，开始下一轮。",ToolTipIcon.Info);
                ShowFloat(false); SavePreferences();
            }
            UpdateUi(false); if (++ticks%60==0) SavePreferences();
        }
        private void NotifyDue()
        {
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
            if (completedNotice) { Hide(); return; }
            if (engine.Phase==TimerPhase.Reminder) engine.Snooze(TimeSpan.FromMinutes(5));
            else { engine.TogglePause(); if (!engine.IsPaused) completedNotice=false; }
            monotonic.Restart(); UpdateUi(false); SavePreferences();
        }
        private void BuildMenu()
        {
            menu.Items.Clear();
            menu.Items.Add("显示浮窗",null,delegate { ShowFloat(true); });
            menu.Items.Add("设置与规则…",null,delegate { OpenSettings(); });
            ToolStripMenuItem small=new ToolStripMenuItem("精简浮窗"); small.Checked=prefs.Compact;
            small.Click+=delegate { prefs.Compact=!prefs.Compact; UpdateUi(true); SavePreferences(); }; menu.Items.Add(small);
            ToolStripMenuItem pin=new ToolStripMenuItem("浮窗置顶"); pin.Checked=prefs.AlwaysOnTop;
            pin.Click+=delegate { prefs.AlwaysOnTop=!prefs.AlwaysOnTop; TopMost=prefs.AlwaysOnTop; SavePreferences(); }; menu.Items.Add(pin);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(engine.IsPaused?"继续计时":"暂停计时",null,delegate { if (!Blocked) { engine.TogglePause(); if (!engine.IsPaused) completedNotice=false; monotonic.Restart(); UpdateUi(false); } });
            menu.Items.Add("重新开始本轮",null,delegate { if (!Blocked) { completedNotice=false; engine.ResetSession(); monotonic.Restart(); UpdateUi(true); SavePreferences(); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出起身",null,delegate { exiting=true; Close(); });
        }
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
                        engine.Configure(demo?TimeSpan.FromSeconds(10):TimeSpan.FromMinutes(prefs.WorkMinutes),demo?TimeSpan.FromSeconds(5):TimeSpan.FromMinutes(prefs.BreakMinutes));
                        TopMost=prefs.AlwaysOnTop; UpdateUi(true); SavePreferences();
                        if (previous!=TimerPhase.Reminder && engine.Phase==TimerPhase.Reminder) NotifyDue();
                    }
                }
            }
            finally { settingsOpen=false; }
        }
        private void ShowFloat(bool activate)
        { if (IsDisposed) return; ClampToScreen(); if (!Visible) Show(); if (activate) { WindowState=FormWindowState.Normal; Activate(); } }
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
            if (!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); return; }
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
            prefs.Compact=true; UpdateUi(true); RenderTo(folder,"compact.png");
            prefs.Compact=false; for (int i=0;i<1800;i++) engine.Tick(TimeSpan.FromSeconds(1));
            UpdateUi(true); RenderTo(folder,"reminder.png");
            engine.StartBreak(); UpdateUi(true); RenderTo(folder,"break.png");
            using (SettingsWindow settings=new SettingsWindow(prefs,scale,""))
            { settings.Show(this); settings.DrawPreview(Path.Combine(folder,"settings.png")); settings.Hide(); }
            Hide();
        }
        private void RenderTo(string folder,string name)
        { Refresh(); using (Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,ClientRectangle); bitmap.Save(Path.Combine(folder,name),System.Drawing.Imaging.ImageFormat.Png); } }
    }

    internal sealed class SettingsWindow : Form
    {
        private readonly NumericUpDown work=new NumericUpDown(), rest=new NumericUpDown();
        private readonly CheckBox pin=new CheckBox(), sound=new CheckBox(), small=new CheckBox();
        private readonly float scale;
        internal int WorkMinutes { get { return (int)work.Value; } }
        internal int BreakMinutes { get { return (int)rest.Value; } }
        internal bool PinOn { get { return pin.Checked; } }
        internal bool SoundOn { get { return sound.Checked; } }
        internal bool CompactOn { get { return small.Checked; } }
        private int S(float v) { return (int)Math.Round(v*scale); }
        private void Place(Control c,int x,int y,int w,int h,float size,bool bold)
        { c.SetBounds(S(x),S(y),S(w),S(h)); c.Font=Palette.Font(size,bold,scale); Controls.Add(c); }
        private void TextAt(string text,int x,int y,int w,int h,float size,bool bold,Color color)
        { Label label=new Label(); label.Text=text; label.ForeColor=color; Place(label,x,y,w,h,size,bold); }
        internal SettingsWindow(Preferences p,float uiScale,string error)
        {
            scale=uiScale; Text="起身 · 设置与规则"; BackColor=Palette.Paper; ForeColor=Palette.Ink;
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=MinimizeBox=false;
            ShowInTaskbar=false; StartPosition=FormStartPosition.CenterParent; AutoScroll=true;
            int maxHeight=Screen.FromPoint(Cursor.Position).WorkingArea.Height-S(66);
            ClientSize=new Size(S(414),Math.Min(S(624),maxHeight)); AutoScrollMinSize=new Size(0,S(624));
            TextAt("让身体，换个姿势。",24,21,370,34,22,true,Palette.Ink);
            TextAt("短暂活动，也值得留一点时间。",25,60,365,23,12,false,Palette.Muted);
            TextAt("提醒节奏",25,105,360,22,12,true,Palette.Ink);
            SoftButton regular=new SoftButton(); regular.Text="日常  30 / 3"; regular.DeviceScale=scale; Place(regular,24,137,172,34,12,false);
            SoftButton focused=new SoftButton(); focused.Text="办公  50 / 5"; focused.DeviceScale=scale; Place(focused,207,137,182,34,12,false);
            regular.Click+=delegate { work.Value=30; rest.Value=3; }; focused.Click+=delegate { work.Value=50; rest.Value=5; };
            TextAt("每隔",26,189,45,26,12,false,Palette.Ink); Place(work,72,186,68,28,13,false);
            work.Minimum=5; work.Maximum=120; work.Value=p.WorkMinutes;
            TextAt("分钟，活动",151,189,86,26,12,false,Palette.Ink); Place(rest,245,186,60,28,13,false);
            rest.Minimum=1; rest.Maximum=20; rest.Value=p.BreakMinutes; TextAt("分钟",317,189,65,26,12,false,Palette.Ink);
            work.AccessibleName="提醒间隔，分钟"; rest.AccessibleName="活动时长，分钟";
            TextAt("调整间隔保留已有计时；活动时长从下次生效。",25,222,365,24,10,false,Palette.Muted);
            pin.Text="浮窗始终置顶"; pin.Checked=p.AlwaysOnTop; Place(pin,26,263,185,26,12,false);
            sound.Text="播放提示音"; sound.Checked=p.Sound; Place(sound,220,263,165,26,12,false);
            small.Text="精简浮窗（到时自动展开）"; small.Checked=p.Compact; Place(small,26,298,360,26,12,false);
            TextAt("规则依据",25,350,360,23,12,true,Palette.Ink);
            TextAt("WHO 将久坐定义为清醒时坐 / 斜倚 / 躺卧，且能量消耗 ≤ 1.5 METs；并未规定统一时限。",25,383,363,47,11,false,Palette.Muted);
            TextAt("默认 30 分钟打断参考 Mayo Clinic；3 分钟是可调的产品预设。办公预设参考 HSE 的 50–60 分钟工作、5–10 分钟休息建议。",25,435,363,61,11,false,Palette.Muted);
            LinkLabel sources=new LinkLabel(); sources.Text="WHO 指南     Mayo Clinic     HSE 建议"; sources.LinkColor=Palette.Green;
            sources.Links.Clear(); sources.Links.Add(0,6,"https://www.ncbi.nlm.nih.gov/books/NBK566046/"); sources.Links.Add(11,11,"https://www.mayoclinic.org/healthy-lifestyle/adult-health/expert-answers/sitting/faq-20058005"); sources.Links.Add(27,6,"https://www.hse.gov.uk/msd/dse/work-routine.htm");
            sources.LinkClicked+=delegate(object s,LinkLabelLinkClickedEventArgs e) { try { Process.Start((string)e.Link.LinkData); } catch (Exception ex) { if (!(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)) throw; } };
            Place(sources,25,501,365,25,11,false);
            TextAt(error.Length>0?error:"仅记录计时，不检测坐姿；锁屏 / 休眠暂停计时。\n设置保存在程序旁；完成活动后，手动继续下一轮。",25,536,365,40,10,false,Palette.Muted);
            SoftButton cancel=new SoftButton(); cancel.Text="取消"; cancel.DeviceScale=scale; cancel.DialogResult=DialogResult.Cancel; Place(cancel,175,582,92,29,11,false);
            SoftButton save=new SoftButton(); save.Text="保存设置"; save.DeviceScale=scale; save.Primary=true; save.DialogResult=DialogResult.OK; Place(save,278,582,111,29,11,true);
            AcceptButton=save; CancelButton=cancel;
        }
        internal void DrawPreview(string path)
        { Refresh(); using (Bitmap bmp=new Bitmap(Width,Height)) { DrawToBitmap(bmp,new Rectangle(0,0,Width,Height)); bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png); } }
    }
}
