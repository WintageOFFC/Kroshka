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

internal sealed class DriveMenuRenderer : ToolStripProfessionalRenderer
{
    public DriveMenuRenderer() : base(new RetroMenuColors()) { RoundedEdges=false; }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var area=new Rectangle(0,0,e.Item.Width-1,e.Item.Height-1);
        using var background=new SolidBrush(Theme.Panel);
        e.Graphics.FillRectangle(background,area);
        if(!e.Item.Selected)return;
        using var shape=Theme.Round(area,6);
        using var selection=new SolidBrush(Theme.Vintage?Color.Navy:Theme.Selection);
        e.Graphics.FillPath(selection,shape);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor=Theme.Vintage && e.Item.Selected?Color.White:Theme.Ink;
        base.OnRenderItemText(e);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var box=e.ImageRectangle;
        int x=box.Left+box.Width/2,y=box.Top+box.Height/2;
        using var pen=new Pen(Theme.Vintage && e.Item.Selected?Color.White:Theme.Mint,2);
        e.Graphics.DrawLines(pen,new Point[]{new(x-5,y),new(x-1,y+4),new(x+6,y-4)});
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if(Theme.Vintage)ControlPaint.DrawBorder3D(e.Graphics,e.ToolStrip.ClientRectangle,Border3DStyle.Raised);
        else base.OnRenderToolStripBorder(e);
    }
}
