using System.Diagnostics;
using System.Text.Json;

namespace Kroshka;
internal static class Tests
{
    public static int RecycleSmoke(string dir)
    {
        string fixture = Path.Combine(dir, "recycle-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string file = Path.Combine(fixture, "kroshka-recycle-test.txt");
        File.WriteAllText(file, "Disposable Kroshka regression test. No user data.");
        long before = ShellFiles.RecycleCount(Path.GetPathRoot(fixture)!);
        var result = FileRemoval.Recycle([file, Path.Combine(fixture, "does-not-exist.txt")], fixture, Path.Combine(dir, "protected-history"), false).GetAwaiter().GetResult();
        long after = ShellFiles.RecycleCount(Path.GetPathRoot(fixture)!);
        bool success = result.Removed.SequenceEqual(new[] { file }) && result.Failures.Count == 1 && result.ShellWarning == null && FileRemoval.ConfirmedMissing(file) && after > before;
        File.WriteAllText(Path.Combine(dir, "recycle-test.json"), JsonSerializer.Serialize(new { success, before, after, result, fixture }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir); var lines = new List<string>();
        void Assert(bool yes, string name) { if (!yes) throw new Exception("FAIL: " + name); lines.Add("PASS: " + name); }
        try
        {
            Sorting(Assert);
            FolderPeriods(dir, Assert);
            ManualRemoval(dir, Assert);
            Assert(DriveProfiles.DirectoryFor(dir, @"C:\", @"C:\") == dir, "primary drive keeps original database path");
            Assert(DriveProfiles.DirectoryFor(dir, @"C:\", @"D:\") == Path.Combine(dir, "drives", "D"), "secondary drive uses isolated history");
            string fixture = Path.Combine(dir, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
            string canonical = PhysicalPath.DirectoryName(fixture);
            Assert(Directory.Exists(canonical) && !canonical.StartsWith(@"\\?\"), "physical folder path can be opened outside MSIX virtualization");
            string dbdir = Path.Combine(dir, "database-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dbdir);
            string a = Path.Combine(fixture, "кириллица ' [100%].bin");
            File.WriteAllBytes(a, new byte[1024]);
            Assert(FileRemoval.Validate(a, fixture, dbdir) == Path.GetFullPath(a), "delete validates a regular selected file");
            bool guarded = false;
            try { FileRemoval.Validate(a, fixture, fixture); } catch (IOException) { guarded = true; }
            Assert(guarded, "delete cannot remove history files");
            guarded = false;
            try { FileRemoval.Validate(a, dbdir, dbdir); } catch (IOException) { guarded = true; }
            Assert(guarded, "delete cannot escape selected root");
            guarded = false;
            try { FileRemoval.Validate(fixture, Path.GetDirectoryName(fixture)!, dbdir); } catch (IOException) { guarded = true; }
            Assert(guarded, "delete rejects directories");
            using (var db = new Store(Path.Combine(dbdir, "unit.db")))
            {
                db.Put(Monitor.Key(a), 1024, 1, false, "baseline");
                Assert(db.Query("SELECT * FROM changes").Count == 0, "baseline creates no fake growth");
                db.Put(Monitor.Key(a), 4096, 2, true, "test");
                Assert((long)db.Query("SELECT sum(delta) FROM changes")[0][0]! == 3072, "growth delta and Unicode path binding");
                db.Put(Monitor.Key(a), 4096, 3, true, "test");
                Assert((long)db.Query("SELECT sum(delta) FROM changes")[0][0]! == 3072, "duplicate notifications are idempotent");
                db.RemoveTree(Monitor.Key(a), true, "test");
                Assert((long)db.Query("SELECT sum(delta) FROM changes")[0][0]! == -1024, "deletion net delta is correct");
                db.Put(@"c:\a\b\nested.bin", 2000, 4, false, "test"); db.Put(@"c:\a\bc\keep.bin", 3000, 4, false, "test");
                db.RemoveTree(@"c:\a\b", true, "test");
                Assert(db.Query("SELECT * FROM files WHERE path=?", @"c:\a\bc\keep.bin").Count == 1, "directory deletion respects path boundary");
                Assert(db.Query("PRAGMA integrity_check")[0][0]?.ToString() == "ok", "SQLite integrity");
                db.PutBatch([new(@"c:\batch\a", 100), new(@"c:\batch\b", 200)], 5, false, "batch");
                db.PutBatch([new(@"c:\batch\a", 150), new(@"c:\batch\b", 200), new(@"c:\batch\c", 300)], 6, true, "batch");
                db.PutBatch([new(@"c:\batch\a", 150)], 7, true, "batch");
                Assert((long)db.Query("SELECT sum(delta) FROM changes WHERE kind='batch'")[0][0]! == 350, "batch growth, creation and duplicate events reconcile");
                long t = Store.Now;
                db.SaveSample(new(t, 10000, 20000, 1000, 2000, 3000, 4000), @"C:\");
                db.SaveSample(new(t + 60, 8500, 20000, 1000, 2000, 4000, null), @"C:\");
                db.SaveSample(new(t + 120, 8000, 20000, 1000, 2000, 4000, 4500), @"C:\");
                Assert((long)db.Query("SELECT sum(delta) FROM changes WHERE kind='Системный замер'")[0][0]! == 1000, "pagefile growth attributed separately");
                Assert((long)db.Query("SELECT sum(delta) FROM changes WHERE kind='Теневые копии'")[0][0]! == 500, "missing VSS sample is not mistaken for zero");
            }
            var config = new Settings { Root = fixture };
            using (var m = new Monitor(dbdir, config))
            {
                m.Start();
                Wait(() => m.Db.Meta("baseline") == "done", 20000);
                Assert(m.Db.Query("SELECT * FROM changes").Count == 0, "real initial scan remains a baseline");
                File.WriteAllBytes(a, new byte[8192]);
                Wait(() => m.Db.Query("SELECT size FROM files WHERE path=?", Monitor.Key(a)).FirstOrDefault()?[0] is long n && n == 8192, 15000);
                Assert((long)m.Db.Query("SELECT sum(delta) FROM changes")[0][0]! == 7168, "watcher sees file growth");
                string b = Path.Combine(fixture, "renamed.bin"); File.Move(a, b);
                Wait(() => m.Db.Query("SELECT size FROM files WHERE path=?", Monitor.Key(b)).Count == 1, 15000);
                Assert((long)m.Db.Query("SELECT sum(delta) FROM changes")[0][0]! == 7168, "rename has zero net space cost");
                string nested = Path.Combine(fixture, "new-dir"); Directory.CreateDirectory(nested); File.WriteAllBytes(Path.Combine(nested, "new.bin"), new byte[10000]);
                Wait(() => m.Db.Query("SELECT size FROM files WHERE path=?", Monitor.Key(Path.Combine(nested, "new.bin"))).Count == 1, 15000);
                Assert(true, "moved-in/new directory contents discovered");
                File.Delete(b);
                Wait(() => m.Db.Query("SELECT size FROM files WHERE path=?", Monitor.Key(b)).Count == 0, 15000);
                Assert((long)m.Db.Query("SELECT sum(delta) FROM changes")[0][0]! == 8976, "delete + create total reconciles");
                Assert(m.Db.Query("SELECT * FROM samples").Count > 0, "disk/RAM/pagefile samples persisted");
                Assert(long.TryParse(m.Db.Meta("last_scan_count"), out var count) && count > 0, "completed map count persisted for progress estimate");
                m.TogglePause(); Assert(Settings.Load(dbdir).Paused, "pause preference persists");
                m.TogglePause(); Assert(!Settings.Load(dbdir).Paused, "resume preference persists");
            }
            File.WriteAllBytes(Path.Combine(fixture, "new-dir", "new.bin"), new byte[16000]);
            using (var reopened = new Monitor(dbdir, config))
            {
                Assert(reopened.Db.Query("SELECT * FROM changes").Count > 0, "history survives process restart");
                reopened.Start();
                Thread.Sleep(1200);
                Assert(!reopened.Scanning && reopened.Scanned == 0 && reopened.NeedsReconcile, "restart does not trigger a full scan; history gap is visible");
                Assert((long)reopened.Db.Query("SELECT size FROM files WHERE path=?", Monitor.Key(Path.Combine(fixture, "new-dir", "new.bin")))[0][0]! == 10000, "offline change is not invented at startup");
                reopened.RequestScan();
                Wait(() => reopened.Db.Query("SELECT size FROM files WHERE path=?", Monitor.Key(Path.Combine(fixture, "new-dir", "new.bin"))).FirstOrDefault()?[0] is long n && n == 16000, 20000);
                Assert(reopened.Db.Query("SELECT * FROM changes WHERE kind='Сверка' AND delta=6000").Count > 0, "offline growth identified as reconciliation");
                reopened.Db.Delta("old-fixture", 1, "test", Store.Now - 367L * 86400);
                reopened.Db.Delta("year-fixture", 1, "test", Store.Now - 300L * 86400);
                reopened.Db.Retain(); Assert(reopened.Db.Query("SELECT * FROM changes WHERE path='old-fixture'").Count == 0 && reopened.Db.Query("SELECT * FROM changes WHERE path='year-fixture'").Count == 1, "year history retained, expired history pruned");
            }
            File.WriteAllText(Path.Combine(dir, "test-results.txt"), string.Join(Environment.NewLine, lines)); return 0;
        }
        catch (Exception ex) { lines.Add(ex.ToString()); File.WriteAllText(Path.Combine(dir, "test-results.txt"), string.Join(Environment.NewLine, lines)); return 1; }
    }
    static void ManualRemoval(string dir, Action<bool, string> assert)
    {
        string dbFile = Path.Combine(dir, "manual-" + Guid.NewGuid().ToString("N") + ".db");
        string path = @"c:\manual\removed.bin";
        using var db = new Store(dbFile);
        db.Put(path, 123, 1, false, "baseline");
        db.RecordManualRemoval(path, true); db.RecordManualRemoval(path, true);
        db.RemoveTree(path, true, "Наблюдение");
        assert((long)db.Query("SELECT sum(delta) FROM changes WHERE path=?", path)[0][0]! == -123, "manual deletion plus duplicate watcher events never double-count bytes");
        var removed = db.ManualRemovals(@"c:\manual");
        assert(removed.Contains(path), "successful removal is persisted for immediate UI state after restart");
        assert(FolderHistoryView.HideRemoved(path, false, Store.Now + 86400, false, removed), "confirmed removal disappears from the current tree immediately");
        assert(!FolderHistoryView.HideRemoved(path, false, Store.Now - 1, false, removed) && !FolderHistoryView.HideRemoved(path, false, Store.Now + 86400, true, removed), "historical periods and show-deleted mode preserve removed entries");
        var missing = db.FolderChildrenAll(@"c:\manual", Store.Now / 300 * 300, Store.Now / 300 * 300 + 300).Children.Single();
        assert(!missing.ExistsNow && missing.Before == 123 && missing.After == 0, "current presence is distinct from historical size");
        db.Put(path, 123, 2, true, "Наблюдение");
        assert(!db.ManualRemovals(@"c:\manual").Contains(path), "restored file stops being marked deleted");
        string fixture = Path.Combine(dir, "location-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        string existing = Path.Combine(fixture, "example.txt"); File.WriteAllText(existing, "test");
        assert(ShellFiles.ExistingLocation(existing) == existing && ShellFiles.ExistingLocation(Path.Combine(fixture, "gone", "missing.txt")) == fixture, "Explorer location resolves existing file or nearest surviving parent");
        assert(!FileRemoval.ConfirmedMissing(existing) && FileRemoval.ConfirmedMissing(Path.Combine(fixture, "missing.txt")), "deletion success requires confirmed absence, not a blanket File.Exists false");
    }
    static void FolderPeriods(string dir, Action<bool, string> assert)
    {
        var last = new DateTime(2026, 10, 29);
        long earliest = new DateTimeOffset(last.AddDays(-90)).ToUnixTimeSeconds();
        var week = HistoryPeriod.Select("1W", last, last, earliest);
        assert(HistoryPeriod.Local(week.From).Date == last.AddDays(-6) && HistoryPeriod.Local(week.To).Date == last.AddDays(1), "week includes selected end day and six preceding days");
        assert(HistoryPeriod.Select("ALL", last, last, earliest).From == earliest, "ALL begins at retained history, not invented prehistory");
        assert(HistoryPeriod.Local(HistoryPeriod.Select("CUSTOM", last.AddDays(-20), last, earliest).From).Date == last.AddDays(-20), "custom calendar range preserved");
        assert(HistoryPeriod.Local(HistoryPeriod.Select("1Y", last, last, earliest).From).Date == last.AddYears(-1).AddDays(1), "year range uses calendar year");
        string file = Path.Combine(dir, "folder-test-" + Guid.NewGuid().ToString("N") + ".db");
        using var db = new Store(file);
        db.Put(@"c:\data\a\grow.bin", 170, 1, false, "test"); db.Put(@"c:\data\a\stable.bin", 20, 1, false, "test");
        db.Put(@"c:\data\new.bin", 90, 1, false, "test"); db.Put(@"c:\database\outside.bin", 500, 1, false, "test");
        db.Delta(@"c:\data\a\grow.bin", 5, "Наблюдение", 900);
        db.Delta(@"c:\data\a\grow.bin", 50, "Наблюдение", 1200);
        db.Delta(@"c:\data\old\deleted.bin", -80, "Наблюдение", 1500);
        db.Delta(@"c:\data\new.bin", 90, "Наблюдение", 1500);
        db.Delta(@"c:\data\a\grow.bin", 20, "Наблюдение", 1800);
        db.Delta(@"c:\data\removed\later.bin", -70, "Наблюдение", 2100);
        db.Delta(@"c:\data\pagefile.sys", 9999, "Системный замер", 1500);
        var result = db.FolderChildren(@"c:\data", 1200, 1800);
        assert(result.Total.Before == 270 && result.Total.After == 330 && result.Total.Delta == 60, "tree reconstructs start/end from current index, interval deltas and subsequent changes");
        var removed = result.Children.Single(r => r.Path == @"c:\data\old");
        assert(removed.Directory && removed.Before == 80 && removed.After == 0, "deleted historical folder remains expandable");
        assert(removed.FreeSpaceEffect == 80 && result.Total.FreeSpaceEffect == -60, "UI convention: deleted folder frees positive bytes; growing files consume negative bytes");
        assert(HistoryTable.FreeSpaceEffect(3072) == -3072 && HistoryTable.FreeSpaceEffect(-1024) == 1024, "file list and details use the same free-space signs as the tree");
        var nested = db.FolderChildren(@"c:\data\a", 1200, 1800);
        assert(nested.Total.Before == 120 && nested.Total.After == 170 && nested.Total.Delta == 50, "nested totals include unchanged files and exclude prefix neighbours");
        assert(db.FolderChildren(@"c:\data\removed", 1200, 1800).Total.After == 70, "deletion after selected range does not change historical end size");
        foreach (var (ts, free) in new (long, long)[] { (900, 9000), (1200, 1000), (1500, 970), (1800, 940), (2100, 7000) })
            db.Exec("INSERT INTO samples VALUES(?,?,?,?,?,?,?)", ts, free, 10000, 100, 200, 0, null);
        var comparison = db.ReadComparison(@"c:\data", new(1200, 1800));
        assert((long)comparison.Samples[0][0]! == 1200 && (long)comparison.Samples[^1][0]! == 1800 && comparison.FreeDelta == -60, "comparison excludes pre-map and later samples and includes exact end boundary");
        assert(comparison.Difference == 0 && comparison.Listing.Total.Delta == 60, "free-space and file deltas reconcile with opposite signs");
        db.Exec("UPDATE samples SET free=930 WHERE ts=1800");
        assert(db.ReadComparison(@"c:\data", new(1200, 1800)).Difference == -10, "unattributed residual stays visible instead of forcing totals to match");
        using (var reader = new Store(file, true)) assert(reader.FolderChildren(@"c:\data", 1200, 1800).Total == result.Total, "independent read-only tree connection returns consistent totals");
        db.PutBatch(Enumerable.Range(0, 2001).Select(i => new ScanFile(@"c:\wide\" + i, 1)).ToList(), 1, false, "test");
        var wide = db.FolderChildren(@"c:\wide", 1200, 1800);
        assert(wide.Count == 2001 && wide.Children.Count == 2000 && wide.Total.After == 2001, "large-folder visible limit does not truncate parent totals");
        var all = db.FolderChildrenAll(@"c:\wide", 1200, 1800);
        assert(all.Children.Count == 2001 && all.Total == wide.Total, "cached full listing supports pages without a repeated database query");
        db.Put(@"c:\edge\a\inside", 7, 1, false, "baseline");
        db.Put(@"c:\edge\ab\inside", 11, 1, false, "baseline");
        db.Put(@"c:\edge\a.txt", 13, 1, false, "baseline");
        var edge = db.FolderChildrenAll(@"c:\edge", 1200, 1800);
        assert(edge.Children.Count == 3 && edge.Total.After == 31, "indexed subtree seek preserves sibling prefixes and direct files");
        db.PutBatch(Enumerable.Range(0, 5000).Select(i => new ScanFile(@"c:\pages\file-" + i.ToString("D5"), 3)).ToList(), 1, false, "test");
        var multiPage = db.FolderChildrenAll(@"c:\pages", 1200, 1800);
        assert(multiPage.Count == 5000 && multiPage.Total.After == 15000, "indexed seek covers multiple batches of direct files without skips or duplicates");
        var pageTwo = db.FolderChildren(@"c:\wide", 1200, 1800, 2000);
        assert(pageTwo.Children.Count == 1 && pageTwo.Total.After == 2001 && !wide.Children.Any(e => e.Path == pageTwo.Children[0].Path), "large folder pagination exposes remaining entries without double counting");
    }
    static void Sorting(Action<bool, string> assert)
    {
        using var table = new BufferedGrid { AllowUserToAddRows = false };
        HistoryTable.AttachFormatting(table);
        var state = new HistoryTable();
        long[] sizes = [1, 1024, 938_910, 7_350, 1_184_891, 3L << 30, -(24L << 20), -938_910, -7_350, 0];
        void Fill()
        {
            table.Rows.Clear(); table.Columns.Clear();
            table.Columns.Add("path", "Файл"); table.Columns.Add("size", "Изменение");
            foreach (long size in sizes) table.Rows.Add(size.ToString(), size);
        }
        long[] Values() => table.Rows.Cast<DataGridViewRow>().Select(r => (long)r.Cells[1].Value).ToArray();
        Fill(); state.Restore(table, 0);
        var allRows = table.Rows.Cast<DataGridViewRow>().ToList();
        var globalSort = TablePages.Sort(allRows, 1, true);
        assert(globalSort.Take(3).Select(r => (long)r.Cells[1].Value).SequenceEqual(sizes.OrderDescending().Take(3)), "page selection happens after global numeric sorting");
        assert(TablePages.ExportValue(globalSort[0].Cells[1], "size") == Theme.Bytes(3L << 30, true), "export formats full-result rows without attaching them to the visible page");
        assert(TablePages.EqualRows(new() { new object?[] { "a", 123L } }, new() { new object?[] { "a", 123L } }) && !TablePages.EqualRows(new() { new object?[] { "a", 123L } }, new() { new object?[] { "a", 124L } }), "unchanged table snapshot is not rebuilt; changed values are detected");
        table.Sort(table.Columns["size"], System.ComponentModel.ListSortDirection.Ascending);
        assert(Values().SequenceEqual(sizes.Order()), "size header sorts signed bytes across B/KiB/MiB/GiB, ascending");
        table.Sort(table.Columns["size"], System.ComponentModel.ListSortDirection.Descending);
        assert(Values().SequenceEqual(sizes.OrderDescending()), "size header sorts signed bytes descending");
        assert(table.Rows[0].Cells[1].FormattedValue?.ToString() == Theme.Bytes(3L << 30, true), "display and CSV keep readable size units");
        state.Remember(table, 0); Fill(); state.Restore(table, 0);
        assert(Values().SequenceEqual(sizes.OrderDescending()) && table.SortOrder == SortOrder.Descending, "refresh restores numeric sorting and header direction");
        Fill(); state.Restore(table, 1);
        assert(table.SortOrder == SortOrder.None, "file and folder views keep independent sort choices");
        table.Rows.Clear(); table.Columns.Clear(); table.Columns.Add("vss", "Теневые копии");
        table.Rows.Add(new object?[] { 2L << 30 }); table.Rows.Add(new object?[] { null }); table.Rows.Add(new object?[] { 800_000L });
        state.Restore(table, 2); table.Sort(table.Columns[0], System.ComponentModel.ListSortDirection.Ascending);
        assert(table.Rows[0].Cells[0].Value == null && (long)table.Rows[1].Cells[0].Value == 800_000L, "system sizes sort numerically with missing samples");
        assert(table.Rows[0].Cells[0].FormattedValue?.ToString() == "Нет замера", "missing sample label remains readable");
    }
    static void Wait(Func<bool> condition, int timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeout) { if (condition()) return; Thread.Sleep(100); }
        throw new TimeoutException("Collector condition did not become true");
    }
}
