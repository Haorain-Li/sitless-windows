using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StandReminder
{
    internal enum UiGlyph { None, More, Hide, Compact, Expand, Window, Calendar, Settings, Pause, Play, Reset, Exit }

    internal static class GlyphPainter
    {
        internal static void Draw(Graphics graphics,UiGlyph glyph,RectangleF bounds,Color color)
        {
            GraphicsState state=graphics.Save();
            graphics.TranslateTransform(bounds.X,bounds.Y); graphics.ScaleTransform(bounds.Width/16f,bounds.Height/16f);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using (Pen pen=new Pen(color,1.4f))
            using (Brush brush=new SolidBrush(color))
            {
                pen.StartCap=pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;
                switch (glyph)
                {
                    case UiGlyph.More:
                        for (int i=0;i<3;i++) graphics.FillEllipse(brush,1+i*5,6.5f,3,3); break;
                    case UiGlyph.Hide: graphics.DrawLine(pen,3,8,13,8); break;
                    case UiGlyph.Compact:
                        graphics.DrawLine(pen,2,2,6,6); graphics.DrawLines(pen,new PointF[]{new PointF(2,6),new PointF(6,6),new PointF(6,2)});
                        graphics.DrawLine(pen,14,14,10,10); graphics.DrawLines(pen,new PointF[]{new PointF(10,14),new PointF(10,10),new PointF(14,10)}); break;
                    case UiGlyph.Expand:
                        graphics.DrawLine(pen,6,6,2,2); graphics.DrawLines(pen,new PointF[]{new PointF(2,6),new PointF(2,2),new PointF(6,2)});
                        graphics.DrawLine(pen,10,10,14,14); graphics.DrawLines(pen,new PointF[]{new PointF(10,14),new PointF(14,14),new PointF(14,10)}); break;
                    case UiGlyph.Window:
                        graphics.DrawRectangle(pen,2,3,12,10); graphics.DrawLine(pen,2,6,14,6); break;
                    case UiGlyph.Calendar:
                        graphics.DrawRectangle(pen,2.5f,3.5f,11,10); graphics.DrawLine(pen,5,2,5,5); graphics.DrawLine(pen,11,2,11,5);
                        graphics.DrawLine(pen,3,7,13,7); graphics.DrawLine(pen,5,10,7,10); graphics.DrawLine(pen,10,10,11,10); break;
                    case UiGlyph.Settings:
                        graphics.DrawEllipse(pen,4,4,8,8); graphics.DrawEllipse(pen,6.5f,6.5f,3,3);
                        for (int i=0;i<8;i++)
                        {
                            double angle=i*Math.PI/4;
                            graphics.DrawLine(pen,8+(float)Math.Cos(angle)*5,8+(float)Math.Sin(angle)*5,8+(float)Math.Cos(angle)*6.5f,8+(float)Math.Sin(angle)*6.5f);
                        }
                        break;
                    case UiGlyph.Pause: graphics.DrawLine(pen,5,3,5,13); graphics.DrawLine(pen,11,3,11,13); break;
                    case UiGlyph.Play: graphics.DrawPolygon(pen,new PointF[]{new PointF(5,3),new PointF(13,8),new PointF(5,13)}); break;
                    case UiGlyph.Reset:
                        graphics.DrawArc(pen,2,2,12,12,35,290);
                        graphics.DrawLines(pen,new PointF[]{new PointF(1.5f,2.5f),new PointF(1.5f,6),new PointF(5,6)}); break;
                    case UiGlyph.Exit:
                        graphics.DrawLines(pen,new PointF[]{new PointF(7,2),new PointF(3,2),new PointF(3,14),new PointF(7,14)});
                        graphics.DrawLine(pen,7,8,14,8); graphics.DrawLines(pen,new PointF[]{new PointF(11,5),new PointF(14,8),new PointF(11,11)}); break;
                }
            }
            graphics.Restore(state);
        }
    }

    internal sealed class MenuEntryStyle
    {
        internal UiGlyph Glyph;
        internal bool Toggle, Danger, Heading;
    }

    internal sealed class ThemeMenuItem : ToolStripMenuItem
    {
        private readonly Size preferredSize;
        internal ThemeMenuItem(string text,EventHandler click,Size size) : base(text,null,click)
        { preferredSize=size; }
        public override Size GetPreferredSize(Size constrainingSize) { return preferredSize; }
        internal void FitWidth(int width) { SetBounds(new Rectangle(0,Bounds.Y,width,Height)); }
    }

    internal sealed class ThemeMenu : ContextMenuStrip
    {
        private float scale=1;
        private Font menuFont, headingFont;
        private Size regionSize;
        private int S(float value) { return (int)Math.Round(value*scale); }
        internal void Configure(float uiScale)
        {
            scale=uiScale; BackColor=Palette.Card; ForeColor=Palette.Ink;
            ShowImageMargin=ShowCheckMargin=false; DropShadowEnabled=true;
            Padding=new Padding(S(8)); Renderer=new ThemeMenuRenderer(scale);
            menuFont=Palette.Font(12,false,scale); headingFont=Palette.Font(10,true,scale); Font=menuFont;
        }
        internal void Heading(string text)
        {
            ToolStripLabel label=new ToolStripLabel(text); label.AutoSize=false;
            label.Size=new Size(S(264),S(28)); label.Font=headingFont;
            label.Tag=new MenuEntryStyle { Heading=true }; Items.Add(label);
        }
        internal ToolStripMenuItem Entry(string text,UiGlyph glyph,EventHandler click,string shortcut,bool toggle,bool value,bool danger)
        {
            ToolStripMenuItem item=new ThemeMenuItem(text,click,new Size(S(264),S(36))); item.Margin=Padding.Empty;
            item.ShortcutKeyDisplayString=shortcut; item.Checked=value;
            item.Tag=new MenuEntryStyle { Glyph=glyph,Toggle=toggle,Danger=danger }; Items.Add(item); return item;
        }
        internal void Divider()
        {
            ToolStripSeparator separator=new ToolStripSeparator(); separator.AutoSize=false;
            separator.Size=new Size(S(264),S(13)); separator.Margin=Padding.Empty; Items.Add(separator);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            // Native dropdowns reserve extra margin even with their image gutter hidden.
            // Stretch rows to the actual popup so hover and shortcut columns align.
            foreach (ToolStripItem item in Items)
            { ThemeMenuItem command=item as ThemeMenuItem; if (command!=null) command.FitWidth(Width); }
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width<1 || Height<1 || regionSize==Size) return;
            regionSize=Size;
            using (GraphicsPath path=Palette.Round(new RectangleF(0,0,Width,Height),S(12)))
            { Region old=Region; Region=new Region(path); if (old!=null) old.Dispose(); }
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { if (menuFont!=null) menuFont.Dispose(); if (headingFont!=null) headingFont.Dispose(); }
        }
    }

    internal sealed class ThemeMenuRenderer : ToolStripRenderer
    {
        private readonly float scale;
        private static readonly Color Danger=Color.FromArgb(160,65,52), DangerHover=Color.FromArgb(249,237,232);
        internal ThemeMenuRenderer(float uiScale) { scale=uiScale; }
        private int S(float value) { return (int)Math.Round(value*scale); }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        { e.Graphics.Clear(Palette.Card); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using (Pen pen=new Pen(Palette.Line,scale))
            using (GraphicsPath path=Palette.Round(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),S(12))) e.Graphics.DrawPath(pen,path);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            MenuEntryStyle style=e.Item.Tag as MenuEntryStyle;
            bool danger=style!=null && style.Danger;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if (e.Item.Selected && e.Item.Enabled)
                using (Brush brush=new SolidBrush(danger?DangerHover:Palette.LightGreen))
                    Palette.FillRound(e.Graphics,brush,new RectangleF(S(6),1,e.Item.Width-S(12),e.Item.Height-2),S(8));
            Color ink=!e.Item.Enabled?Palette.Muted:danger?Danger:Palette.Green;
            if (style==null) return;
            float y=(e.Item.Height-S(16))/2f;
            if (style.Toggle)
            {
                ToolStripMenuItem item=(ToolStripMenuItem)e.Item;
                RectangleF box=new RectangleF(S(13),y+scale,S(14),S(14));
                using (GraphicsPath path=Palette.Round(box,S(4)))
                using (Pen pen=new Pen(item.Checked?Palette.Green:Palette.Line,1.3f*scale))
                {
                    if (item.Checked) using (Brush brush=new SolidBrush(Palette.Green)) e.Graphics.FillPath(brush,path);
                    e.Graphics.DrawPath(pen,path);
                }
                if (item.Checked)
                    using (Pen pen=new Pen(Color.White,1.5f*scale))
                    {
                        pen.StartCap=pen.EndCap=LineCap.Round;
                        e.Graphics.DrawLines(pen,new PointF[]{new PointF(S(16),y+S(8)),new PointF(S(19),y+S(11)),new PointF(S(24),y+S(5))});
                    }
            }
            else GlyphPainter.Draw(e.Graphics,style.Glyph,new RectangleF(S(12),y,S(16),S(16)),ink);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            MenuEntryStyle style=e.Item.Tag as MenuEntryStyle;
            bool heading=style!=null && style.Heading, shortcut=e.Text!=e.Item.Text;
            Color color=heading || shortcut || !e.Item.Enabled?Palette.Muted:style!=null && style.Danger?Danger:Palette.Ink;
            Rectangle bounds=new Rectangle(S(heading?12:42),0,e.Item.Width-S(heading?24:56),e.Item.Height);
            ToolStripMenuItem item=e.Item as ToolStripMenuItem;
            if (!shortcut && item!=null && !String.IsNullOrEmpty(item.ShortcutKeyDisplayString)) bounds.Width-=S(67);
            TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis;
            flags|=shortcut?TextFormatFlags.Right:TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,bounds,color,flags);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        { using (Pen pen=new Pen(Palette.Line,scale)) e.Graphics.DrawLine(pen,S(12),e.Item.Height/2,e.Item.Width-S(12),e.Item.Height/2); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
    }
}
