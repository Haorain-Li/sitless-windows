using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace StandReminder
{
    internal static class CalendarDates
    {
        internal static DateTime MonthStart(DateTime date) { return new DateTime(date.Year,date.Month,1); }
        internal static DateTime? DayAt(DateTime month,int cell)
        {
            if (cell<0 || cell>=42) return null;
            DateTime first=MonthStart(month); int offset=((int)first.DayOfWeek+6)%7;
            int day=cell-offset+1;
            if (day<1 || day>DateTime.DaysInMonth(first.Year,first.Month)) return null;
            return first.AddDays(day-1);
        }
        internal static DateTime MoveMonth(DateTime month,int delta)
        {
            DateTime first=MonthStart(month);
            long index=(long)(first.Year-1)*12+first.Month-1+delta;
            index=Math.Max(0,Math.Min(9999L*12-1,index));
            return new DateTime((int)(index/12)+1,(int)(index%12)+1,1);
        }
    }
    internal sealed class DailyCalendar : Control
    {
        private readonly Dictionary<string,DailyCheckInRecord> records=new Dictionary<string,DailyCheckInRecord>();
        private readonly Font dayFont,chipFont;
        private readonly float scale;
        private int hover=-1;
        internal DateTime VisibleMonth { get; private set; }
        internal DateTime SelectedDate { get; private set; }
        internal int RecordCount { get { return records.Count; } }
        internal event EventHandler ViewChanged;
        internal DailyCalendar(float uiScale)
        {
            scale=uiScale; dayFont=Palette.Font(11,true,scale); chipFont=Palette.Font(9,false,scale);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            BackColor=Palette.Card; TabStop=true; Cursor=Cursors.Hand;
            AccessibleRole=AccessibleRole.Table; AccessibleName="每日打卡月历";
            SelectedDate=DateTime.Today; VisibleMonth=CalendarDates.MonthStart(SelectedDate);
        }
        internal void SetRecords(List<DailyCheckInRecord> days)
        { records.Clear(); foreach (DailyCheckInRecord record in days) records[record.Date]=record; Invalidate(); }
        internal DailyCheckInRecord SelectedRecord
        { get { DailyCheckInRecord record; records.TryGetValue(SelectedDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),out record); return record; } }
        internal void SetMonth(DateTime month)
        {
            VisibleMonth=CalendarDates.MonthStart(month);
            if (CalendarDates.MonthStart(SelectedDate)!=VisibleMonth) SelectedDate=VisibleMonth;
            Changed();
        }
        internal void SelectDate(DateTime date)
        { SelectedDate=date.Date; VisibleMonth=CalendarDates.MonthStart(date); Changed(); }
        private void Changed()
        {
            AccessibleDescription=SelectedDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+"，使用方向键选择日期，PageUp / PageDown 切换月份。";
            Invalidate(); if (ViewChanged!=null) ViewChanged(this,EventArgs.Empty);
        }
        private int CellAt(Point point)
        {
            if (!ClientRectangle.Contains(point) || Width<=0 || Height<=0) return -1;
            return Math.Min(5,point.Y*6/Height)*7+Math.Min(6,point.X*7/Width);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); int cell=CellAt(e.Location); if (hover!=cell) { hover=cell; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover=-1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button!=MouseButtons.Left) return;
            DateTime? date=CalendarDates.DayAt(VisibleMonth,CellAt(e.Location));
            if (date.HasValue) { Focus(); SelectDate(date.Value); }
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key=keyData&Keys.KeyCode;
            if (key==Keys.Left || key==Keys.Right || key==Keys.Up || key==Keys.Down || key==Keys.PageUp || key==Keys.PageDown || key==Keys.Home) return true;
            return base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); int delta=e.KeyCode==Keys.Left?-1:e.KeyCode==Keys.Right?1:e.KeyCode==Keys.Up?-7:e.KeyCode==Keys.Down?7:0;
            if (delta!=0)
            {
                long ticks=SelectedDate.Ticks+(long)delta*TimeSpan.TicksPerDay;
                if (ticks>=DateTime.MinValue.Ticks && ticks<=DateTime.MaxValue.Date.Ticks) SelectDate(new DateTime(ticks));
                e.Handled=true;
            }
            else if (e.KeyCode==Keys.PageUp || e.KeyCode==Keys.PageDown)
            { SetMonth(CalendarDates.MoveMonth(VisibleMonth,e.KeyCode==Keys.PageUp?-1:1)); e.Handled=true; }
            else if (e.KeyCode==Keys.Home) { SelectDate(DateTime.Today); e.Handled=true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            for (int cell=0;cell<42;cell++)
            {
                DateTime? date=CalendarDates.DayAt(VisibleMonth,cell); if (!date.HasValue) continue;
                int col=cell%7, row=cell/7;
                RectangleF box=new RectangleF(col*Width/7f+2*scale,row*Height/6f+2*scale,Width/7f-4*scale,Height/6f-4*scale);
                bool selected=date.Value==SelectedDate, today=date.Value==DateTime.Today;
                Color fill=selected?Palette.LightGreen:hover==cell?Palette.Paper:Palette.Card;
                using (Brush brush=new SolidBrush(fill)) Palette.FillRound(e.Graphics,brush,box,6*scale);
                using (Pen pen=new Pen(today || (selected && Focused)?Palette.Green:Palette.Line,today?1.5f*scale:scale))
                using (GraphicsPath path=Palette.Round(box,6*scale)) e.Graphics.DrawPath(pen,path);
                Rectangle text=new Rectangle((int)box.X+(int)(5*scale),(int)box.Y+(int)(2*scale),(int)box.Width-(int)(8*scale),(int)(18*scale));
                TextRenderer.DrawText(e.Graphics,date.Value.Day.ToString(CultureInfo.InvariantCulture),dayFont,text,date.Value>DateTime.Today?Palette.Muted:Palette.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
                DailyCheckInRecord record;
                if (records.TryGetValue(date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),out record))
                {
                    DrawChip(e.Graphics,box.X+4*scale,box.Y+22*scale,"提",record.PelvicDone);
                    DrawChip(e.Graphics,box.X+29*scale,box.Y+22*scale,"水",record.WaterDone);
                }
            }
        }
        private void DrawChip(Graphics graphics,float x,float y,string text,bool done)
        {
            RectangleF bounds=new RectangleF(x,y,21*scale,13*scale);
            using (Brush brush=new SolidBrush(done?Palette.Green:Palette.Paper)) Palette.FillRound(graphics,brush,bounds,3*scale);
            TextRenderer.DrawText(graphics,text,chipFont,Rectangle.Round(bounds),done?Color.White:Palette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing)
        { base.Dispose(disposing); if (disposing) { dayFont.Dispose(); chipFont.Dispose(); } }
    }
}
