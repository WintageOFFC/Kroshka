using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Kroshka;

internal sealed class BufferedGrid : DataGridView
{
    public BufferedGrid()
    {
        DoubleBuffered = true;
        var bar = VerticalScrollBar;
        var face = new ScrollFace(bar);
        // Keep the native scrollbar as the scroll engine; its empty window region
        // prevents theme animations from drawing over our separate visible surface.
        bar.Region=new Region(Rectangle.Empty);
        Controls.Add(face);
        void PlaceFace() {
            face.Bounds=bar.Bounds;
            face.Visible=bar.Visible;
            if(face.Visible && Controls.GetChildIndex(face)!=0)face.BringToFront();
        }
        bar.LocationChanged+=(_,_)=>PlaceFace();
        bar.SizeChanged+=(_,_)=>PlaceFace();
        bar.VisibleChanged+=(_,_)=>PlaceFace();
        PlaceFace();
        Scroll += (_, _) => face.Invalidate();
        RowsAdded += (_, _) => face.Invalidate();
        RowsRemoved += (_, _) => face.Invalidate();
        bar.ValueChanged += (_, _) => face.Invalidate();
        SizeChanged += (_, _) => face.Invalidate();
    }
}

internal sealed class ScrollFace : Control
{
    [StructLayout(LayoutKind.Sequential)]
    struct ScrollInfo {
        public int size,left,top,right,bottom,arrow,thumbStart,thumbEnd,reserved;
        public uint state,firstArrow,pageBefore,thumb,pageAfter,lastArrow;
    }
    [DllImport("user32.dll")]
    static extern bool GetScrollBarInfo(IntPtr hwnd,int objectId,ref ScrollInfo info);

    readonly ScrollBar bar;
    public ScrollFace(ScrollBar bar)
    {
        this.bar=bar;

        TabStop=false;
        AccessibleRole=AccessibleRole.None;
        SetStyle(ControlStyles.Selectable,false);
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
    }
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button==MouseButtons.Left && bar.IsHandleCreated)
            SendMessage(bar.Handle,0x0201,new IntPtr(1),new IntPtr((e.Y<<16)|(e.X&0xffff)));
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if(Parent is DataGridView grid) {
            Point pt=PointToScreen(e.Location);
            SendMessage(grid.Handle,0x020A,new IntPtr(e.Delta<<16),new IntPtr((pt.Y<<16)|(pt.X&0xffff)));
            if(e is HandledMouseEventArgs handled)handled.Handled=true;
        }
        base.OnMouseWheel(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;
        g.Clear(Theme.Vintage?Theme.Bg:Theme.Surface);
        if(!bar.IsHandleCreated)return;
        var info=new ScrollInfo{size=Marshal.SizeOf<ScrollInfo>()};
        if(!GetScrollBarInfo(bar.Handle,-4,ref info))return;
        int w=Width,h=Height,arrow=info.arrow;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(info.thumbEnd>info.thumbStart && w>6) {
            var thumb=new Rectangle(3,info.thumbStart,w-6,info.thumbEnd-info.thumbStart);
            using var path=Theme.Round(thumb,4);
            using var fill=new SolidBrush(Theme.Vintage?Theme.Panel:Theme.Muted);
            g.FillPath(fill,path);
            if(Theme.Vintage) ControlPaint.DrawBorder3D(g,thumb,Border3DStyle.Raised);
        }
        if(arrow<=0)return;
        using var ink=new Pen(Theme.Ink,1.3f);
        for(int end=0;end<=1;end++) {
            int y=end==1?h-arrow/2:arrow/2,sign=end==1?1:-1,mid=w/2;
            if(Theme.Vintage)ControlPaint.DrawBorder3D(g,new Rectangle(0,end==1?h-arrow:0,w,arrow),Border3DStyle.Raised);
            g.DrawLines(ink,new Point[]{new(mid-3,y-sign),new(mid,y+2*sign),new(mid+3,y-sign)});
        }
    }
}
