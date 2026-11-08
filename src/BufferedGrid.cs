using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Kroshka;

internal sealed class BufferedGrid : DataGridView
{
    readonly ScrollPainter scroll;
    public BufferedGrid()
    {
        DoubleBuffered=true;
        scroll=new ScrollPainter(VerticalScrollBar);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)scroll?.Dispose();
        base.Dispose(disposing);
    }
}

// Keep Windows' hit testing, accessibility, wheel, keyboard and thumb dragging.
// Paint on the existing scrollbar using the thumb position calculated by Windows.
internal sealed class ScrollPainter : NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct ScrollInfo {
        public int size,left,top,right,bottom,arrow,thumbStart,thumbEnd,reserved;
        public uint state,firstArrow,pageBefore,thumb,pageAfter,lastArrow;
    }
    [DllImport("user32.dll")] static extern bool GetScrollBarInfo(IntPtr hwnd,int objectId,ref ScrollInfo info);
    readonly ScrollBar bar;
    public ScrollPainter(ScrollBar bar)
    {
        this.bar=bar;bar.HandleCreated+=Attach;bar.HandleDestroyed+=Detach;
        if(bar.IsHandleCreated)Attach(null,EventArgs.Empty);
    }
    void Attach(object? sender,EventArgs e)=>AssignHandle(bar.Handle);
    void Detach(object? sender,EventArgs e){if(Handle!=IntPtr.Zero)ReleaseHandle();}
    public void Dispose(){bar.HandleCreated-=Attach;bar.HandleDestroyed-=Detach;Detach(null,EventArgs.Empty);}
    protected override void WndProc(ref Message msg)
    {
        base.WndProc(ref msg);
        if(Handle==IntPtr.Zero || !bar.Visible || msg.Msg is not (0x0f or 0x0318 or 0x200 or 0x201 or 0x202 or 0xe0 or 0xe9))return;
        var info=new ScrollInfo{size=Marshal.SizeOf<ScrollInfo>()};
        if(!GetScrollBarInfo(Handle,-4,ref info))return; // OBJID_CLIENT
        using var g=msg.Msg==0x0318 && msg.WParam!=IntPtr.Zero ? Graphics.FromHdc(msg.WParam) : Graphics.FromHwnd(Handle);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Theme.Vintage?Theme.Bg:Theme.Surface);
        int w=bar.Width,h=bar.Height,arrow=info.arrow;
        if(info.thumbEnd>info.thumbStart && w>6) {
            var thumb=new Rectangle(3,info.thumbStart,w-6,Math.Max(1,info.thumbEnd-info.thumbStart));
            using var path=Theme.Round(thumb,4);using var fill=new SolidBrush(Theme.Vintage?Theme.Panel:Theme.Muted);
            g.FillPath(fill,path);if(Theme.Vintage) ControlPaint.DrawBorder3D(g,thumb,Border3DStyle.Raised);
        }
        if(arrow>0) {
            using var ink=new Pen(Theme.Ink,1.3f);int mid=w/2;
            foreach(bool bottom in new[]{false,true}) {
                int y=bottom?h-arrow/2:arrow/2, sign=bottom?1:-1;
                if(Theme.Vintage)ControlPaint.DrawBorder3D(g,new Rectangle(0,bottom?h-arrow:0,w,arrow),Border3DStyle.Raised);
                g.DrawLines(ink,new Point[]{new(mid-3,y-sign),new(mid,y+2*sign),new(mid+3,y-sign)});
            }
        }
    }
}
