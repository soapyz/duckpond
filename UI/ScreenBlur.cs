using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace DuckPond.UI;

/// <summary>抓取桌面并按 fadetop 思路做快速模糊：强降采样 → 双三次放大 → 压暗。</summary>
public static class ScreenBlur
{
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

    public static System.Windows.Media.Imaging.BitmapSource Capture(double darkening)
    {
        var vs = WinForms.SystemInformation.VirtualScreen;
        using var shot = new Bitmap(Math.Max(1, vs.Width), Math.Max(1, vs.Height), PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot))
        {
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, shot.Size, CopyPixelOperation.SourceCopy);
        }

        // 1/14 降采样：一次性丢掉高频细节，放大后自然形成柔和雾化效果
        var sw = Math.Max(8, shot.Width / 14);
        var sh = Math.Max(4, shot.Height / 14);
        using var small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(shot, new Rectangle(0, 0, sw, sh));
        }

        // 再对小块做一次位移叠加以消除放大时的块状感
        using var soft = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(soft))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    g.DrawImage(small, new Rectangle(dx, dy, sw, sh));
        }

        using var full = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(soft, new Rectangle(0, 0, full.Width, full.Height));
            var alpha = (int)Math.Clamp(darkening * 255, 0, 220);
            using var veil = new SolidBrush(Color.FromArgb(alpha, 6, 16, 13));
            g.FillRectangle(veil, 0, 0, full.Width, full.Height);
        }

        var hbmp = full.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero,
                System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
        finally { DeleteObject(hbmp); }
    }
}
