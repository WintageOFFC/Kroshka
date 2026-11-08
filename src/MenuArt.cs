namespace Kroshka;

internal static class MenuArt
{
    public static Image? Load(string name)
    {
        using var stream = typeof(MenuArt).Assembly.GetManifestResourceStream("Kroshka." + name);
        if (stream == null) return null;
        using var image = Image.FromStream(stream);
        return new Bitmap(image, new Size(36, 36));
    }
    public static ContextMenuStrip Create() => new()
    {
        BackColor = Theme.Panel, ForeColor = Theme.Ink, Font = Theme.Font(10),
        ImageScalingSize = new Size(36, 36), Padding = new Padding(3),
        Renderer = new ToolStripProfessionalRenderer(new RetroMenuColors()) { RoundedEdges = false }
    };
}
