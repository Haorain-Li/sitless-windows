using System;
using System.Drawing;

namespace StandReminder
{
    internal static class WindowSizing
    {
        internal static Size DefaultSize(bool compact) { return compact?new Size(280,88):new Size(344,390); }
        internal static Size MinimumLogical(bool compact) { return compact?new Size(260,84):new Size(320,360); }
        internal static Size MaximumLogical(bool compact) { return compact?new Size(700,220):new Size(860,975); }
        internal static Size ToPixels(Size logical,float dpi)
        { return new Size((int)Math.Round(logical.Width*dpi),(int)Math.Round(logical.Height*dpi)); }
        internal static Size ToLogical(Size pixels,float dpi)
        { return new Size((int)Math.Round(pixels.Width/dpi),(int)Math.Round(pixels.Height/dpi)); }
        internal static Size Maximum(bool compact,float dpi,Size workArea)
        {
            Size maximum=ToPixels(MaximumLogical(compact),dpi);
            return new Size(Math.Max(1,Math.Min(maximum.Width,workArea.Width)),Math.Max(1,Math.Min(maximum.Height,workArea.Height)));
        }
        internal static Size Minimum(bool compact,float dpi,Size workArea)
        {
            Size maximum=Maximum(compact,dpi,workArea), minimum=ToPixels(MinimumLogical(compact),dpi);
            return new Size(Math.Min(minimum.Width,maximum.Width),Math.Min(minimum.Height,maximum.Height));
        }
        internal static Size Restore(Size logical,bool compact,float dpi,Size workArea)
        {
            Size minimum=Minimum(compact,dpi,workArea), maximum=Maximum(compact,dpi,workArea);
            Size low=MinimumLogical(compact), high=MaximumLogical(compact), fallback=DefaultSize(compact);
            if (logical.Width<low.Width || logical.Width>high.Width) logical.Width=fallback.Width;
            if (logical.Height<low.Height || logical.Height>high.Height) logical.Height=fallback.Height;
            Size pixels=ToPixels(logical,dpi);
            return new Size(Math.Max(minimum.Width,Math.Min(maximum.Width,pixels.Width)),Math.Max(minimum.Height,Math.Min(maximum.Height,pixels.Height)));
        }
        internal static Point ScreenPoint(IntPtr packed)
        {
            long value=packed.ToInt64();
            return new Point(unchecked((short)(value&0xffff)),unchecked((short)((value>>16)&0xffff)));
        }
        internal static int HitTest(Point point,Size size,int edge,int corner)
        {
            if (point.X<0 || point.Y<0 || point.X>=size.Width || point.Y>=size.Height) return 0;
            bool left=point.X<edge, right=point.X>=size.Width-edge;
            bool top=point.Y<edge, bottom=point.Y>=size.Height-edge;
            if ((left && point.Y<corner) || (top && point.X<corner)) return 13;
            if ((right && point.Y<corner) || (top && point.X>=size.Width-corner)) return 14;
            if ((left && point.Y>=size.Height-corner) || (bottom && point.X<corner)) return 16;
            if ((right && point.Y>=size.Height-corner) || (bottom && point.X>=size.Width-corner)) return 17;
            if (left) return 10; if (right) return 11; if (top) return 12; if (bottom) return 15;
            return 1;
        }
        internal static Rectangle DragBounds(Rectangle initial,Point origin,Point current,int hit,Size minimum,Size maximum)
        {
            int dx=current.X-origin.X, dy=current.Y-origin.Y;
            if (hit==2) return new Rectangle(initial.X+dx,initial.Y+dy,initial.Width,initial.Height);
            bool left=hit==10 || hit==13 || hit==16, right=hit==11 || hit==14 || hit==17;
            bool top=hit==12 || hit==13 || hit==14, bottom=hit==15 || hit==16 || hit==17;
            int width=Math.Max(minimum.Width,Math.Min(maximum.Width,initial.Width+(left?-dx:right?dx:0)));
            int height=Math.Max(minimum.Height,Math.Min(maximum.Height,initial.Height+(top?-dy:bottom?dy:0)));
            return new Rectangle(left?initial.Right-width:initial.X,top?initial.Bottom-height:initial.Y,width,height);
        }
    }
}
