namespace Kroshka;

internal static class MenuArt
{
    public static ContextMenuStrip Create() => new()
    {
        BackColor = Theme.Panel, ForeColor = Theme.Ink, Font = Theme.Font(10),
        ImageScalingSize = new Size(36, 36), Padding = new Padding(3),
        Renderer = new ToolStripProfessionalRenderer(new RetroMenuColors()) { RoundedEdges = false }
    };
}
