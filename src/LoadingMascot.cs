using System.Drawing.Drawing2D;

namespace Kroshka;

internal static class LoadingMascot
{
    public static void Draw(Graphics g, Rectangle bounds, float angle)
    {
        var saved = g.Save();
        g.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f); g.RotateTransform(angle);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(ThemeArt.Picture("Loading"), -bounds.Width / 2f, -bounds.Height / 2f, bounds.Width, bounds.Height);
        g.Restore(saved);
    }
}
