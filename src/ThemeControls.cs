using System.Drawing.Drawing2D;

namespace Kroshka;

internal sealed class GraphToggle : SoftButton
{
    public void FitShape()
    {
        using var shape=Theme.Round(ClientRectangle,10);
        var previous=Region;Region=new Region(shape);previous?.Dispose();
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if(Width>0 && Height>0) FitShape(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int x=Width/2,y=Height/2;
        using var ink=new Pen(Theme.Ink,1.5f);
        e.Graphics.DrawLine(ink,x-4,y,x+4,y);
        if(Text=="+") e.Graphics.DrawLine(ink,x,y-4,x,y+4);
    }
}

internal sealed class ModePicker : DriveButton
{
    static readonly string[] Labels=["По файлам","По папкам","Системные размеры","Журнал наблюдения","Дерево папок"];
    readonly ContextMenuStrip menu=MenuArt.Create();
    int selected=4;
    protected override bool ShowDisk => false;
    public event EventHandler? SelectedIndexChanged;
    public int SelectedIndex {
        get => selected;
        set {
            if(value==selected)return;
            selected=value;
            Text=Labels[value];
            SelectedIndexChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public ModePicker()
    {
        Text=Labels[selected];
        menu.ShowCheckMargin=true;
        menu.ShowImageMargin=false;
        for(int i=0;i<Labels.Length;i++) {
            int index=i;
            menu.Items.Add(new ToolStripMenuItem(Labels[i],null,(_,_)=>SelectedIndex=index));
        }
    }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        menu.ForeColor=Theme.Ink;
        menu.BackColor=Theme.Panel;
        menu.Font=Font;
        for (int i=0;i<menu.Items.Count;i++)
            ((ToolStripMenuItem)menu.Items[i]).Checked=i==selected;
        menu.Show(this,0,Height);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)menu.Dispose();
        base.Dispose(disposing);
    }
}
internal sealed class ThemeTile : SoftButton
{
    readonly string id,label;
    readonly Bitmap portrait;
    public ThemeTile(string id,string label)
    {
        this.id=id;this.label=label;AccessibleName=label;Size=new Size(190,80);
        portrait=ThemeArt.Picture("Themes",id);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        if(id==Theme.Style){using var path=Theme.Round(new RectangleF(1,1,Width-3,Height-3),9);using var line=new Pen(Theme.Mint,2);g.DrawPath(line,path);}
        g.DrawImage(portrait,new Rectangle(6,4,72,72));
        TextRenderer.DrawText(g,label,Font,new Rectangle(82,0,Width-84,Height),Theme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
        if(id==Theme.Style){using var pen=new Pen(Theme.Mint,2);g.DrawLines(pen,new Point[]{new(Width-21,12),new(Width-17,16),new(Width-10,8)});}
    }

}
