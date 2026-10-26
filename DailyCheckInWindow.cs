using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace StandReminder
{
    internal sealed class DailyCheckInWindow : Form
    {
        private readonly DailyCheckInEngine engine;
        private readonly Preferences prefs;
        private readonly Func<string> persist;
        private readonly float scale;
        private readonly List<Font> fonts=new List<Font>();
        private readonly Panel[] pages=new Panel[3];
        private readonly SoftButton[] tabs=new SoftButton[3];
        private readonly SoftButton saveSettings=new SoftButton();
        private readonly CheckBox pelvic=new CheckBox(), water=new CheckBox();
        private readonly CheckBox pelvicReminder=new CheckBox(), waterReminder=new CheckBox();
        private readonly Label date=new Label(), totals=new Label(), historyTotals=new Label(), error=new Label();
        private readonly NumericUpDown goal=new NumericUpDown();
        private readonly DateTimePicker reminderTime=new DateTimePicker();
        private readonly DailyCalendar history;
        private readonly Label monthLabel=new Label();
        private readonly SoftButton previousMonth=new SoftButton(), nextMonth=new SoftButton(), importHistory=new SoftButton();
        private readonly Panel body=new Panel();
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private bool updating;
        private string displayedDate;
        private int activeTab;
        private int S(float value) { return (int)Math.Round(value*scale); }
        private void Place(Control parent,Control control,int x,int y,int width,int height,float size,bool bold)
        {
            control.SetBounds(S(x),S(y),S(width),S(height));
            Font font=Palette.Font(size,bold,scale); control.Font=font; fonts.Add(font);
            SoftButton button=control as SoftButton; if (button!=null) button.DeviceScale=scale;
            parent.Controls.Add(control);
        }
        private Label TextAt(Control parent,string text,int x,int y,int width,int height,float size,bool bold)
        {
            Label label=new Label(); label.Text=text; label.ForeColor=bold?Palette.Ink:Palette.Muted;
            Place(parent,label,x,y,width,height,size,bold); return label;
        }
        internal DailyCheckInWindow(DailyCheckInEngine data,Preferences preferences,float uiScale,Func<string> save)
        {
            engine=data; prefs=preferences; scale=uiScale; persist=save; history=new DailyCalendar(scale);
            Text="起身 · 每日打卡与历史"; BackColor=Palette.Paper; ForeColor=Palette.Ink;
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=MinimizeBox=false; ShowInTaskbar=false; StartPosition=FormStartPosition.CenterParent;
            int maxHeight=Screen.FromPoint(Cursor.Position).WorkingArea.Height-SystemInformation.CaptionHeight-SystemInformation.FixedFrameBorderSize.Height*2-S(24);
            ClientSize=new Size(S(540),Math.Min(S(640),maxHeight));
            TextAt(this,"每天一点，慢慢坚持。",24,18,492,36,23,true);
            TextAt(this,"按实际情况打卡，今天的勾选会在次日刷新。",25,60,490,25,12,false);
            string[] names={"今日打卡","历史记录","提醒与目标"};
            for (int i=0;i<tabs.Length;i++)
            {
                int index=i; tabs[i]=new SoftButton(); tabs[i].Text=names[i];
                Place(this,tabs[i],24+i*168,103,156,34,12,true);
                tabs[i].Click+=delegate { SelectTab(index); };
            }
            RoundedCard card=new RoundedCard(); card.DeviceScale=scale;
            card.SetBounds(S(24),S(151),S(492),ClientSize.Height-S(239)); Controls.Add(card);
            body.BackColor=Palette.Card; body.AutoScroll=true;
            body.SetBounds(S(12),S(12),card.Width-S(24),card.Height-S(24)); card.Controls.Add(body);
            for (int i=0;i<pages.Length;i++)
            {
                pages[i]=new Panel(); pages[i].BackColor=Palette.Card;
                // Leave room for the native scrollbar, whose DPI can differ in render previews.
                pages[i].Size=new Size(S(440),S(i==2?416:370)); body.Controls.Add(pages[i]);
            }
            BuildToday(); BuildHistory(); BuildSettings();
            error.ForeColor=Palette.Amber;
            Place(this,error,25,(int)(ClientSize.Height/scale)-80,490,26,10,false);
            saveSettings.Text="保存提醒与目标"; saveSettings.Primary=true;
            Place(this,saveSettings,24,(int)(ClientSize.Height/scale)-46,212,34,12,true);
            saveSettings.Click+=delegate { ApplySettings(); };
            importHistory.Text="导入旧版记录";
            Place(this,importHistory,24,(int)(ClientSize.Height/scale)-46,180,34,12,false);
            importHistory.Click+=delegate { ImportHistory(); };
            SoftButton close=new SoftButton(); close.Text="关闭"; close.DialogResult=DialogResult.Cancel;
            Place(this,close,414,(int)(ClientSize.Height/scale)-46,102,34,12,false); CancelButton=close;
            error.Text=persist();
            RefreshRecords(); SelectTab(0);
            timer.Interval=1000;
            timer.Tick+=delegate { if (engine.Today.Date!=DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) || displayedDate!=engine.Today.Date) RefreshRecords(); };
            timer.Start();
        }
        private void BuildToday()
        {
            Panel page=pages[0];
            Place(page,date,6,2,434,26,14,true);
            Place(page,totals,6,35,434,44,11,false); totals.ForeColor=Palette.Muted;
            pelvic.Text="完成今日提肛计划"; pelvic.AutoSize=false;
            Place(page,pelvic,8,94,430,36,15,true);
            TextAt(page,"按你已有的个人计划确认；不额外规定次数。\n如有疼痛或难以放松，暂停并咨询专业人员。",32,136,400,48,11,false);
            water.AutoSize=false;
            Place(page,water,8,212,430,36,15,true);
            TextAt(page,"达到自己设置的全天饮水目标后勾选。\n无需为补打卡而集中大量饮水。",32,254,400,48,11,false);
            TextAt(page,"勾选即保存，可再次点击撤销。\n这里记录的是你的确认，每项每天最多累计 1 次。",6,320,432,46,11,false);
            pelvic.CheckedChanged+=delegate { if (!updating) Complete(DailyHabit.Pelvic,pelvic.Checked); };
            water.CheckedChanged+=delegate { if (!updating) Complete(DailyHabit.Water,water.Checked); };
        }
        private void BuildHistory()
        {
            Panel page=pages[1];
            Place(page,historyTotals,6,2,434,42,12,true);
            previousMonth.Text="‹"; nextMonth.Text="›";
            previousMonth.AccessibleName="上个月"; nextMonth.AccessibleName="下个月";
            Place(page,previousMonth,6,47,34,30,18,true); Place(page,monthLabel,50,49,278,28,15,true);
            Place(page,nextMonth,342,47,34,30,18,true);
            SoftButton today=new SoftButton(); today.Text="今天"; Place(page,today,386,47,54,30,11,false);
            previousMonth.Click+=delegate { history.SetMonth(CalendarDates.MoveMonth(history.VisibleMonth,-1)); };
            nextMonth.Click+=delegate { history.SetMonth(CalendarDates.MoveMonth(history.VisibleMonth,1)); };
            today.Click+=delegate { history.SelectDate(DateTime.Today); };
            string[] weekdays={"一","二","三","四","五","六","日"};
            for (int i=0;i<7;i++)
            { Label label=TextAt(page,weekdays[i],6+i*62,83,62,20,11,false); label.TextAlign=ContentAlignment.MiddleCenter; }
            Place(page,history,6,106,434,240,11,false);
            TextAt(page,"绿色：已确认提 / 水；浅色：未确认；空白：无记录。",6,350,434,20,10,false);
            history.ViewChanged+=delegate { UpdateCalendarLabels(); };
        }
        private void UpdateCalendarLabels()
        {
            monthLabel.Text=history.VisibleMonth.ToString("yyyy 年 M 月",CultureInfo.InvariantCulture);
            previousMonth.Enabled=history.VisibleMonth>new DateTime(1,1,1); nextMonth.Enabled=history.VisibleMonth<new DateTime(9999,12,1);
            DailyCheckInRecord record=history.SelectedRecord;
            historyTotals.Text="累计提肛 "+engine.Count(DailyHabit.Pelvic)+" 次 · 喝水 "+engine.Count(DailyHabit.Water)+" 次 · 全完成 "+engine.Count(DailyHabit.Pelvic|DailyHabit.Water)+" 天\n"+
                history.SelectedDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+
                (record==null?(history.SelectedDate>DateTime.Today?" · 尚未到这一天":" · 无打卡记录"):
                " · 提肛 "+(record.PelvicDone?"✓":"—")+"  喝水 "+(record.WaterDone?"✓":"—")+"  目标 "+Litres(record.WaterTargetMl)+" L");
        }
        private void ImportHistory()
        {
            using (OpenFileDialog file=new OpenFileDialog())
            {
                file.Title="选择旧版打卡记录（保留当前已有日期）"; file.Filter="打卡记录 XML|*.xml;*.bak|所有文件|*.*"; file.CheckFileExists=true;
                if (file.ShowDialog(this)!=DialogResult.OK) return;
                try
                {
                    int count=engine.Import(DailyCheckInStore.Read(file.FileName));
                    prefs.DailyWaterTargetMl=engine.Today.WaterTargetMl;
                    string problem=persist(); error.Text=problem.Length==0?"已导入 "+count+" 天记录；已有历史日期保持不变。":problem;
                    RefreshRecords();
                }
                catch (Exception exception)
                { if (!StorageErrors.Expected(exception)) throw; error.Text="无法导入：文件格式无效或无法读取，当前记录保持不变。"; }
            }
        }
        private void BuildSettings()
        {
            Panel page=pages[2];
            TextAt(page,"每日饮水目标 / L",6,2,300,26,13,true);
            goal.DecimalPlaces=1; goal.Increment=0.1m; goal.Minimum=0.1m; goal.Maximum=10m;
            goal.Value=prefs.DailyWaterTargetMl/1000m; goal.BorderStyle=BorderStyle.FixedSingle;
            goal.BackColor=Palette.Paper; goal.ForeColor=Palette.Ink;
            Place(page,goal,6,35,138,31,16,true);
            SoftButton preset15=new SoftButton(), preset17=new SoftButton();
            preset15.Text="1.5 L"; preset17.Text="1.7 L";
            Place(page,preset15,160,35,130,32,12,false); Place(page,preset17,306,35,130,32,12,false);
            preset15.Click+=delegate { goal.Value=1.5m; }; preset17.Click+=delegate { goal.Value=1.7m; };
            TextAt(page,"参考：温和气候、低活动成年人，女性 1.5 L / 男性 1.7 L。\n这是饮水量参考，不含食物中的水；个人需要会有差异。\n有医嘱限液时按医嘱设置，软件不判断医学上的达标。",6,80,434,64,10,false);
            TextAt(page,"每天提醒一次",6,163,250,26,13,true);
            pelvicReminder.Text="提醒提肛打卡"; waterReminder.Text="提醒喝水打卡";
            pelvicReminder.Checked=prefs.PelvicCheckReminder; waterReminder.Checked=prefs.WaterCheckReminder;
            Place(page,pelvicReminder,6,197,210,28,12,false); Place(page,waterReminder,230,197,210,28,12,false);
            TextAt(page,"提醒时间",6,243,140,28,12,false);
            reminderTime.Format=DateTimePickerFormat.Custom; reminderTime.CustomFormat="HH:mm"; reminderTime.ShowUpDown=true;
            reminderTime.Value=DateTime.Today.AddMinutes(prefs.CheckInReminderMinute);
            Place(page,reminderTime,160,239,130,32,14,true);
            TextAt(page,"两项合并提醒，已完成的不再提醒。\n全屏、锁屏时延后；次日不追补昨天的提醒。",6,286,434,44,11,false);
            TextAt(page,"目标改动应用于今天及以后，并清除今天的喝水勾选。\n本机固定保存，移动软件不会丢失；退出期间不会提醒。",6,338,434,46,10,false);
            LinkLabel sources=new LinkLabel(); sources.Text="饮水参考：国家疾控局    提肛说明：NIDDK";
            sources.LinkColor=Palette.Green; sources.ActiveLinkColor=Palette.Ink; sources.LinkBehavior=LinkBehavior.HoverUnderline;
            int secondSource=sources.Text.IndexOf("提肛说明",StringComparison.Ordinal);
            sources.Links.Add(0,"饮水参考：国家疾控局".Length,"https://www.ndcpa.gov.cn/jbkzzx/c100040/common/content/content_2104761364364300288.html");
            sources.Links.Add(secondSource,sources.Text.Length-secondSource,"https://www.niddk.nih.gov/health-information/urologic-diseases/kegel-exercises");
            Place(page,sources,6,390,434,24,10,false);
            sources.LinkClicked+=delegate(object sender,LinkLabelLinkClickedEventArgs e)
            {
                try { Process.Start((string)e.Link.LinkData); }
                catch (System.ComponentModel.Win32Exception) { error.Text="无法打开浏览器，请参阅使用说明中的来源链接。"; }
            };
        }
        private void Complete(DailyHabit habit,bool completed)
        {
            bool rolled=engine.SetDate(DateTime.Today,prefs.DailyWaterTargetMl);
            // A click on yesterday's still-visible checkbox only refreshes the date.
            if (!rolled && displayedDate==engine.Today.Date) engine.SetCompleted(habit,completed);
            error.Text=persist(); RefreshRecords();
        }
        private void ApplySettings()
        {
            engine.SetDate(DateTime.Today,prefs.DailyWaterTargetMl);
            prefs.DailyWaterTargetMl=(int)(goal.Value*1000m);
            prefs.PelvicCheckReminder=pelvicReminder.Checked; prefs.WaterCheckReminder=waterReminder.Checked;
            prefs.CheckInReminderMinute=reminderTime.Value.Hour*60+reminderTime.Value.Minute;
            engine.SetWaterTarget(prefs.DailyWaterTargetMl);
            string problem=persist(); error.Text=problem.Length==0?"已保存。今天的勾选和历史记录已同步。":problem;
            RefreshRecords();
        }
        internal void RefreshRecords()
        {
            bool rolled=engine.SetDate(DateTime.Today,prefs.DailyWaterTargetMl);
            if (rolled) error.Text=persist();
            displayedDate=engine.Today.Date; updating=true;
            try
            {
                date.Text=displayedDate+"  ·  今日打卡";
                totals.Text="累计提肛 "+engine.Count(DailyHabit.Pelvic)+" 天   ·   喝水 "+engine.Count(DailyHabit.Water)+" 天\n今日已完成 "+((engine.Today.PelvicDone?1:0)+(engine.Today.WaterDone?1:0))+" / 2 项";
                pelvic.Checked=engine.Today.PelvicDone; water.Checked=engine.Today.WaterDone;
                water.Text="完成今日饮水 "+Litres(engine.Today.WaterTargetMl)+" L";
                historyTotals.Text="累计提肛 "+engine.Count(DailyHabit.Pelvic)+" 次  ·  喝水 "+engine.Count(DailyHabit.Water)+" 次  ·  全完成 "+engine.Count(DailyHabit.Pelvic|DailyHabit.Water)+" 天";
                if (activeTab==1) FillHistory();
            }
            finally { updating=false; }
        }
        private void FillHistory()
        {
            history.SetRecords(engine.History()); UpdateCalendarLabels();
        }
        internal static string Litres(int millilitres) { return (millilitres/1000m).ToString("0.0##",CultureInfo.InvariantCulture); }
        internal void SelectTab(int index)
        {
            activeTab=index;
            saveSettings.Visible=index==2;
            importHistory.Visible=index==1;
            for (int i=0;i<pages.Length;i++) { pages[i].Visible=i==index; tabs[i].Primary=i==index; tabs[i].Invalidate(); }
            body.AutoScrollPosition=Point.Empty;
            if (index==1) FillHistory();
        }
        internal void DrawPreview(string path,int tab)
        {
            SelectTab(tab); Refresh();
            using (Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size)); bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Stop(); timer.Dispose(); }
            base.Dispose(disposing);
            if (disposing) { foreach (Font font in fonts) font.Dispose(); fonts.Clear(); }
        }
    }
}
