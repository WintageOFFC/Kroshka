using System.ComponentModel;

namespace Kroshka;

internal sealed record FolderEntry(string Path, bool Directory, long Before, long After, long Delta)
{
    public bool ExistsNow { get; init; } = true;
    public long FreeSpaceEffect => HistoryTable.FreeSpaceEffect(Delta);
}
internal sealed record FolderListing(FolderEntry Total, List<FolderEntry> Children, long Count);
internal sealed record FolderComparison(HistoryPeriod Period, FolderListing Listing, List<object?[]> Samples)
{
    public HashSet<string> RemovedNow { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public long? FreeDelta => Samples.Count > 1 ? (long)Samples[^1][1]! - (long)Samples[0][1]! : null;
    public long EstimatedFreeDelta => Listing.Total.FreeSpaceEffect;
    public long? Difference => FreeDelta - EstimatedFreeDelta;
}

internal sealed class FolderHistoryView : UserControl
{
    sealed class Node(FolderEntry entry, int depth)
    {
        public FolderEntry Entry = entry;
        public int Depth = depth;
        public bool Expanded, Loading;
        public List<Node>? Children;
        public long ChildCount;
        public List<FolderEntry>? Cached;
    }
    const int PageSize = 250;
    sealed record More(Node Parent);
    readonly SoftButton reload = new() { Text = "Обновить", Width = 112, Dock = DockStyle.Right, Height = 30 };
    readonly Label comparison = new() { AutoEllipsis=true, Dock = DockStyle.Fill, ForeColor = Theme.Mint, Font = Theme.Font(9), TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolTip explanation = new();
    readonly System.Windows.Forms.Timer animation = new() { Interval = 100 };
    int frame;
    bool deleting;
    readonly CheckBox showRemoved = new() { Text = "Показать удалённые", Dock = DockStyle.Right, Width = 185, ForeColor = Theme.Muted, Font = Theme.Font(9), FlatStyle = FlatStyle.Flat };
    HashSet<string> removedNow = new(StringComparer.OrdinalIgnoreCase);
    public Func<IReadOnlyList<string>, string, Task>? FilesRemoved { get; set; }
    public static bool HideRemoved(string path, bool directory, long periodEnd, bool show, ISet<string> removed, bool existsNow = true) =>
        !directory && !show && periodEnd > Store.Now && (!existsNow || removed.Contains(path));
    public FolderComparison? Snapshot { get; private set; }
    public bool IsLoading { get; private set; }
    public event EventHandler? SnapshotChanged;
    public DataGridView Grid { get; } = new BufferedGrid();
    Node? root;
    string dbPath = "", rootPath = "", key = "";
    HistoryPeriod requested;
    long baseline, from, to;
    int revision, sortColumn = 3;
    bool descending = true;
    readonly SemaphoreSlim reads = new(1, 1);

    public Control Summary => comparison;
    public Control RemovedFilter => showRemoved;
    public Control RefreshButton => reload;

    public FolderHistoryView()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Bg;
        showRemoved.CheckedChanged += (_, _) => Render();
        explanation.SetToolTip(showRemoved, "Удалённые файлы скрыты в текущем периоде. Их история и исторические итоги сохранены. В прошлых периодах они всегда видны.");
        explanation.SetToolTip(comparison, "На диске — реальный замер свободного места Windows.\nВ таблице — оценка вклада файлов по их размерам.\nВезде плюс освобождает место, минус занимает.");
        Grid.Dock = DockStyle.Fill; Grid.BackgroundColor = Theme.Surface; Grid.BorderStyle = BorderStyle.None;
        Grid.EnableHeadersVisualStyles = false; Grid.ColumnHeadersDefaultCellStyle = new() { BackColor = Theme.Panel, SelectionBackColor=Theme.Panel, SelectionForeColor=Theme.Muted, ForeColor = Theme.Muted, Font = Theme.Font(9, true) };
        Grid.DefaultCellStyle = new() { BackColor = Theme.Panel, ForeColor = Theme.Ink, SelectionBackColor = Theme.Line, SelectionForeColor = Theme.Mint, Font = Theme.Font(9) };
        Grid.GridColor = Theme.Line; Grid.RowTemplate.Height = 31; Grid.ColumnHeadersHeight = 32; Grid.RowHeadersVisible = false;
        Grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        Grid.AllowUserToAddRows = Grid.AllowUserToDeleteRows = false; Grid.ReadOnly = true; Grid.MultiSelect = true;
        Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        Grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        Grid.Columns.Add("path", "Папка / файл"); Grid.Columns.Add("before", "Размер был"); Grid.Columns.Add("after", "Размер стал"); Grid.Columns.Add("size", "Свободное ±");
        Grid.Columns[3].ToolTipText = "Вклад в свободное место (оценка по файлам): + освободилось, − занялось.";
        Grid.Columns[0].FillWeight = 55;
        foreach (DataGridViewColumn c in Grid.Columns) { c.SortMode = DataGridViewColumnSortMode.Programmatic; if (c.Index > 0) { c.ValueType = typeof(long); c.DefaultCellStyle.NullValue = "Нет данных"; c.FillWeight = 15; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft; c.DefaultCellStyle.Padding = new Padding(0); } }
        HistoryTable.AttachFormatting(Grid);
        Controls.Add(Grid);
        Grid.CellClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || ModifierKeys != Keys.None) return;
            // Only the [+] / [-] glyph expands. Clicking the filename selects it.
            if (Grid.Rows[e.RowIndex].Tag is Node hit && !OnExpander(hit)) return;
            if (Grid.Rows[e.RowIndex].Tag is Node n) await Toggle(n);
            else if (Grid.Rows[e.RowIndex].Tag is More more) await LoadMore(more.Parent);
        };
        Grid.KeyDown += async (_, e) =>
        {
            if (Grid.CurrentRow?.Tag is not Node n) return;
            if (e.KeyCode == Keys.Right && !n.Expanded || e.KeyCode == Keys.Left && n.Expanded) { e.Handled = true; await Toggle(n); }
        };
        Grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 0 && Grid.Rows[e.RowIndex].Tag is Node n && n.Entry.Directory && ModifierKeys == Keys.None && !OnExpander(n)) await Toggle(n); };
        Grid.CellPainting += PaintLoading;
        animation.Tick += (_, _) => { frame = (frame + 1) % 24; foreach (DataGridViewRow row in Grid.Rows) if (row.Tag is Node n && n.Loading) Grid.InvalidateCell(0, row.Index); };
        VisibleChanged += (_, _) => UpdateAnimation();
        var menu = MenuArt.Create();
        var openLocation = menu.Items.Add("Открыть расположение", ThemeArt.Picture("MenuLocation"));
        openLocation.Padding = new Padding(5, 4, 8, 4);
        menu.Items.Add(new ToolStripSeparator());
        var delete = menu.Items.Add("Переместить в корзину…", ThemeArt.Picture("MenuRecycle"));
        delete.Padding = new Padding(5, 4, 8, 4);
        menu.Opening += (_, _) => {
            openLocation.Image=ThemeArt.Picture("MenuLocation"); delete.Image=ThemeArt.Picture("MenuRecycle");
            delete.Enabled = !Program.Preview && !deleting && SelectedFiles().Count > 0 && !Grid.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Tag is Node n && n.Entry.Directory);
            openLocation.Enabled = Grid.CurrentRow?.Tag is Node;
        };
        delete.Click += async (_, _) => await DeleteSelected();
        openLocation.Click += async (_, _) => {
            if (Grid.CurrentRow?.Tag is not Node n) return;
            try { await ShellFiles.OpenLocation(n.Entry.Path); }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Расположение недоступно"); }
        };
        Grid.ContextMenuStrip = menu;
        Grid.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !Grid.Rows[e.RowIndex].Selected) { Grid.ClearSelection(); Grid.CurrentCell = Grid.Rows[e.RowIndex].Cells[0]; Grid.Rows[e.RowIndex].Selected = true; } };
        Grid.ColumnHeaderMouseClick += (_, e) => { descending = sortColumn == e.ColumnIndex ? !descending : e.ColumnIndex != 0; sortColumn = e.ColumnIndex; Render(); };
        reload.Click += async (_, _) => await Reload();
    }

    public void SetPeriod(string database, string drive, HistoryPeriod period, long firstMap)
    {
        string next = database + "|" + drive + "|" + period + "|" + firstMap;
        if (next == key) return;
        key = next; removedNow.Clear(); dbPath = database; rootPath = drive; requested = period; baseline = firstMap;
        _ = Reload();
    }

    async Task<FolderListing?> Read(string path, string folder, long begin, long end, int version, int offset = 0)
    {
        await reads.WaitAsync();
        try
        {
            if (IsDisposed || version != revision) return null;
            return await Task.Run(() => { using var db = new Store(path, true); db.Exec("BEGIN"); try { return db.FolderChildrenAll(folder, begin, end); } finally { db.Exec("ROLLBACK"); } });
        }
        finally { reads.Release(); }
    }

    async Task Reload()
    {
        int version = ++revision; IsLoading = true; root = null; Grid.Rows.Clear(); reload.Enabled = false;
        Snapshot = null; comparison.Text = "Сопоставляю свободное место и файлы…"; SnapshotChanged?.Invoke(this, EventArgs.Empty);
        // Completed five-minute buckets give every expanded folder the same end time.
        from = Math.Max(requested.From, baseline);
        to = Math.Min(requested.To, Store.Now / 300 * 300);
        if (baseline == 0 || from >= to)
        {
            comparison.Text = baseline == 0 ? "Сначала нужно завершить первую карту диска." : "За этот период ещё нет карты и завершённых 5-минутных интервалов.";
            IsLoading = false; reload.Enabled = true; SnapshotChanged?.Invoke(this, EventArgs.Empty); return;
        }
        comparison.Text = "Собираю дерево из сохранённой карты…";
        string path = dbPath, drive = rootPath; long begin = from, end = to;
        try
        {
            FolderComparison snapshot;
            await reads.WaitAsync();
            try
            {
                if (IsDisposed || version != revision) return;
                snapshot = await Task.Run(() => { using var db = new Store(path, true); return db.ReadComparison(drive, new(begin, end)); });
            }
            finally { reads.Release(); }
            if (IsDisposed || version != revision) return;
            var listing = snapshot.Listing;
            root = new(listing.Total, 0) { Expanded = true, Children = listing.Children.Take(PageSize).Select(e => new Node(e, 1)).ToList(), ChildCount = listing.Count, Cached = listing.Children };
            removedNow = snapshot.RemovedNow; Snapshot = snapshot; IsLoading = false;
            comparison.Text = snapshot.FreeDelta is long free
                ? $"Свободного: {Theme.Bytes(free, true)} · файлы — оценка"
                : "Недостаточно замеров свободного места для сопоставления.";
            explanation.SetToolTip(comparison, "На диске — реальный замер Windows; в таблице — оценка по файлам.\nВезде + означает освобождение, − означает занятие места.\nРазница учёта: "
                + (snapshot.Difference is long difference ? Theme.Bytes(difference, true) : "нет пары замеров")
                + ". Возможны служебные данные, сжатие и пропуски наблюдения.");
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            Render(); UpdateStatus();
        }
        catch (Exception ex) { if (!IsDisposed && version == revision) comparison.Text = "Не удалось собрать дерево: " + ex.Message; }
        finally { if (!IsDisposed && version == revision) { IsLoading = false; reload.Enabled = true; SnapshotChanged?.Invoke(this, EventArgs.Empty); } }
    }

    async Task Toggle(Node node)
    {
        if (!node.Entry.Directory || node.Loading) return;
        if (node.Expanded) { node.Expanded = false; Render(); return; }
        if (node.Children == null)
        {
            int version = revision; node.Loading = true; UpdateAnimation(); Grid.Invalidate();
            string path = dbPath; long begin = from, end = to;
            try
            {
                var listing = await Read(path, node.Entry.Path, begin, end, version);
                if (listing == null || IsDisposed || version != revision) return;
                node.Cached = listing.Children; node.Children = listing.Children.Take(PageSize).Select(e => new Node(e, node.Depth + 1)).ToList(); node.ChildCount = listing.Count;
            }
            catch (Exception ex) { if (!IsDisposed && version == revision) comparison.Text = "Папка не открыта: " + ex.Message; return; }
            finally { node.Loading = false; UpdateAnimation(); Grid.Invalidate(); }
        }
        node.Expanded = true; Render(); UpdateStatus();
    }

    Task LoadMore(Node node)
    {
        if (node.Children != null && node.Cached != null)
        {
            node.Children.AddRange(node.Cached.Skip(node.Children.Count).Take(PageSize).Select(e => new Node(e, node.Depth + 1)));
            Render();
        }
        return Task.CompletedTask;
    }

    bool OnExpander(Node node)
    {
        int left = Grid.GetCellDisplayRectangle(0, Grid.CurrentRow?.Index ?? 0, false).Left;
        if(Theme.Vintage) return Grid.PointToClient(Cursor.Position).X <= left+15+14*node.Depth;
        return Grid.PointToClient(Cursor.Position).X <= left + 5 + (node.Depth * 3 + 4) * TextRenderer.MeasureText(" ", Grid.DefaultCellStyle.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
    }
    List<string> SelectedFiles() => Grid.SelectedRows.Cast<DataGridViewRow>()
        .Where(r => r.Tag is Node n && !n.Entry.Directory).Select(r => ((Node)r.Tag!).Entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    async Task DeleteSelected()
    {
        var paths = SelectedFiles();
        if (deleting || Program.Preview || paths.Count == 0) return;
        if (Grid.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Tag is Node n && n.Entry.Directory))
        { MessageBox.Show(this, "Выбери только файлы. Папки этим действием не удаляются.", "Удаление файлов"); return; }
        if (!FileRemoval.Confirm(FindForm()!, paths)) return;
        deleting = true;
        string operationKey = key, drive = rootPath, data = Path.GetDirectoryName(dbPath)!;
        try
        {
            var result = await FileRemoval.Recycle(paths, drive, data);
            if (!IsDisposed && operationKey == key)
            {
                removedNow.UnionWith(result.Removed.Select(Monitor.Key));
                Render(); // Reflect only confirmed successes, before waiting for database work.
            }
            string? historyWarning = null;
            if (result.Removed.Count > 0 && FilesRemoved != null)
            {
                try { await FilesRemoved(result.Removed, drive); }
                catch (Exception ex) { historyWarning = ex.Message; }
            }
            if (IsDisposed) return;
            if (operationKey == key) comparison.Text = $"В корзину: {result.Removed.Count}. История сохранена.";
            var warnings = result.Failures.ToList();
            if (result.ShellWarning != null) warnings.Add("Не удалось обновить значок корзины: " + result.ShellWarning);
            if (historyWarning != null) warnings.Add("Удаление выполнено, запись истории ожидает наблюдателя: " + historyWarning);
            if (warnings.Count > 0) MessageBox.Show(this, string.Join("\n", warnings), "Результат удаления");
        }
        finally { deleting = false; }
    }

    void UpdateAnimation() => animation.Enabled = Visible && Grid.Rows.Cast<DataGridViewRow>().Any(r => r.Tag is Node n && n.Loading);
    void PaintLoading(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0 || Grid.Rows[e.RowIndex].Tag is not Node n) return;
        if (Theme.Vintage && n.Entry.Directory) {
            e.Paint(e.ClipBounds,e.PaintParts & ~DataGridViewPaintParts.ContentForeground); e.Handled=true;
            var g=e.Graphics!; var box=e.CellBounds; int x=box.Left+5+14*n.Depth,y=box.Top+box.Height/2;
            using var ink=new SolidBrush(Theme.Ink);
            Point[] arrow=n.Expanded ? [new(x,y-2),new(x+8,y-2),new(x+4,y+3)] : [new(x+1,y-4),new(x+1,y+4),new(x+6,y)];
            g.FillPolygon(ink,arrow); x+=13;
            using var gold=new SolidBrush(Color.Gold);
            g.FillRectangle(gold,x,y-5,7,4);g.FillRectangle(gold,x,y-3,15,10);g.DrawRectangle(Pens.SaddleBrown,x,y-3,15,10);
            string title=n.Depth==0?n.Entry.Path.TrimEnd((char)92)+(char)92:Path.GetFileName(n.Entry.Path);
            TextRenderer.DrawText(g,title,Grid.DefaultCellStyle.Font,new Rectangle(x+19,box.Top,Math.Max(1,box.Right-x-19),box.Height),Theme.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        if (!n.Loading) return;
        if (!e.Handled) { e.Paint(e.ClipBounds,e.PaintParts); e.Handled=true; }
        var area = new Rectangle(e.CellBounds.Right - 34, e.CellBounds.Top + 1, 29, 29);
        using var back = new SolidBrush(Grid.Rows[e.RowIndex].Selected ? Theme.Line : Theme.Panel); e.Graphics!.FillRectangle(back, area);
        LoadingMascot.Draw(e.Graphics, area, frame * 15);
    }
    void UpdateStatus() => explanation.SetToolTip(reload, $"{HistoryPeriod.Local(from):dd.MM.yy HH:mm} → {HistoryPeriod.Local(to):dd.MM.yy HH:mm} · по журналу, возможны пропуски");

    public void Restyle() => Render();
    void Render()
    {
        if (root == null || IsDisposed) return;
        var selected = Grid.SelectedRows.Cast<DataGridViewRow>().Where(r => r.Tag is Node).Select(r => ((Node)r.Tag!).Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? current = (Grid.CurrentRow?.Tag as Node)?.Entry.Path;
        int scroll = Grid.FirstDisplayedScrollingRowIndex;
        Grid.SuspendLayout(); Grid.Rows.Clear();
        var prepared = new List<DataGridViewRow>();
        int AddRow(params object?[] values) { var row = new DataGridViewRow(); row.CreateCells(Grid, values); row.Height = Grid.RowTemplate.Height; prepared.Add(row); return prepared.Count - 1; }
        void Add(Node n)
        {
            if (HideRemoved(n.Entry.Path, n.Entry.Directory, requested.To, showRemoved.Checked, removedNow, n.Entry.ExistsNow)) return;
            string name = n.Depth == 0 ? n.Entry.Path + "\\" : Path.GetFileName(n.Entry.Path);
            if (!n.Entry.Directory && (!n.Entry.ExistsNow || removedNow.Contains(n.Entry.Path))) name += "  [удалён сейчас]";
            int index = AddRow(new string(' ', n.Depth * 3) + (n.Entry.Directory ? Theme.Style == "Classic" ? (n.Expanded ? "[-] " : "[+] ") : (n.Expanded ? "▼ " : "▶ ") : "    ") + name, n.Entry.Before < 0 ? null : (object)n.Entry.Before, n.Entry.After < 0 ? null : (object)n.Entry.After, n.Entry.FreeSpaceEffect);
            var row = prepared[index]; row.Tag = n; row.Cells[0].ToolTipText = n.Entry.Path;
            row.Cells[3].Style.ForeColor = n.Entry.FreeSpaceEffect < 0 ? Theme.Pink : Theme.Mint;
            if (!n.Entry.Directory && (!n.Entry.ExistsNow || removedNow.Contains(n.Entry.Path))) { row.Cells[0].Style.ForeColor = Theme.Muted; row.Cells[0].ToolTipText += "\nФайл удалён сейчас. Размеры относятся к выбранному историческому интервалу."; }
            if (n.Entry.Before < 0 || n.Entry.After < 0) { row.Cells[1].ToolTipText = row.Cells[2].ToolTipText = "История неполная: отрицательный восстановленный размер указывает на пропуск замеров."; }
            if (!n.Expanded || n.Children == null) return;
            Func<Node, object> sort = sortColumn switch { 1 => n => n.Entry.Before, 2 => n => n.Entry.After, 3 => n => n.Entry.FreeSpaceEffect, _ => n => n.Entry.Path };
            var ordered = descending ? n.Children.OrderByDescending(sort) : n.Children.OrderBy(sort);
            foreach (var child in ordered.ThenBy(n => n.Entry.Path, StringComparer.OrdinalIgnoreCase)) Add(child);
            if (n.ChildCount > n.Children.Count)
            {
                int i = AddRow(new string(' ', (n.Depth + 1) * 3) + $"… Показать ещё ({n.Children.Count:N0} / {n.ChildCount:N0})");
                prepared[i].Tag = new More(n); prepared[i].Cells[0].Style.ForeColor = Theme.Mint;
            }
        }
        Add(root);
        Grid.Rows.AddRange(prepared.ToArray());
        var currentRow = prepared.FirstOrDefault(r => r.Tag is Node n && n.Entry.Path == current);
        if (currentRow != null) Grid.CurrentCell = currentRow.Cells[0];
        foreach (DataGridViewColumn c in Grid.Columns) c.HeaderCell.SortGlyphDirection = c.Index == sortColumn ? descending ? SortOrder.Descending : SortOrder.Ascending : SortOrder.None;
        if (scroll >= 0 && scroll < Grid.Rows.Count) Grid.FirstDisplayedScrollingRowIndex = scroll;
        Grid.ClearSelection();
        foreach (DataGridViewRow row in Grid.Rows) if (row.Tag is Node n && selected.Contains(n.Entry.Path)) row.Selected = true;
        Grid.ResumeLayout(); UpdateAnimation();
    }
    protected override void Dispose(bool disposing) { revision++; if (disposing) { animation.Dispose(); explanation.Dispose(); Grid.ContextMenuStrip?.Dispose(); } base.Dispose(disposing); }
}

