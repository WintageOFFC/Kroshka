using System.Drawing.Drawing2D;

namespace Kroshka;

internal static class Glass
{
    // A static frosted surface with two soft Gaussian highlights and fine grain.
    // Only this small texture is generated; there is no animation or blur loop.
    static readonly Lazy<Bitmap> tile = new(() => {
        var bmp = new Bitmap(256,96);
        var random = new Random(146);
        for (int y=0;y<bmp.Height;y++) for (int x=0;x<bmp.Width;x++) {
            double a = Math.Exp(-Math.Pow((x-42)/79.0,2)-Math.Pow((y+8)/74.0,2));
            double b = Math.Exp(-Math.Pow((x-238)/108.0,2)-Math.Pow((y-89)/87.0,2));
            int grain = random.Next(-2,3);
            bmp.SetPixel(x,y,Color.FromArgb(35+(int)(18*a+18*b)+grain,48+(int)(31*a+8*b)+grain,73+(int)(28*a+28*b)+grain));
        }
        return bmp;
    });
    public static void Paint(Graphics g,RectangleF r)
    {
        r.Width--; r.Height--;
        using var path = Theme.Round(r,13);
        var state = g.Save(); g.SetClip(path,CombineMode.Intersect);
        g.DrawImage(tile.Value,r); g.Restore(state);
        using var edge = new Pen(Color.FromArgb(100,180,220,255)); g.DrawPath(edge,path);
    }
}

internal sealed class HeaderIcon(bool settings) : SoftButton
{
    public bool UpdateAvailable { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float x=Width/2f,y=Height/2f;
        using var pen=new Pen(Theme.Ink,2);
        if (settings) {
            using var outline=new GraphicsPath(); var teeth=new List<PointF>();
            for(int i=0;i<32;i++) {
                double a=Math.PI*2*i/32; int radius=i%4 is 0 or 3?8:11;
                teeth.Add(new(x+(float)Math.Cos(a)*radius,y+(float)Math.Sin(a)*radius));
            }
            outline.AddPolygon(teeth.ToArray()); g.DrawPath(pen,outline); g.DrawEllipse(pen,x-3,y-3,6,6);
            using var bow = new SolidBrush(Color.FromArgb(93,213,145));
            g.FillPolygon(bow,new PointF[]{new(x+1,y-14),new(x+9,y-11),new(x+1,y-7)});
            g.FillPolygon(bow,new PointF[]{new(x+16,y-15),new(x+8,y-11),new(x+16,y-7)});
        } else {
            g.DrawLine(pen,x,y-10,x,y+3); g.DrawLines(pen,new PointF[]{new(x-5,y-2),new(x,y+3),new(x+5,y-2)});
            g.DrawLines(pen,new PointF[]{new(x-8,y+4),new(x-8,y+8),new(x+8,y+8),new(x+8,y+4)});
            if(UpdateAvailable) { using var dot=new SolidBrush(Theme.Mint);g.FillEllipse(dot,x+9,y-13,7,7); }
        }
    }
}

internal static class Appearance
{
    static Color Map(Color value, Color[] old)
    {
        int index=Array.IndexOf(old,value); return index<0?value:Theme.Colors()[index];
    }
    static void Style(DataGridViewCellStyle style,Color[] old)
    {
        style.ForeColor=Map(style.ForeColor,old); style.BackColor=Map(style.BackColor,old);
        style.SelectionForeColor=Map(style.SelectionForeColor,old); style.SelectionBackColor=Map(style.SelectionBackColor,old);
        if(style.Font is Font font) style.Font=Theme.Font(font.Size,font.Bold);
    }
    public static void Row(DataGridViewRow row,Color[] old)
    {
        foreach(DataGridViewCell cell in row.Cells) if(cell.HasStyle) Style(cell.Style,old);
    }
    public static void Apply(Control c,Color[] old)
    {
        c.ForeColor=Map(c.ForeColor,old); c.BackColor=Map(c.BackColor,old);
        c.Font=Theme.Font(c.Font.Size,c.Font.Bold);
        if(c is SoftButton b) b.Accent=Map(b.Accent,old);
        if(c is Form form) RetroDisplay.ApplyWindow(form);
        if(c is DataGridView grid) {
            grid.BackgroundColor=Theme.Panel;grid.GridColor=Theme.Line;
            Style(grid.DefaultCellStyle,old);Style(grid.ColumnHeadersDefaultCellStyle,old);
            foreach(DataGridViewRow row in grid.Rows) Row(row,old);
        }
        if(c.ContextMenuStrip is {} menu){menu.BackColor=Theme.Panel;menu.ForeColor=Theme.Ink;}
        foreach(Control child in c.Controls) Apply(child,old);
        c.Invalidate();
    }
}
internal sealed class DriveButton : SoftButton
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;int y=Height/2;
        using var p=new Pen(Theme.Mint,1.6f);
        g.DrawPolygon(p,new Point[]{new(12,y-6),new(26,y-6),new(30,y+1),new(9,y+1)});
        g.DrawRectangle(p,9,y+1,21,6);g.DrawLine(p,23,y+4,26,y+4);
        TextRenderer.DrawText(g,Text,Font,new Rectangle(38,0,Width-60,Height),ForeColor,
            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        g.DrawLines(p,new Point[]{new(Width-19,y-2),new(Width-15,y+2),new(Width-11,y-2)});
    }
}
