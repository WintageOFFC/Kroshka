using System.Runtime.InteropServices;
using System.Text;

namespace Kroshka;

// Windows 10/11 includes SQLite. No bundled database runtime or NuGet packages.
internal sealed class Store : IDisposable
{
    IntPtr db;
    readonly Dictionary<string, IntPtr> statements = new();
    public readonly object Gate = new();
    const string Lib = "winsqlite3.dll";
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int n, out IntPtr stmt, IntPtr tail);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_reset(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_clear_bindings(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] text, int n, IntPtr destroy);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_null(IntPtr stmt, int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern long sqlite3_column_int64(IntPtr stmt, int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_type(IntPtr stmt, int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr stmt);
    static byte[] Utf(string s) => Encoding.UTF8.GetBytes(s + "\0");
    void Check(int rc) { if (rc != 0) throw new IOException(Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) + " (" + rc + ")"); }
    public Store(string path, bool readOnly = false)
    {
        Check(sqlite3_open_v2(Utf(path), out db, (readOnly ? 1 : 2 | 4) | 0x10000, IntPtr.Zero));
        sqlite3_busy_timeout(db, 2500);
        if (readOnly) { Exec("PRAGMA cache_size=-4096"); return; }
        Exec("PRAGMA journal_mode=WAL"); Exec("PRAGMA synchronous=NORMAL");
        Exec("PRAGMA cache_size=-4096"); Exec("PRAGMA journal_size_limit=8388608");
        Exec("CREATE TABLE IF NOT EXISTS files(path TEXT PRIMARY KEY, size INTEGER NOT NULL, seen INTEGER NOT NULL) WITHOUT ROWID");
        Exec("CREATE TABLE IF NOT EXISTS changes(bucket INTEGER, path TEXT, delta INTEGER, kind TEXT, PRIMARY KEY(bucket,path,kind)) WITHOUT ROWID");
        Exec("CREATE INDEX IF NOT EXISTS changes_path ON changes(path,bucket)");
        Exec("CREATE TABLE IF NOT EXISTS samples(ts INTEGER PRIMARY KEY, free INTEGER, total INTEGER, ram INTEGER, ramtotal INTEGER, page INTEGER, vss INTEGER)");
        Exec("CREATE TABLE IF NOT EXISTS notes(ts INTEGER, text TEXT)");
        Exec("CREATE TABLE IF NOT EXISTS manual_removals(path TEXT PRIMARY KEY,ts INTEGER NOT NULL) WITHOUT ROWID");
        Exec("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT) WITHOUT ROWID");
        if (Meta("baseline") == "done" && Meta("baseline_ts") == null)
        {
            var firstMap = Query("SELECT min(ts) FROM notes WHERE text LIKE 'Карта готова%'")[0][0];
            Meta("baseline_ts", ((firstMap is long ts ? ts : Now) / 300 * 300).ToString());
        }
    }
    IntPtr Bind(string sql, object?[] values)
    {
        if (!statements.TryGetValue(sql, out var stmt))
        { Check(sqlite3_prepare_v2(db, Utf(sql), -1, out stmt, IntPtr.Zero)); statements.Add(sql, stmt); }
        sqlite3_reset(stmt); sqlite3_clear_bindings(stmt);
        for (int i = 0; i < values.Length; i++)
        {
            object? v = values[i];
            if (v == null) Check(sqlite3_bind_null(stmt, i + 1));
            else if (v is string s) { var b = Utf(s); Check(sqlite3_bind_text(stmt, i + 1, b, b.Length - 1, new IntPtr(-1))); }
            else Check(sqlite3_bind_int64(stmt, i + 1, Convert.ToInt64(v)));
        }
        return stmt;
    }
    public void Exec(string sql, params object?[] values)
    {
        lock (Gate)
        {
            var stmt = Bind(sql, values);
            try { int rc; while ((rc = sqlite3_step(stmt)) == 100) { } if (rc != 101) Check(rc); }
            finally { sqlite3_reset(stmt); }
        }
    }
    public List<object?[]> Query(string sql, params object?[] values)
    {
        lock (Gate)
        {
            var stmt = Bind(sql, values); var rows = new List<object?[]>();
            try
            {
                int rc;
                while ((rc = sqlite3_step(stmt)) == 100)
                {
                    var row = new object?[sqlite3_column_count(stmt)];
                    for (int i = 0; i < row.Length; i++) row[i] = sqlite3_column_type(stmt, i) switch
                    { 5 => null, 3 => Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i)), _ => sqlite3_column_int64(stmt, i) };
                    rows.Add(row);
                }
                if (rc != 101) Check(rc);
            }
            finally { sqlite3_reset(stmt); }
            return rows;
        }
    }
    public string? Meta(string key) => Query("SELECT value FROM meta WHERE key=?", key).FirstOrDefault()?[0]?.ToString();
    public void Meta(string key, string value) => Exec("INSERT OR REPLACE INTO meta VALUES(?,?)", key, value);
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public void Note(string text) => Exec("INSERT INTO notes VALUES(?,?)", Now, text);
    public void Delta(string path, long delta, string kind, long? when = null)
    {
        if (delta == 0) return;
        long bucket = (when ?? Now) / 300 * 300;
        Exec("INSERT INTO changes VALUES(?,?,?,?) ON CONFLICT(bucket,path,kind) DO UPDATE SET delta=delta+excluded.delta", bucket, path, delta, kind);
    }
    public void Put(string path, long size, long generation, bool record, string kind)
    {
        lock (Gate)
        {
            if (record)
            {
                var old = Query("SELECT size FROM files WHERE path=?", path);
                Delta(path, size - (old.Count == 0 ? 0 : (long)old[0][0]!), kind);
            }
            Exec("INSERT INTO files VALUES(?,?,?) ON CONFLICT(path) DO UPDATE SET size=excluded.size,seen=excluded.seen", path, size, generation);
        }
    }
    public void PutBatch(List<ScanFile> files, long generation, bool record, string kind)
    {
        lock (Gate)
        {
            Exec("BEGIN");
            try
            {
                // Bulk staging removes a SELECT + managed row allocations for every file on reconciliation.
                Exec("CREATE TEMP TABLE IF NOT EXISTS scan_batch(path TEXT PRIMARY KEY,size INTEGER) WITHOUT ROWID");
                Exec("DELETE FROM scan_batch");
                foreach (var f in files) Exec("INSERT OR REPLACE INTO scan_batch VALUES(?,?)", f.Path, f.Size);
                if (record) Exec("INSERT INTO changes SELECT ?,b.path,b.size-coalesce(f.size,0),? FROM scan_batch b LEFT JOIN files f ON b.path=f.path WHERE b.size<>coalesce(f.size,0) ON CONFLICT(bucket,path,kind) DO UPDATE SET delta=delta+excluded.delta", Now / 300 * 300, kind);
                Exec("INSERT INTO files SELECT path,size,? FROM scan_batch WHERE true ON CONFLICT(path) DO UPDATE SET size=excluded.size,seen=excluded.seen", generation);
                Exec("COMMIT");
            }
            catch { Exec("ROLLBACK"); throw; }
        }
    }
    public void ScanCache(bool enabled) => Exec(enabled ? "PRAGMA cache_size=-32768" : "PRAGMA cache_size=-4096");
    public void RemoveTree(string path, bool record, string kind)
    {
        lock (Gate)
        {
            var exact = Query("SELECT size FROM files WHERE path=?", path);
            if (exact.Count > 0) { if (record) Delta(path, -(long)exact[0][0]!, kind); Exec("DELETE FROM files WHERE path=?", path); }
            string prefix = path.TrimEnd('\\') + "\\";
            string cursor = prefix;
            while (true)
            {
                var rows = Query("SELECT path,size FROM files WHERE path>=? AND path<? AND path>? ORDER BY path LIMIT 300", prefix, prefix + '\uffff', cursor);
                if (rows.Count == 0) break;
                foreach (var row in rows)
                { string p = (string)row[0]!; if (record) Delta(p, -(long)row[1]!, kind); Exec("DELETE FROM files WHERE path=?", p); cursor = p; }
            }
        }
    }
    public void RecordManualRemoval(string path, bool record)
    {
        lock (Gate)
        {
            Exec("BEGIN");
            try
            {
                RemoveTree(Monitor.Key(path), record, "Удалено вручную");
                Exec("INSERT OR REPLACE INTO manual_removals VALUES(?,?)", Monitor.Key(path), Now);
                Exec("COMMIT");
            }
            catch { Exec("ROLLBACK"); throw; }
        }
    }
    public HashSet<string> ManualRemovals(string folder)
    {
        string prefix = Monitor.Key(folder) + "\\";
        return Query("SELECT path FROM manual_removals m WHERE path>=? AND path<? AND NOT EXISTS(SELECT 1 FROM files f WHERE f.path=m.path)", prefix, prefix + '\uffff')
            .Select(r => (string)r[0]!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    public void Retain()
    {
        long cutoff = Now - 366L * 86400;
        Exec("DELETE FROM changes WHERE bucket<?", cutoff); Exec("DELETE FROM samples WHERE ts<?", cutoff); Exec("DELETE FROM notes WHERE ts<?", cutoff);
        Exec("DELETE FROM manual_removals WHERE ts<?", cutoff);
        Exec("PRAGMA wal_checkpoint(PASSIVE)");
    }
    public FolderListing FolderChildren(string folder, long from, long to, int offset = 0)
    {
        var all = FolderChildrenAll(folder, from, to);
        return all with { Children = all.Children.Skip(Math.Max(0, offset)).Take(2000).ToList() };
    }
    public FolderListing FolderChildrenAll(string folder, long from, long to)
    {
        string prefix = Monitor.Key(folder) + "\\", upper = prefix + '\uffff', cursor = prefix;
        var children = new Dictionary<string, FolderEntry>(StringComparer.OrdinalIgnoreCase);
        // Seek by the primary-key path index. Sum each subtree once without materializing
        // millions of descendant strings into a GROUP BY temporary table.
        while (true)
        {
            var batch = Query("SELECT path,size FROM files WHERE path>=? AND path<? ORDER BY path LIMIT 2048", cursor, upper);
            if (batch.Count == 0) break;
            foreach (var row in batch)
            {
                string path = (string)row[0]!;
                if (StringComparer.Ordinal.Compare(path, cursor) < 0) continue;
                int slash = path.IndexOf('\\', prefix.Length);
                if (slash < 0)
                {
                    long size = (long)row[1]!;
                    children[path] = new(path, false, size, size, 0);
                    cursor = path + "\0";
                }
                else
                {
                    string child = path[..slash], childPrefix = child + "\\";
                    cursor = childPrefix + '\uffff';
                    long size = (long)Query("SELECT coalesce(sum(size),0) FROM files WHERE path>=? AND path<?", childPrefix, cursor)[0][0]!;
                    children[child] = new(child, true, size, size, 0);
                }
            }
        }
        var changes = Query("""
            SELECT path,sum(CASE WHEN bucket<? THEN delta ELSE 0 END),sum(CASE WHEN bucket>=? THEN delta ELSE 0 END)
            FROM changes WHERE bucket>=? AND path>=? AND path<? AND kind NOT IN ('Системный замер','Теневые копии') GROUP BY path
            """, to, to, from, prefix, upper);
        foreach (var row in changes)
        {
            string path = (string)row[0]!; int slash = path.IndexOf('\\', prefix.Length);
            string child = slash < 0 ? path : path[..slash];
            if (!children.TryGetValue(child, out var e)) e = new(child, slash >= 0, 0, 0, 0) { ExistsNow = false };
            long during = (long)row[1]!, later = (long)row[2]!;
            children[child] = e with { Directory = e.Directory || slash >= 0, Before = e.Before - during - later, After = e.After - later, Delta = e.Delta + during };
        }
        var items = children.Values.OrderByDescending(e => e.Directory).ThenByDescending(e => Math.Abs(e.Delta)).ThenByDescending(e => e.After).ThenBy(e => e.Path, StringComparer.Ordinal).ToList();
        return new(new(Monitor.Key(folder), true, items.Sum(e => e.Before), items.Sum(e => e.After), items.Sum(e => e.Delta)), items, items.Count);
    }
    public FolderComparison ReadComparison(string folder, HistoryPeriod period)
    {
        lock (Gate)
        {
            Exec("BEGIN");
            try
            {
                var listing = FolderChildrenAll(folder, period.From, period.To);
                long step = Math.Max(60, (period.To - period.From) / 1000);
                // Include the end-boundary sample if it exists; never borrow samples outside the tree interval.
                var samples = Query("SELECT ts,free,page,vss,ram,ramtotal,total FROM samples WHERE ts IN (SELECT min(ts) FROM samples WHERE ts>=? AND ts<=? GROUP BY (ts-?)/?) OR ts=(SELECT max(ts) FROM samples WHERE ts>=? AND ts<=?) ORDER BY ts",
                    period.From, period.To, period.From, step, period.From, period.To);
                var removed = ManualRemovals(folder);
                Exec("COMMIT"); return new(period, listing, samples) { RemovedNow = removed };
            }
            catch { Exec("ROLLBACK"); throw; }
        }
    }
    public void SaveSample(Sample s, string root)
    {
        lock (Gate)
        {
            Exec("BEGIN");
            try
            {
                var page = Query("SELECT page FROM samples WHERE page>=0 ORDER BY ts DESC LIMIT 1").FirstOrDefault();
                if (page != null && s.Page >= 0) Delta(Path.Combine(Path.GetPathRoot(root)!, "pagefile.sys"), s.Page - (long)page[0]!, "Системный замер", s.Ts);
                if (s.Vss.HasValue)
                {
                    var vss = Query("SELECT vss FROM samples WHERE vss IS NOT NULL ORDER BY ts DESC LIMIT 1").FirstOrDefault();
                    if (vss != null) Delta(Path.Combine(Path.GetPathRoot(root)!, "System Volume Information"), s.Vss.Value - (long)vss[0]!, "Теневые копии", s.Ts);
                }
                Exec("INSERT OR REPLACE INTO samples VALUES(?,?,?,?,?,?,?)", s.Ts, s.Free, s.Total, s.Ram, s.RamTotal, s.Page, s.Vss);
                Exec("COMMIT");
            }
            catch { Exec("ROLLBACK"); throw; }
        }
    }
    public void Dispose() { lock (Gate) { foreach (var stmt in statements.Values) sqlite3_finalize(stmt); statements.Clear(); sqlite3_close(db); } }
}


