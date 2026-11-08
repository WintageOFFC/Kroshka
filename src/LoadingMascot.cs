using System.Drawing.Drawing2D;

namespace Kroshka;

internal static class LoadingMascot
{
    public static Image? Load()
    {
        using var stream = typeof(LoadingMascot).Assembly.GetManifestResourceStream("Kroshka.Loading");
        if (stream == null) return null;
        using var source = Image.FromStream(stream);
        return new Bitmap(source, new Size(64, 64));
    }
    public static void Draw(Graphics g, Image? image, Rectangle bounds, float angle)
    {
        var saved = g.Save();
        g.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f); g.RotateTransform(angle);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (image != null) g.DrawImage(image, -bounds.Width / 2f, -bounds.Height / 2f, bounds.Width, bounds.Height);
        else { using var pen = new Pen(Theme.Mint, 2); g.DrawArc(pen, -10, -10, 20, 20, 0, 270); }
        g.Restore(saved);
    }
}
