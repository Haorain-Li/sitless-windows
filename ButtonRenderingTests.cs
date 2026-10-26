using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using StandReminder;

internal static class ButtonRenderingTests
{
    private static int assertions;
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    public static int Main()
    {
        try
        {
            foreach (Color surface in new Color[] { Palette.Paper, Palette.Card })
            foreach (float scale in new float[] { 1f, 1.5f, 2f })
            for (int state = 0; state < 6; state++)
            {
                using (Panel parent = new Panel())
                using (SoftButton button = new SoftButton())
                {
                    parent.BackColor = surface;
                    parent.Controls.Add(button);
                    button.Size = new Size((int)(120*scale), (int)(36*scale));
                    button.DeviceScale = scale;
                    button.Primary = state >= 3;
                    button.Amber = state == 5;
                    button.Enabled = state != 2;
                    if (surface==Palette.Paper && scale==1f && state==0)
                        Console.WriteLine("Button Opaque style: " + typeof(Control).GetMethod("GetStyle", Hidden).Invoke(button, new object[] { ControlStyles.Opaque }));
                    typeof(SoftButton).GetField("hovered", Hidden).SetValue(button, state == 1);
                    typeof(SoftButton).GetField("pressed", Hidden).SetValue(button, state == 4);
                    using (Bitmap buffer = new Bitmap(button.Width, button.Height))
                    using (Graphics graphics = Graphics.FromImage(buffer))
                    {
                        // Native WM_PAINT may skip OnPaintBackground for an opaque ButtonBase.
                        // DrawToBitmap's WM_PRINT path can conceal the resulting black corners.
                        graphics.Clear(Color.Black);
                        typeof(SoftButton).GetMethod("OnPaint", Hidden).Invoke(button,
                            new object[] { new PaintEventArgs(graphics, button.ClientRectangle) });
                        foreach (Point corner in new Point[] {
                            new Point(0, 0), new Point(button.Width-1, 0),
                            new Point(0, button.Height-1), new Point(button.Width-1, button.Height-1) })
                        {
                            assertions++;
                            if (buffer.GetPixel(corner.X, corner.Y).ToArgb() != surface.ToArgb())
                                throw new Exception("Unpainted button corner at " + corner + ", scale " + scale + ", state " + state);
                        }
                    }
                }
            }
            Console.WriteLine("PASS: " + assertions + " button rendering assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.GetBaseException().Message);
            return 1;
        }
    }
}
