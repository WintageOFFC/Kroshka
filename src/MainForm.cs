using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kroshka;

internal static class Theme
{
    public static Color Bg = Color.FromArgb(6, 14, 12), Panel = Color.FromArgb(10, 25, 21), Line = Color.FromArgb(36, 72, 59),
        Ink = Color.FromArgb(203, 242, 222), Muted = Color.FromArgb(111, 159, 137), Mint = Color.FromArgb(123, 255, 195),
        Lilac = Color.FromArgb(171, 233, 191), Pink = Color.FromArgb(244, 184, 117);
    public static string Style { get; private set; } = "Classic";
    static readonly Color[] Classic = Colors();
    public static Color[] Colors() => [Bg, Panel, Line, Ink, Muted, Mint, Lilac, Pink];
    public static void Select(string style)
    {
        Style = style is "Vintage" or "Modern" ? style : "Classic";
        string[] hex = Style switch {
            "Vintage" => ["#c0c0c0","#d4d0c8","#858585","#111111","#343434","#122b81","#203e91","#aa4100"],
            "Modern" => ["#172139","#293853","#475e7a","#f2f5ff","#bccbe0","#a3ecff","#d5c7ff","#ffad78"],
            _ => []
        };
        var c = hex.Length == 0 ? Classic : hex.Select(ColorTranslator.FromHtml).ToArray();
        (Bg, Panel, Line, Ink, Muted, Mint, Lilac, Pink) = (c[0],c[1],c[2],c[3],c[4],c[5],c[6],c[7]);
    }
    public static bool Vintage => Style == "Vintage";
    public static Color Surface => Vintage ? Color.FromArgb(230,231,234) : Panel;
    public static Color Selection => Vintage ? Color.FromArgb(197,220,242) : Line;
    public static Color Phosphor => Vintage ? Color.FromArgb(134,181,255) : Mint;
    public static Font Font(float n, bool bold = false) => new(Style switch { "Vintage" => bold ? "Tahoma" : "Franklin Gothic Demi", "Modern" => "Segoe UI", _ => "Consolas" }, n, bold ? FontStyle.Bold : FontStyle.Regular);
    public static string Bytes(long n, bool sign = false)
    {
        string prefix = sign && n > 0 ? "+" : ""; double v = Math.Abs((double)n);
        string[] units = ["Б", "КиБ", "МиБ", "ГиБ", "ТиБ"]; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return prefix + (n < 0 ? "−" : "") + v.ToString(i > 0 ? "0.##" : "0", CultureInfo.GetCultureInfo("ru-RU")) + " " + units[i];
    }
    public static GraphicsPath Round(RectangleF r, float radius = 18)
    {
        var path = new GraphicsPath();
        if (Style != "Modern") { path.AddRectangle(r); return path; }
        float d = Math.Min(radius * 2, Math.Min(r.Width,r.Height));
        path.AddArc(r.Left,r.Top,d,d,180,90); path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); path.AddArc(r.Left,r.Bottom-d,d,d,90,90);
        path.CloseFigure(); return path;
    }
}
internal class SoftButton : Button
{
    public Color Accent = Theme.Panel;
    public SoftButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; ForeColor = Theme.Ink; Font = Theme.Font(10, true); Cursor = Cursors.Hand; Height = 38; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (this is GraphToggle && Parent is Overview chart && Theme.Style=="Modern") {
            var state=e.Graphics.Save();e.Graphics.TranslateTransform(-Left,-Top);
            Glass.Paint(e.Graphics,new RectangleF(0,124,chart.Width,chart.Collapsed?40:188));
            e.Graphics.Restore(state);
        } else e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 10);
        using var b = new SolidBrush(Enabled ? Accent : Theme.Line); e.Graphics.FillPath(b, p);
        using var border = new Pen(Enabled ? (Focused ? Theme.Mint : Theme.Line) : Color.FromArgb(22,40,31));e.Graphics.DrawPath(border,p);
        if (Theme.Style == "Vintage") ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Raised);
        if (this is not (DriveButton or ThemeTile or GraphToggle)) TextRenderer.DrawText(e.Graphics, Text.ToUpperInvariant(), Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
}
internal sealed class Overview : Control
{
    public Sample? Current;
    public List<object?[]> Points = new();
    public string Status = "", Period = "сегодня";
    public long MaximumGap = 180;
    public bool ShowDates;
    public string FreeCaption = "СВОБОДНО НА ДИСКЕ", RamCaption = "ОПЕРАТИВНАЯ ПАМЯТЬ";
    public string? DeltaNote;
    readonly GraphToggle collapse = new() { Text = "−", Width = 30, Height = 26, AccessibleName = "Свернуть график" };
    public bool Collapsed { get; private set; }
    bool loading;
    int loadingFrame;

    readonly System.Windows.Forms.Timer loadingTimer = new() { Interval = 100 };
    public void SetLoading(bool value) { loading = value; loadingTimer.Enabled = loading && Visible; Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) { loadingTimer.Dispose(); } base.Dispose(disposing); }
    public event EventHandler? CollapseChanged;
    public Overview()
    {
        DoubleBuffered = true; Height = 325; Controls.Add(collapse);
        loadingTimer.Tick += (_, _) => { loadingFrame = (loadingFrame + 1) % 24; Invalidate(new Rectangle(Math.Max(0, Width - 98), 129, 40, 38)); };
        VisibleChanged += (_, _) => loadingTimer.Enabled = loading && Visible;
        collapse.Click += (_, _) => SetCollapsed(!Collapsed);
        Resize += (_, _) => collapse.Location = new Point(Math.Max(0, Width - 46), 131);
    }
    public void SetCollapsed(bool value)
    {
        Collapsed = value; collapse.Text = value ? "+" : "−";
        collapse.AccessibleName = value ? "Развернуть график" : "Свернуть график";
        Invalidate(); CollapseChanged?.Invoke(this, EventArgs.Empty);
    }
    void TextAt(Graphics g, string text, float size, Color color, float x, float y, bool bold = false)
    { if (Theme.Vintage) color = color == Theme.Pink ? Color.SandyBrown : Theme.Phosphor; if(size>=25 && Theme.Style != "Modern"){RetroDisplay.Number(g,text,x,y,35,color);return;}using var f = Theme.Font(size, bold); using var b = new SolidBrush(color); g.DrawString(text, f, b, x, y); }
    void Card(Graphics g, RectangleF r)
    {
        if (Theme.Style == "Modern") { Glass.Paint(g,r); return; }
        if (Theme.Style == "Vintage") {
            using var fill = new SolidBrush(Color.FromArgb(31,36,47)); g.FillRectangle(fill,r);
            using var vintageLines = new Pen(Color.FromArgb(10,Theme.Phosphor));
            for (float y=r.Top+4;y<r.Bottom-2;y+=4) g.DrawLine(vintageLines,r.Left+2,y,r.Right-2,y);
            if (r.Top>0) { using var head = new SolidBrush(Theme.Bg); g.FillRectangle(head,r.Left+2,r.Top+2,r.Width-4,36); }
            ControlPaint.DrawBorder3D(g,Rectangle.Round(r),Border3DStyle.Sunken); return;
        }
        using var b = new SolidBrush(Theme.Panel);g.FillRectangle(b,r);using var pen=new Pen(Theme.Line);g.DrawRectangle(pen,r.X+.5f,r.Y+.5f,r.Width-1,r.Height-1);
        using var lines=new Pen(Color.FromArgb(10,Theme.Mint));for(float y=r.Top+4;y<r.Bottom;y+=4)g.DrawLine(lines,r.Left+1,y,r.Right-1,y);
        using var edge=new Pen(Color.FromArgb(135,Theme.Mint),2);g.DrawLine(edge,r.Left+1,r.Top+1,r.Left+19,r.Top+1);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Bg);
        float w = Width, gap = 14, cw = (w - gap * 2) / 3;
        for (int i = 0; i < 3; i++) Card(g, new RectangleF(i * (cw + gap), 0, cw, 110));
        TextAt(g, FreeCaption, 9, Theme.Muted, 19, 15, true);
        TextAt(g, Current == null ? "Пока нет замера" : Theme.Bytes(Current.Free), 25, Theme.Mint, 17, 37, true);
        TextAt(g, Current == null ? "Первый замер появится после старта" : "из " + Theme.Bytes(Current.Total), 9, Theme.Muted, 20, 83);
        float x2 = cw + gap + 19;
        TextAt(g, "ИЗМЕНЕНИЕ СВОБОДНОГО МЕСТА", 8.5f, Theme.Muted, x2, 15, true);
        long? delta = Points.Count > 1 ? (long)Points[^1][1]! - (long)Points[0][1]! : null;
        TextAt(g, delta.HasValue ? Theme.Bytes(delta.Value, true) : "Ещё собираю", 25, delta < 0 ? Theme.Pink : Theme.Lilac, x2 - 2, 37, true);
        TextAt(g, DeltaNote ?? "между первым и последним замером", 8.5f, Theme.Muted, x2, 83);
        float x3 = 2 * (cw + gap) + 19;
        TextAt(g, RamCaption, 9, Theme.Muted, x3, 15, true);
        TextAt(g, Current == null ? "—" : Theme.Bytes(Current.Ram), 25, Theme.Lilac, x3 - 2, 37, true);
        TextAt(g, Current == null ? "" : "занято из " + Theme.Bytes(Current.RamTotal), 9, Theme.Muted, x3, 83);
        Card(g, new RectangleF(0, 124, w, Collapsed ? 40 : 188));
        const TextFormatFlags align=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.Left|TextFormatFlags.NoPadding;
        using var heading=Theme.Font(12,true); using var note=Theme.Font(9);
        TextRenderer.DrawText(g,"Как менялось свободное место",heading,new Rectangle(20,124,Width-345,40),Theme.Ink,align);
        TextRenderer.DrawText(g,loading ? "Обновляю данные…" : Period,note,new Rectangle(Width-320,124,220,40),Theme.Muted,align);
        if (loading) LoadingMascot.Draw(g, new Rectangle((int)w - 94, 131, 32, 32), loadingFrame * 15);
        if (Collapsed) return;
        var plot = new RectangleF(24, 183, w - 184, 91);
        using var grid = new Pen(Theme.Line);
        for (int i = 0; i < 3; i++) g.DrawLine(grid, plot.Left, plot.Top + i * plot.Height / 2, plot.Right, plot.Top + i * plot.Height / 2);
        if (Points.Count > 0)
        {
            long min = Points.Min(r => (long)r[1]!), max = Points.Max(r => (long)r[1]!);
            double range = Math.Max(256L << 20, max - min); double low = min - range * .12; range *= 1.24;
            long t0 = (long)Points[0][0]!, t1 = (long)Points[^1][0]!;
            var pts = Points.Select(r => new PointF(Points.Count == 1 ? plot.Right : plot.Left + (float)(((long)r[0]! - t0) / (double)Math.Max(1, t1 - t0) * plot.Width), plot.Bottom - (float)(((long)r[1]! - low) / range * plot.Height))).ToArray();
            using var pen = new Pen(Theme.Phosphor, 1.8f);using var glow = new Pen(Color.FromArgb(35,Theme.Phosphor),7);
            for (int i = 1; i < pts.Length; i++)
            { if ((long)Points[i][0]! - (long)Points[i - 1][0]! < MaximumGap) {g.DrawLine(glow,pts[i-1],pts[i]);g.DrawLine(pen, pts[i - 1], pts[i]);} else { using var dot = new SolidBrush(Theme.Phosphor); g.FillEllipse(dot, pts[i].X - 2, pts[i].Y - 2, 4, 4); } }
            // The badge uses the plotted sample, never an independently refreshed live value.
            var last = pts[^1];
            using var level = new Pen(Color.FromArgb(70, Theme.Phosphor)) { DashStyle = DashStyle.Dot };
            g.DrawLine(level, plot.Left, last.Y, plot.Right, last.Y);
            using var halo = new SolidBrush(Color.FromArgb(45, Theme.Phosphor)); using var dotInk = new SolidBrush(Theme.Phosphor);
            g.FillEllipse(halo, last.X - 6, last.Y - 6, 12, 12); g.FillEllipse(dotInk, last.X - 3, last.Y - 3, 6, 6);
            float badgeY = Math.Clamp(last.Y - 21, plot.Top, plot.Bottom - 42);
            var badge = new RectangleF(plot.Right + 12, badgeY, 134, 42);
            using var badgeBg = new SolidBrush(Theme.Vintage ? Color.FromArgb(31,36,47) : Theme.Bg); using var badgeBorder = new Pen(Theme.Phosphor);
            g.FillRectangle(badgeBg, badge); g.DrawRectangle(badgeBorder, badge.X, badge.Y, badge.Width, badge.Height);
            g.DrawLine(badgeBorder, last.X + 4, last.Y, badge.Left, badge.Top + badge.Height / 2);
            TextAt(g, Theme.Bytes((long)Points[^1][1]!), 11, Theme.Phosphor, badge.Left + 7, badge.Top + 3, true);
            TextAt(g, HistoryPeriod.Local(t1).ToString(ShowDates ? "dd.MM HH:mm" : "HH:mm:ss") + " · замер", 8, Theme.Muted, badge.Left + 7, badge.Top + 24);
            TextAt(g, Theme.Bytes((long)(low + range)), 8, Theme.Muted, plot.Right + 12, plot.Top - 17);
            TextAt(g, Theme.Bytes((long)low), 8, Theme.Muted, plot.Right + 12, plot.Bottom + 10);
            TextAt(g, DateTimeOffset.FromUnixTimeSeconds(t0).LocalDateTime.ToString(ShowDates ? "dd.MM HH:mm" : "HH:mm"), 8, Theme.Muted, plot.Left, 284);
        }
        else TextAt(g, "За выбранный период пока нет замеров.", 11, Theme.Muted, 24, 217);
        
    }
}
internal sealed class MainForm : Form
{


    Monitor monitor;
    readonly Monitor primary;
    readonly Dictionary<string, Monitor> monitors = new(StringComparer.OrdinalIgnoreCase);
    readonly ComboBox drivePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Mint };
    readonly Overview overview = new();
    readonly RetroDatePicker day = new() { Width = 130, Height = 30, AccessibleName = "Начало периода" };
    readonly RetroDatePicker endDay = new() { Width = 130, Height = 30, AccessibleName = "Конец периода" };
    readonly ModePicker mode = new() { Width=180, Height=30, AccessibleName="Представление истории" };
    readonly TextBox search = new() { PlaceholderText = "Найти файл или папку…", Width = 195, BorderStyle = BorderStyle.FixedSingle };
    readonly FolderHistoryView folderTree = new() { Visible = false };
    readonly Label periodHint = new() { AutoSize = true, ForeColor = Theme.Muted, Padding = new Padding(12, 7, 0, 0), Font = Theme.Font(8.5f) };
    readonly Dictionary<string, SoftButton> periodButtons = new();
    string preset = "1D";
    bool updatingDates, refreshAgain, multiDay;
    readonly DataGridView table = new BufferedGrid();
    readonly Label health = new(), empty = new(), subtitle = new();
    readonly SoftButton start = new() { Text = "Начать наблюдение", Width = 185, Accent = Theme.Mint, ForeColor = Theme.Bg };
    readonly SoftButton pause = new() { Text = "Пауза", Width = 132 };
    readonly SoftButton scan = new() { Text = "Сверить", Width = 112 };
    readonly ScanProgress scanProgress = new();
    readonly System.Windows.Forms.Timer progressRefresh = new() { Interval = 1000 };

    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 10000 };
    readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    bool exiting, refreshing;
    readonly HistoryTable tablePresentation = new();
    int renderedMode = -1;
    const int TablePageSize = 100;
    int tablePage;
    long earliestSample = Store.Now;
    List<DataGridViewRow> tableData = new();
    readonly Dictionary<int, (int Column, bool Descending)> tableSorts = new();
    readonly SoftButton refreshTable = new() { Text = "Обновить", Dock = DockStyle.Right, Width = 112 };
    readonly Panel tablePager = new() { Dock = DockStyle.Bottom, Height = 41, Padding=new Padding(0,7,0,0) };
    readonly Label pageLabel = new() { AutoSize = false, Width = 220, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted };
    readonly SoftButton prevPage = new() { Text = "←", Width = 45, Height = 28 };
    readonly SoftButton nextPage = new() { Text = "→", Width = 45, Height = 28 };
    public bool StartInTray;
    List<object?[]> rows = new();
    List<object?[]> latestSamples = new();
    public MainForm(Monitor m)
    {
        monitor = primary = m; monitors[m.Config.Root] = m; Text = "Крошка " + Program.Version + " — история места на диске"; BackColor = Theme.Bg; ForeColor = Theme.Ink;
        RetroDisplay.Window(this);
        Font = Theme.Font(10); ClientSize = new Size(1100, 830); MinimumSize = new Size(1040, 780); StartPosition = FormStartPosition.CenterScreen;
        Icon = ThemeArt.WindowIcon();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 18), RowCount = 7, ColumnCount = 1, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 77)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 325));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Paint += (_, e) => {
            e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(ThemeArt.Picture("Mascot"),new Rectangle(0,0,70,70));
        };
        var title = new LedTitle { Location = new Point(83, 0), Width = 195 };
        var version = new Label { Text = "v" + Program.Version, AutoSize = true, Location = new Point(286, 14), ForeColor = Theme.Mint, Font = Theme.Font(13) };
        subtitle.Text = Program.Preview ? "DEMO // ВЫМЫШЛЕННЫЕ ДАННЫЕ" : "МОНИТОР ДИСКА // ЛОКАЛЬНАЯ ИСТОРИЯ";
        subtitle.SetBounds(83, 46, 450, 23); subtitle.ForeColor = Theme.Muted; subtitle.Font = Theme.Font(9);
        var settings = new HeaderIcon(true) { AccessibleName = "Настройки", Width = 44, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        header.Resize += (_, _) => settings.Location = new Point(header.Width - 44, 10);
        drivePicker.AccessibleName = "Диск для просмотра";
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)) drivePicker.Items.Add(drive.Name);
        if (!drivePicker.Items.Contains(m.Config.Root)) drivePicker.Items.Add(m.Config.Root);
        drivePicker.SelectedItem = m.Config.Root;
        var disk = new DriveButton { Width = 195, Height = 38, AccessibleName = "Выбрать диск" };
        var updates = new HeaderIcon(false) { Width = 44, AccessibleName = "Обновления приложения" };
        var diskMenu = MenuArt.Create();
        string DriveText(string drive) {
            try { var label = new DriveInfo(drive).VolumeLabel; return (string.IsNullOrWhiteSpace(label) ? "Диск" : label) + " (" + drive.TrimEnd('\\') + ")"; }
            catch (IOException) { return drive; }
        }
        void SetDiskTitle() => disk.Text = DriveText((string)drivePicker.SelectedItem!);
        disk.Click += (_,_) => {
            diskMenu.Items.Clear();
            foreach (var item in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady)) {
                string drive = item.Name;
                diskMenu.Items.Add(DriveText(drive), null, (_,_) => { if (!drivePicker.Items.Contains(drive)) drivePicker.Items.Add(drive); drivePicker.SelectedItem = drive; });
            }
            diskMenu.Show(disk,0,disk.Height);
        };
        SetDiskTitle(); drivePicker.SelectedIndexChanged += (_,_) => SetDiskTitle();
        header.Resize += (_,_) => { disk.Location = new(header.Width-249,10); updates.Location = new(header.Width-303,10); };
        var tips = new ToolTip(); tips.SetToolTip(settings,"Настройки"); tips.SetToolTip(updates,"Проверить обновления"); tips.SetToolTip(disk,"Выбрать диск");
        var updater = new ReleaseCheck(this, updates, () => { exiting = true; Close(); });
        updates.Click += async (_,_) => await updater.Check(true);
        FormClosed += (_,_) => { tips.Dispose(); diskMenu.Dispose(); drivePicker.Dispose(); };

        drivePicker.SelectedIndexChanged += async (_, _) => await SelectDrive();
        settings.Click += (_, _) => ShowSettings(); header.Controls.AddRange([title, version, subtitle, updates, disk, settings]); root.Controls.Add(header, 0, 0);
        overview.Dock = DockStyle.Fill; root.Controls.Add(overview, 0, 1);
        overview.CollapseChanged += (_, _) =>
        {
            root.RowStyles[1].Height = overview.Collapsed ? 172 : 325;
            monitor.Config.GraphCollapsed = overview.Collapsed;
            if (!Program.Preview) monitor.Config.Save(monitor.DataDir);
        };
        overview.SetCollapsed(monitor.Config.GraphCollapsed);
        var filterHost = new Panel { Dock = DockStyle.Fill };
        var periodRow = new Panel { Dock=DockStyle.Top, Height=32 };
        var periods = new FlowLayoutPanel { Dock=DockStyle.Left, AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, WrapContents=false };
        periodRow.Controls.Add(folderTree.Summary);
        periodRow.Controls.Add(periods);
        foreach (string p in new[] { "1D", "1W", "1M", "3M", "1Y", "ALL" })
        {
            var button = new SoftButton { Text = p, Width = 52, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            periodButtons[p] = button; button.Click += (_, _) => SelectPeriod(p); periods.Controls.Add(button);
        }
        periods.Controls.Add(periodHint);
        var filterRow = new Panel { Dock=DockStyle.Bottom, Height=34 };
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34, WrapContents = false, Padding = Padding.Empty };
        var label = new Label { Text = "С", AutoSize = false, Width = 18, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink };
        var endLabel = new Label { Text = "по", AutoSize = false, Width = 32, Height = 30, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink };
        search.BackColor=Theme.Surface; search.ForeColor=Theme.Ink;
        var export = new SoftButton { Text = "Экспорт CSV", Width = 120, Height = 30 }; export.Click += (_, _) => Export();
        var searchBox = new Panel { Width = search.Width, Height = 30, BackColor = Theme.Panel };
        search.BorderStyle = BorderStyle.None; search.AutoSize = true;
        void AlignSearch() { search.SetBounds(7,(searchBox.ClientSize.Height-search.PreferredHeight)/2,searchBox.ClientSize.Width-14,search.PreferredHeight); }
        searchBox.Controls.Add(search); searchBox.Resize += (_,_) => AlignSearch(); search.FontChanged += (_,_) => AlignSearch(); AlignSearch();
        searchBox.Paint += (_,e) => { using var pen = new Pen(Theme.Line); e.Graphics.DrawRectangle(pen,0,0,searchBox.Width-1,searchBox.Height-1); };
        foreach (Control control in new Control[] { label, day, endLabel, endDay, mode, searchBox, export }) control.Margin = new Padding(0, 0, 6, 0);
        folderTree.RemovedFilter.Dock=DockStyle.None;
        folderTree.RemovedFilter.Size=searchBox.Size;
        folderTree.RemovedFilter.Margin=searchBox.Margin;
        filters.Controls.AddRange([label, day, endLabel, endDay, mode, searchBox, folderTree.RemovedFilter, export]);
        var refreshSlot=new Panel { Dock=DockStyle.Right, Width=112, Padding=new Padding(0,0,0,4) };
        refreshTable.Dock=folderTree.RefreshButton.Dock=DockStyle.Fill;
        refreshSlot.Controls.Add(refreshTable);
        refreshSlot.Controls.Add(folderTree.RefreshButton);
        filterRow.Controls.Add(filters);
        filterRow.Controls.Add(refreshSlot);
        filterHost.Controls.Add(filterRow); filterHost.Controls.Add(periodRow); root.Controls.Add(filterHost, 0, 2);
        StyleTable(); var tablePanel = new Panel { Dock = DockStyle.Fill }; table.Dock = DockStyle.Fill;
        empty.Dock = DockStyle.Fill; empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = Theme.Muted; empty.Font = Theme.Font(12);
        tablePanel.Controls.Add(table); tablePanel.Controls.Add(tablePager);
        refreshTable.Click += (_, _) => RefreshView(); tablePanel.Controls.Add(empty);
        var pagerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        pagerFlow.Controls.AddRange([prevPage, pageLabel, nextPage]); tablePager.Controls.Add(pagerFlow);
        prevPage.Click += (_, _) => { tablePage--; RenderTablePage(); };
        nextPage.Click += (_, _) => { tablePage++; RenderTablePage(); };
        table.ColumnHeaderMouseClick += (_, e) => {
            bool desc = tableSorts.TryGetValue(renderedMode, out var sort) && sort.Column == e.ColumnIndex ? !sort.Descending : false;
            tableSorts[renderedMode] = (e.ColumnIndex, desc); tablePage = 0; SortTableData(); RenderTablePage();
        }; tablePanel.Controls.Add(folderTree); root.Controls.Add(tablePanel, 0, 3);
        folderTree.FilesRemoved = async (paths, drive) => {
            if (monitors.TryGetValue(drive.EndsWith('\\') ? drive : drive + "\\", out var owner))
                await Task.Run(() => owner.RecordManualRemovals(paths));
            else throw new IOException("Наблюдатель диска недоступен.");
        };
        folderTree.SnapshotChanged += (_, _) => { if (!IsDisposed && mode.SelectedIndex == 4) ApplyTreeComparison(); };
        scanProgress.Dock = DockStyle.Fill; scanProgress.Visible = false; root.Controls.Add(scanProgress, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new Padding(0, 5, 0, 9), Margin = Padding.Empty };
        start.Height = pause.Height = scan.Height = 40;
        start.Click += (_, _) => StartMonitoring(); pause.Click += (_, _) => { monitor.TogglePause(); pause.Text = monitor.Paused ? "Продолжить" : "Пауза"; RefreshView(); };
        scan.Click += (_, _) => { monitor.RequestScan(); RefreshView(); };
        var trayButton = new SoftButton { Text = "Свернуть в трей", Width = 164, Height = 40 }; trayButton.Click += (_, _) => Hide();
        foreach (var b in new[] { start, pause, scan, trayButton }) b.Margin = new Padding(0, 0, 10, 0);
        actions.Controls.AddRange([start, pause, scan, trayButton]); root.Controls.Add(actions, 0, 5);
        health.Dock = DockStyle.Fill; health.AutoSize = true; health.MinimumSize = new Size(0, 23); health.ForeColor = Theme.Muted; health.Font = Theme.Font(8); health.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(health, 0, 6);
        tray = new NotifyIcon { Text = "Крошка · история диска", Icon = Icon, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Open(); };
        var menu = new ContextMenuStrip { BackColor = Theme.Panel, ForeColor = Theme.Ink, Font = Theme.Font(10), Renderer = new ToolStripProfessionalRenderer(new RetroMenuColors()) { RoundedEdges = false } }; menu.Items.Add("Открыть Крошку", null, (_, _) => Open());
        menu.Items.Add("Пауза / продолжить", null, (_, _) => { if (monitor.Config.Monitoring) monitor.TogglePause(); });
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Выйти", null, (_, _) => { exiting = true; Close(); }); tray.ContextMenuStrip = menu;
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { foreach (var extra in monitors.Values.Where(x => x != primary)) extra.Dispose(); refresh.Stop(); debounce.Stop(); progressRefresh.Dispose(); tray.Dispose(); };
        Shown += async (_, _) => {
            foreach (string drive in primary.Config.ExtraDrives.ToArray())
                if (drivePicker.Items.Contains(drive)) { try { GetMonitor(drive); } catch (Exception ex) { health.Text = "Диск " + drive + ": " + ex.Message; } }
            if (drivePicker.Items.Contains(primary.Config.SelectedDrive)) drivePicker.SelectedItem = primary.Config.SelectedDrive;
            if (StartInTray) Hide(); else RefreshView();
            if (!Program.Preview) await updater.Check(false);
        };
        day.ValueChanged += (_, _) =>
        {
            if (updatingDates) return;
            updatingDates = true;
            if (preset == "1D" || day.Value > endDay.Value) endDay.Value = day.Value;
            else preset = "CUSTOM";
            updatingDates = false; UpdatePeriodButtons(); RefreshView();
        };
        endDay.ValueChanged += (_, _) =>
        {
            if (updatingDates) return;
            updatingDates = true; if (endDay.Value < day.Value) day.Value = endDay.Value;
            preset = "CUSTOM"; updatingDates = false; UpdatePeriodButtons(); RefreshView();
        };
        void ShowModeControls()
        {
            bool tree=mode.SelectedIndex==4;
            folderTree.Summary.Visible=folderTree.RemovedFilter.Visible=folderTree.RefreshButton.Visible=tree;
            searchBox.Visible=refreshTable.Visible=!tree;
            table.Visible=tablePager.Visible=!tree;
            folderTree.Visible=tree;
        }
        ShowModeControls();
        mode.SelectedIndexChanged += (_, _) => { ShowModeControls(); RefreshView(); }; UpdatePeriodButtons();
        search.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); }; debounce.Tick += (_, _) => { debounce.Stop(); RefreshView(); };
        refresh.Tick += (_, _) => { if (Visible) RefreshView(false); }; refresh.Start();
        progressRefresh.Tick += (_, _) => RefreshProgress();
        VisibleChanged += (_, _) => { progressRefresh.Enabled = Visible; RefreshProgress(); };
        progressRefresh.Start();
        table.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Details(e.RowIndex); };
    }
    internal async Task CaptureDemo(string destination)
    {
        mode.SelectedIndex=4;
        while (refreshing || folderTree.IsLoading) await Task.Delay(60);
        using var frame=new Bitmap(Width,Height);
        DrawToBitmap(frame,new Rectangle(0,0,Width,Height));
        frame.Save(destination,System.Drawing.Imaging.ImageFormat.Png);
    }

    Monitor GetMonitor(string drive)
    {
        if (monitors.TryGetValue(drive, out var existing)) return existing;
        string dir = DriveProfiles.DirectoryFor(primary.DataDir, primary.Config.Root, drive);
        var config = Settings.Load(dir); config.Root = drive; config.Monitoring = true;
        var next = new Monitor(dir, config); monitors[drive] = next;
        if (!Program.Preview) next.Start();
        return next;
    }
    async Task SelectDrive()
    {
        if (drivePicker.SelectedItem is not string drive || drive == monitor.Config.Root) return;
        drivePicker.Enabled = false;
        try
        {
            // Let the current view finish before changing its database owner.
            while (refreshing) await Task.Delay(50);
            monitor = GetMonitor(drive);
            primary.Config.ExtraDrives = monitors.Keys.Where(d => d != primary.Config.Root).ToList();
            primary.Config.SelectedDrive = drive;
            if (!Program.Preview) primary.Config.Save(primary.DataDir);
            folderTree.Visible = false; renderedMode = -1; overview.SetCollapsed(monitor.Config.GraphCollapsed); RefreshView();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось открыть диск"); drivePicker.SelectedItem = monitor.Config.Root; }
        finally { drivePicker.Enabled = true; }
    }
    void SelectPeriod(string value)
    {
        preset = value; updatingDates = true;
        if (preset == "ALL") endDay.Value = DateTime.Today;
        long earliest = earliestSample;
        var range = HistoryPeriod.Select(preset, day.Value, endDay.Value, earliest);
        day.Value = HistoryPeriod.Local(range.From).Date;
        updatingDates = false; UpdatePeriodButtons(); RefreshView();
    }
    void UpdatePeriodButtons()
    {
        foreach (var (name, button) in periodButtons) { button.Accent = preset == name ? Theme.Mint : Theme.Panel; button.ForeColor = preset == name ? Theme.Bg : Theme.Ink; button.Invalidate(); }
    }
    void RefreshProgress()
    {
        if (IsDisposed) return;
        scanProgress.Visible = monitor.Scanning || (monitor.Paused && monitor.Config.Monitoring);
        scanProgress.UpdateState(monitor.Scanning, monitor.Paused, Interlocked.Read(ref monitor.Scanned), Interlocked.Read(ref monitor.ScanEstimate), monitor.ScanPhase, Visible);
        pause.Text = monitor.Paused ? "Продолжить" : "Пауза";
        scan.Enabled = monitor.Config.Monitoring && !monitor.Scanning;
        start.Visible = !monitor.Config.Monitoring && !Program.Preview; pause.Enabled = monitor.Config.Monitoring;
        health.Text = monitor.Health + "   ·   История: 1 год";
        health.ForeColor = monitor.NeedsReconcile && !monitor.Scanning ? Theme.Pink : Theme.Muted;
    }
    void Open() { StartInTray=false; Show(); WindowState = FormWindowState.Normal; Activate(); RefreshView(); }
    public void OpenWindow() => Open();
    void StyleTable()
    {
        HistoryTable.AttachFormatting(table);
        table.BackgroundColor = Theme.Panel; table.BorderStyle = BorderStyle.None; table.EnableHeadersVisualStyles = false;
        table.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel, SelectionBackColor=Theme.Panel, SelectionForeColor=Theme.Muted, ForeColor = Theme.Muted, Font = Theme.Font(9, true), Padding = new Padding(10, 0, 0, 0) };
        table.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel, ForeColor = Theme.Ink, SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Mint, Font = Theme.Font(9), Padding = new Padding(10, 0, 0, 0) };
        table.GridColor = Theme.Line; table.RowTemplate.Height = 43; table.ColumnHeadersHeight = 36; table.RowHeadersVisible = false;
        table.AllowUserToAddRows = false; table.AllowUserToDeleteRows = false; table.ReadOnly = true; table.MultiSelect = false;
        table.SelectionMode = DataGridViewSelectionMode.FullRowSelect; table.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        table.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; table.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
    }
    void StartMonitoring()
    {
        if (Program.Preview) return;
        try { monitor.Start(); RefreshView(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Не удалось начать"); }
    }
    async void RefreshView(bool updateTable = true)
    {
        if (IsDisposed) return;
        if (refreshing) { if (updateTable) refreshAgain = true; return; } refreshing = true;
        if (updateTable) overview.SetLoading(true);
        RefreshProgress();
        try
        {
            var active = monitor;
            var metadata = await Task.Run(() => {
                long first = (long?)active.Db.Query("SELECT min(ts) FROM samples")[0][0] ?? Store.Now;
                long.TryParse(active.Db.Meta("baseline_ts"), out long firstMap);
                return (first, firstMap, ready: active.Db.Meta("baseline") == "done");
            });
            if (IsDisposed || monitor != active) return;
            long earliest = earliestSample = metadata.first;
            var period = HistoryPeriod.Select(preset, day.Value, endDay.Value, earliest);
            long from = period.From, to = period.To;
            long step = Math.Max(60, (to - Math.Max(from, earliest)) / 1000);
            multiDay = HistoryPeriod.Local(from).Date != HistoryPeriod.Local(to - 1).Date;
            int selectedMode = mode.SelectedIndex; string filter = search.Text.Trim();
            periodHint.Text = $"История с {HistoryPeriod.Local(earliest):dd.MM.yy HH:mm}";
            search.Enabled = selectedMode != 4;
            folderTree.Visible = selectedMode == 4; table.Visible = selectedMode != 4; tablePager.Visible = selectedMode != 4;
            if (selectedMode == 4)
            {
                empty.Visible = false; folderTree.BringToFront();
                long baseline = metadata.firstMap;
                baseline = Math.Max(baseline, new DateTimeOffset(DateTime.Today.AddDays(-366)).ToUnixTimeSeconds());
                if (!metadata.ready) baseline = 0;
                folderTree.SetPeriod(Path.Combine(monitor.DataDir, "history.db"), monitor.Config.Root, period, baseline);
                ApplyTreeComparison();
                return;
            }
            var result = await Task.Run(() =>
            {
                List<object?[]> samples; Sample? current = null;
                // Freeze the two reads together so the live card cannot jump ahead of the graph.
                lock (monitor.Db.Gate)
                {
                    samples = monitor.Db.Query("SELECT ts,free,page,vss,ram,ramtotal,total FROM samples WHERE ts IN (SELECT min(ts) FROM samples WHERE ts>=? AND ts<? GROUP BY (ts-?)/?) OR ts=(SELECT max(ts) FROM samples WHERE ts>=? AND ts<?) ORDER BY ts", from, to, from, step, from, to);
                    var last = monitor.Db.Query("SELECT ts,free,total,ram,ramtotal,page,vss FROM samples ORDER BY ts DESC LIMIT 1").FirstOrDefault();
                    if (last != null) current = new((long)last[0]!, (long)last[1]!, (long)last[2]!, (long)last[3]!, (long)last[4]!, (long)last[5]!, (long?)last[6]);
                }
                List<object?[]> changes;
                if (!updateTable && renderedMode == selectedMode) changes = rows;
                else if (selectedMode == 3) changes = monitor.Db.Query("SELECT ts,text FROM notes WHERE ts>=? AND ts<? ORDER BY ts DESC LIMIT 500", from, to);
                else if (selectedMode == 2) changes = samples;
                else changes = monitor.Db.Query("SELECT path,sum(delta),min(bucket),max(bucket),group_concat(DISTINCT kind) FROM changes WHERE bucket>=? AND bucket<? AND (?='' OR instr(lower(path),lower(?))>0) GROUP BY path HAVING sum(delta)<>0 ORDER BY abs(sum(delta)) DESC LIMIT 2000", from, to, filter, filter);
                if (selectedMode == 1 && (updateTable || renderedMode != selectedMode)) changes = changes.GroupBy(r => Path.GetDirectoryName((string)r[0]!) ?? (string)r[0]!).Select(g => new object?[] { g.Key, g.Sum(r => (long)r[1]!), g.Min(r => (long)r[2]!), g.Max(r => (long)r[3]!), string.Join(", ", g.Select(r => (string)r[4]!).Distinct()) }).OrderByDescending(r => Math.Abs((long)r[1]!)).ToList();
                return (samples, changes, current);
            });
            if (IsDisposed) return;
            if (selectedMode != mode.SelectedIndex) { refreshAgain = true; return; }
            bool tableChanged = renderedMode != selectedMode || !TablePages.EqualRows(rows, result.changes);
            latestSamples = result.samples; rows = result.changes;
            overview.Points = latestSamples; overview.Current = result.current;
            overview.FreeCaption = "СВОБОДНО НА ДИСКЕ"; overview.RamCaption = "ОПЕРАТИВНАЯ ПАМЯТЬ"; overview.DeltaNote = null;
            overview.Period = period.Caption; overview.ShowDates = multiDay; overview.MaximumGap = Math.Max(180, step * 2); overview.Invalidate();
            if (!tableChanged) return;
            if (renderedMode != selectedMode) tablePage = 0;
            table.SuspendLayout(); table.Rows.Clear(); table.Columns.Clear();
            var prepared = new List<DataGridViewRow>();
            int AddRow(params object?[] values) { var row = new DataGridViewRow(); row.CreateCells(table, values); row.Height = table.RowTemplate.Height; prepared.Add(row); return prepared.Count - 1; }
            if (selectedMode == 3)
            {
                table.Columns.Add("time", "Время"); table.Columns.Add("note", "Событие наблюдения"); table.Columns[0].FillWeight = 15; table.Columns[1].FillWeight = 85;
                foreach (var row in rows) AddRow(Time((long)row[0]!), row[1]);
            }
            else if (selectedMode == 2)
            {
                table.Columns.Add("time", "Время"); table.Columns.Add("page", "Подкачка на диске"); table.Columns.Add("vss", "Теневые копии"); table.Columns.Add("ram", "Занято ОЗУ");
                foreach (var row in rows.AsEnumerable().Reverse()) AddRow(Time((long)row[0]!), (long)row[2]! < 0 ? null : row[2], row[3], row[4]);
            }
            else
            {
                table.Columns.Add("path", selectedMode == 0 ? "Файл · двойной щелчок для подробностей" : "Папка · сумма показанных файлов");
                table.Columns.Add("size", "Свободное ±"); table.Columns.Add("when", "Интервал / источник");
                table.Columns[1].ToolTipText = "Вклад в свободное место: + освободилось, − занялось. Оценка по записанным размерам.";
                table.Columns[0].FillWeight = 65; table.Columns[1].FillWeight = 15; table.Columns[2].FillWeight = 20;
                foreach (var row in rows)
                {
                    long d = HistoryTable.FreeSpaceEffect((long)row[1]!); int i = AddRow(row[0], d, Time((long)row[2]!) + " · " + row[4]);
                    prepared[i].Tag = row;
                    prepared[i].Cells[1].Style.ForeColor = d < 0 ? Theme.Pink : Theme.Mint;
                    prepared[i].Cells[0].ToolTipText = (string)row[0]!;
                }
            }
            tableData = prepared;
            tablePresentation.Restore(table, selectedMode); renderedMode = selectedMode;
            foreach (DataGridViewColumn column in table.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
            SortTableData(); RenderTablePage();
            table.ResumeLayout(); empty.Visible = rows.Count == 0;
            if (empty.Visible) empty.BringToFront();
            empty.Text = monitor.Scanning ? "Сначала познакомимся с диском 🐾\n\nПервая карта не считается новыми файлами.\nИстория изменений начнётся после её построения." : "За этот день записей пока нет.\n\nВыбери другую дату или начни наблюдение.\nИсторию до установки восстановить нельзя.";
            health.Text = monitor.Health + "   ·   История: 1 год" + (rows.Count >= 2000 ? " · показаны 2000 крупнейших изменений; уточни поиск" : "");
            start.Visible = !monitor.Config.Monitoring && !Program.Preview; pause.Enabled = monitor.Config.Monitoring; scan.Enabled = monitor.Config.Monitoring && !monitor.Scanning;
        }
        catch (Exception ex) { health.Text = "Не удалось прочитать историю: " + ex.Message; }
        finally { refreshing = false; if (!IsDisposed && (mode.SelectedIndex != 4 || folderTree.Snapshot != null)) overview.SetLoading(false); if (refreshAgain && !IsDisposed) { refreshAgain = false; BeginInvoke(() => RefreshView()); } }
    }
    void SortTableData()
    {
        if (tableSorts.TryGetValue(renderedMode, out var sort))
            tableData = TablePages.Sort(tableData, sort.Column, sort.Descending);
    }
    void RenderTablePage()
    {
        if (table.Columns.Count == 0) return;
        tablePage = Math.Clamp(tablePage, 0, Math.Max(0, (tableData.Count - 1) / TablePageSize));
        table.SuspendLayout(); table.Rows.Clear();
        table.Rows.AddRange(tableData.Skip(tablePage * TablePageSize).Take(TablePageSize).ToArray());
        pageLabel.Text = tableData.Count == 0 ? "Нет записей" : $"{tablePage * TablePageSize + 1}–{Math.Min((tablePage + 1) * TablePageSize, tableData.Count)} / {tableData.Count:N0}";
        prevPage.Enabled = tablePage > 0; nextPage.Enabled = (tablePage + 1) * TablePageSize < tableData.Count;
        foreach (DataGridViewColumn c in table.Columns)
            c.HeaderCell.SortGlyphDirection = tableSorts.TryGetValue(renderedMode, out var sort) && c.Index == sort.Column ? sort.Descending ? SortOrder.Descending : SortOrder.Ascending : SortOrder.None;
        table.ResumeLayout();
    }
    void ApplyTreeComparison()
    {
        var snapshot = folderTree.Snapshot;
        if (snapshot == null) { overview.SetLoading(folderTree.IsLoading); if (!folderTree.IsLoading) { overview.Points = new(); overview.Current = null; overview.Invalidate(); } return; }
        overview.SetLoading(false);
        overview.Points = snapshot?.Samples ?? new();
        var last = overview.Points.LastOrDefault();
        overview.Current = last == null ? null : new((long)last[0]!, (long)last[1]!, (long)last[6]!, (long)last[4]!, (long)last[5]!, (long)last[2]!, (long?)last[3]);
        overview.FreeCaption = "СВОБОДНО НА КОНЕЦ СРАВНЕНИЯ"; overview.RamCaption = "ОЗУ НА КОНЕЦ СРАВНЕНИЯ";
        overview.Period = snapshot?.Period.Caption ?? "Сопоставляю с деревом…";
        overview.ShowDates = snapshot != null && HistoryPeriod.Local(snapshot.Period.From).Date != HistoryPeriod.Local(snapshot.Period.To).Date;
        overview.MaximumGap = snapshot == null ? 180 : Math.Max(180, (snapshot.Period.To - snapshot.Period.From) / 1000 * 2);
        string fmt = overview.ShowDates ? "dd.MM HH:mm" : "HH:mm:ss";
        overview.DeltaNote = overview.Points.Count > 1
            ? $"{HistoryPeriod.Local((long)overview.Points[0][0]!).ToString(fmt)} → {HistoryPeriod.Local((long)last![0]!).ToString(fmt)} · замеры"
            : "Нет пары замеров в интервале дерева";
        overview.Invalidate();
    }
    string Time(long ts) => DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime.ToString(multiDay ? "dd.MM HH:mm" : "HH:mm");
    void Details(int i)
    {
        if (mode.SelectedIndex > 1 || i >= table.Rows.Count || table.Rows[i].Tag is not object?[] r) return;
        string p = (string)r[0]!;
        using var dialog = new Form { Text = "Изменение файла", BackColor = Theme.Bg, ForeColor = Theme.Ink, Font = Theme.Font(11), ClientSize = new Size(720, 280), StartPosition = FormStartPosition.CenterParent };
        RetroDisplay.Window(dialog);
        var text = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Ink, Location = new Point(24, 24), Size = new Size(670, 170), Text = p + "\r\n\r\nСвободное место (оценка): " + Theme.Bytes(HistoryTable.FreeSpaceEffect((long)r[1]!), true) + "\r\nЗамеры: " + Time((long)r[2]!) + "–" + Time((long)r[3]! + 300) + " · " + r[4] + "\r\n«Сверка» — время обнаружения, не точное время изменения." };
        var open = new SoftButton { Text = "Открыть расположение", Width = 210, Location = new Point(24, 215) };
        open.Click += (_, _) => { string? dir = Directory.Exists(p) ? p : Path.GetDirectoryName(p); while (dir != null && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir); if (dir != null) OpenFolder(dialog, dir); };
        var copy = new SoftButton { Text = "Копировать путь", Width = 170, Location = new Point(248, 215) }; copy.Click += (_, _) => Clipboard.SetText(p);
        dialog.Controls.AddRange([text, open, copy]); dialog.ShowDialog(this);
    }
    void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "Крошка-" + day.Value.ToString("yyyy-MM-dd") + ".csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string Escape(object? obj) { string s = obj?.ToString() ?? ""; if (s.Length > 0 && "=+@-".Contains(s[0])) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var exportTable = mode.SelectedIndex == 4 ? folderTree.Grid : table;
        var sb = new StringBuilder(); sb.AppendLine(string.Join(";", exportTable.Columns.Cast<DataGridViewColumn>().Select(c => Escape(c.HeaderText))));
        foreach (DataGridViewRow row in (mode.SelectedIndex == 4 ? exportTable.Rows.Cast<DataGridViewRow>() : tableData)) sb.AppendLine(string.Join(";", row.Cells.Cast<DataGridViewCell>().Select((c, i) => Escape(TablePages.ExportValue(c, exportTable.Columns[i].Name)))));
        try { File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true)); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }
    void ShowSettings()
    {
        using var f = new Form { Text = "Крошка · настройки", BackColor = Theme.Bg, ForeColor = Theme.Ink, Font = Theme.Font(10), ClientSize = new Size(730, 622), StartPosition = FormStartPosition.CenterParent, MaximizeBox = false };
        RetroDisplay.Window(f);
        var startup = new Label { Text = "Автозапуск настраивается вручную через Windows (shell:startup).", AutoSize = true, Location = new Point(26, 25), ForeColor = Theme.Muted };
        var info = new Label { Text = "Наблюдаем: " + monitor.Config.Root + "\n\n• Место на диске и ОЗУ: один замер в минуту.\n• Изменения файлов: пакетами, с интервалами по 5 минут.\n• Полный обход: первая карта и только по кнопке «Сверить».\n• История: 1 год. Удаление файлов — только с подтверждением.\n• Содержимое файлов не читается и никуда не отправляется.\n\nСканирование: два потока, пакетная запись, без искусственных пауз.\nИндекс занимает место: обычно десятки или сотни МБ.\nПосле перерыва возможны пропуски истории; они отмечаются явно.\nЗакрытые папки и ссылки пропускаются; график учитывает весь диск.\n\nСумма логических размеров отличается от физически занятого места\nиз-за сжатия, разреженных файлов, жёстких ссылок и служебных данных.\nПеремещение: плюс у старого пути, минус у нового.", Location = new Point(26, 65), Size = new Size(680, 330), ForeColor = Theme.Muted };
        var system = new SoftButton { Text = "Замер теневых копий…", Width = 230, Location = new Point(26, 410), Enabled = !Program.Preview };
        system.Click += async (_, _) =>
        {
            system.Enabled = false;
            try
            {
                var psi = new ProcessStartInfo(Program.Exe) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                psi.ArgumentList.Add("--probe"); psi.ArgumentList.Add("--data"); psi.ArgumentList.Add(monitor.DataDir);
                psi.ArgumentList.Add("--root"); psi.ArgumentList.Add(monitor.Config.Root);
                using var proc = Process.Start(psi); if (proc != null) await proc.WaitForExitAsync();
                string report = Path.Combine(monitor.DataDir, "system-probe.json");
                if (proc?.ExitCode == 0 && File.Exists(report))
                {
                    var s = JsonSerializer.Deserialize<Sample>(File.ReadAllText(report))!;
                    monitor.Db.SaveSample(s, monitor.Config.Root);
                    monitor.Db.Note(s.Vss.HasValue ? "Ручной системный замер: теневые копии " + Theme.Bytes(s.Vss.Value) : "Системный замер не смог прочитать теневые копии.");
                    MessageBox.Show(f, s.Vss.HasValue ? "Под теневые копии выделено " + Theme.Bytes(s.Vss.Value) + ". Замер сохранён в историю." : "Размер теневых копий недоступен.", "Системный замер");
                }
            }
            catch (Exception ex) { MessageBox.Show(f, "Замер не выполнен: " + ex.Message); }
            finally { if (!system.IsDisposed) system.Enabled = true; RefreshView(); }
        };
        var folder = new SoftButton { Text = "Папка истории", Width = 170, Location = new Point(272, 410) }; folder.Click += (_, _) => OpenFolder(f, monitor.DataDir);
        var foot = new Label { Text = "Для замера теневых копий Windows покажет запрос администратора.\nЗакрытие окна оставляет Крошку в трее. Выход — через меню значка.", Location = new Point(26, 467), Size = new Size(680, 50), ForeColor = Theme.Muted, Font = Theme.Font(9) };
        var themeTitle = new Label { Text="Тема",AutoSize=true,Location=new Point(26,532) };
        var picker = new FlowLayoutPanel { Location=new Point(90,520),Size=new Size(612,87),WrapContents=false };
        foreach (var (id,label) in new[]{("Classic","Классика"),("Vintage","Винтаж"),("Modern","Модерн")}) {
            var tile=new ThemeTile(id,label); picker.Controls.Add(tile);
            tile.Click += (_,_) => {
                if(Theme.Style==id) return;
                var old=Theme.Colors(); primary.Config.Appearance=id;
                if(!Program.Preview) primary.Config.Save(primary.DataDir);
                Theme.Select(id); Appearance.Apply(this,old); Appearance.Apply(f,old);
                tray.Icon=Icon;
                foreach(var row in tableData) Appearance.Row(row,old);
                search.BackColor=search.Parent!.BackColor=Theme.Surface;
                folderTree.Restyle(); UpdatePeriodButtons();
            };
        }
        f.Controls.AddRange([startup,info,system,folder,foot,themeTitle,picker]); f.ShowDialog(this);
    }
    static void OpenFolder(IWin32Window owner, string path)
    {
        try
        {
            string fullPath = PhysicalPath.DirectoryName(path);
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("Папка не найдена: " + fullPath);
            // Let the Windows shell open the directory itself, without Explorer's command-line parser.
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex) { MessageBox.Show(owner, "Не удалось открыть папку:\n" + ex.Message, "Крошка", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}




