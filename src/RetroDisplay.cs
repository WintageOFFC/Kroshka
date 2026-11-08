using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Kroshka;
internal static class RetroDisplay
{
    static readonly int[] Masks = [0x3f,0x06,0x5b,0x4f,0x66,0x6d,0x7d,0x07,0x7f,0x6f];
    public static void Number(Graphics g, string text, float x, float y, float height, Color color)
    {
        float unit = height / 40, width = 23 * unit, gap = 5 * unit, stroke = 3.2f * unit;
        int split = text.LastIndexOf(' '); string digits = split > 0 ? text[..split] : text, suffix = split > 0 ? text[(split + 1)..] : "";
        foreach (char ch in digits)
        {
            if (ch is '.' or ',') { using var dot = new SolidBrush(color); g.FillRectangle(dot, x, y + height - stroke, stroke, stroke); x += 7 * unit; continue; }
            if (ch is '+' or '-' or '−')
            { using var p = new Pen(color, stroke); g.DrawLine(p,x,y+height/2,x+13*unit,y+height/2); if(ch=='+')g.DrawLine(p,x+6.5f*unit,y+height/2-7*unit,x+6.5f*unit,y+height/2+7*unit);x+=18*unit;continue; }
            if (ch < '0' || ch > '9')
            { using var f=Theme.Font(18,true); using var b=new SolidBrush(color);g.DrawString(text,f,b,x,y);return; }
            var segments = new (float x1,float y1,float x2,float y2)[]
            { (4,1,19,1),(21,4,21,17),(21,23,21,36),(4,39,19,39),(2,23,2,36),(2,4,2,17),(4,20,19,20) };
            for(int s=0;s<7;s++)
            {
                var (a,b,c,d)=segments[s]; bool on=(Masks[ch-'0']&(1<<s))!=0;
                if(on){using var glow=new Pen(Color.FromArgb(22,color),stroke+6*unit);g.DrawLine(glow,x+a*unit,y+b*unit,x+c*unit,y+d*unit);}
                using var ink=new Pen(on?color:Color.FromArgb(24,52,41),stroke){StartCap=LineCap.Square,EndCap=LineCap.Square};
                g.DrawLine(ink,x+a*unit,y+b*unit,x+c*unit,y+d*unit);
            }
            x+=width+gap;
        }
        using var suffixFont=Theme.Font(11,true);using var suffixBrush=new SolidBrush(Theme.Muted);g.DrawString(suffix,suffixFont,suffixBrush,x+6,y+height-19);
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    public static void Window(Form form)
    {
        form.HandleCreated+=(_,_)=>ApplyWindow(form);
    }
    public static void ApplyWindow(Form form)
        {
            try { int dark=Theme.Style=="Vintage"?0:1,corners=Theme.Style=="Modern"?2:1,bg=ColorTranslator.ToWin32(Theme.Bg),ink=ColorTranslator.ToWin32(Theme.Mint),line=ColorTranslator.ToWin32(Theme.Line);
                int backdrop=Theme.Style=="Modern"?3:1; DwmSetWindowAttribute(form.Handle,38,ref backdrop,4);
                DwmSetWindowAttribute(form.Handle,33,ref corners,4);
                DwmSetWindowAttribute(form.Handle,20,ref dark,4);DwmSetWindowAttribute(form.Handle,35,ref bg,4);DwmSetWindowAttribute(form.Handle,36,ref ink,4);DwmSetWindowAttribute(form.Handle,34,ref line,4);
            } catch { }
        }
}
internal sealed class LedTitle : Control
{
    static readonly string[] Glyphs = [
        "10001"+"10010"+"10100"+"11000"+"10100"+"10010"+"10001",
        "11110"+"10001"+"10001"+"11110"+"10000"+"10000"+"10000",
        "01110"+"10001"+"10001"+"10001"+"10001"+"10001"+"01110",
        "10101"+"10101"+"10101"+"10101"+"10101"+"10101"+"11111",
        "10001"+"10010"+"10100"+"11000"+"10100"+"10010"+"10001",
        "01110"+"10001"+"10001"+"11111"+"10001"+"10001"+"10001"];
    public LedTitle(){DoubleBuffered=true;Size=new Size(295,42);AccessibleName="Крошка";}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Bg);using var on=new SolidBrush(Theme.Mint);using var off=new SolidBrush(Color.FromArgb(14,39,29));using var glow=new SolidBrush(Color.FromArgb(20,Theme.Mint));
        float step=5f;for(int c=0;c<Glyphs.Length;c++)for(int i=0;i<35;i++)
        {float x=c*32+i%5*step,y=3+i/5*step;bool lit=Glyphs[c][i]=='1';if(lit)e.Graphics.FillRectangle(glow,x-1,y-1,6,6);e.Graphics.FillRectangle(lit?on:off,x,y,3.5f,3.5f);}
    }
}
internal sealed class RetroDatePicker : Control
{
    DateTime value=DateTime.Today;
    public DateTime Value {get=>value;set{this.value=value.Date;Invalidate();ValueChanged?.Invoke(this,EventArgs.Empty);}}
    public event EventHandler? ValueChanged;
    public RetroDatePicker(){DoubleBuffered=true;Height=30;Width=190;Cursor=Cursors.Hand;TabStop=true;AccessibleName="Дата истории";AccessibleRole=AccessibleRole.ComboBox;}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Panel);using var p=new Pen(Focused?Theme.Mint:Theme.Line);e.Graphics.DrawRectangle(p,0,0,Width-1,Height-1);
        using var f=Theme.Font(10);TextRenderer.DrawText(e.Graphics,value.ToString("dd.MM.yyyy")+"  ▾",f,ClientRectangle,Theme.Mint,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
    protected override void OnClick(EventArgs e){base.OnClick(e);Pick();}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Left)Value=Value.AddDays(-1);else if(e.KeyCode==Keys.Right)Value=Value.AddDays(1);else if(e.KeyCode is Keys.Enter or Keys.Space or Keys.Down)Pick();}
    void Pick()
    {
        using var f=new Form{Text="Дата истории",ClientSize=new Size(294,312),BackColor=Theme.Bg,ForeColor=Theme.Ink,Font=Theme.Font(10),FormBorderStyle=FormBorderStyle.FixedToolWindow,StartPosition=FormStartPosition.CenterParent};RetroDisplay.Window(f);
        DateTime month=new(Value.Year,Value.Month,1);var title=new Label{Location=new Point(48,18),Size=new Size(198,25),TextAlign=ContentAlignment.MiddleCenter,ForeColor=Theme.Mint};
        var prev=new SoftButton{Text="<",Location=new Point(12,12),Size=new Size(32,32)};var next=new SoftButton{Text=">",Location=new Point(250,12),Size=new Size(32,32)};
        var buttons=new List<SoftButton>();string[] days=["ПН","ВТ","СР","ЧТ","ПТ","СБ","ВС"];
        for(int i=0;i<7;i++)f.Controls.Add(new Label{Text=days[i],Location=new Point(13+i*39,54),Size=new Size(36,22),ForeColor=Theme.Muted,TextAlign=ContentAlignment.MiddleCenter});
        for(int i=0;i<42;i++){var b=new SoftButton{Location=new Point(12+i%7*39,80+i/7*37),Size=new Size(36,33)};buttons.Add(b);f.Controls.Add(b);b.Click+=(_,_)=>{Value=(DateTime)b.Tag!;f.Close();};}
        void Fill(){title.Text=month.ToString("MMMM yyyy",System.Globalization.CultureInfo.GetCultureInfo("ru-RU")).ToUpperInvariant();var first=month.AddDays(-(((int)month.DayOfWeek+6)%7));for(int i=0;i<42;i++){DateTime d=first.AddDays(i);buttons[i].Tag=d;buttons[i].Text=d.Day.ToString();buttons[i].ForeColor=d.Month==month.Month?Theme.Ink:Theme.Muted;buttons[i].Accent=d==Value?Theme.Line:Theme.Panel;}}
        prev.Click+=(_,_)=>{month=month.AddMonths(-1);Fill();};next.Click+=(_,_)=>{month=month.AddMonths(1);Fill();};f.Controls.AddRange([title,prev,next]);Fill();f.ShowDialog(FindForm());
    }
}

internal sealed class RetroMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Panel;
    public override Color ImageMarginGradientBegin => Theme.Panel;
    public override Color ImageMarginGradientMiddle => Theme.Panel;
    public override Color ImageMarginGradientEnd => Theme.Panel;
    public override Color MenuItemSelected => Theme.Line;
    public override Color MenuItemBorder => Theme.Mint;
    public override Color MenuBorder => Theme.Line;
    public override Color SeparatorDark => Theme.Line;
    public override Color SeparatorLight => Theme.Panel;
}

