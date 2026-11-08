using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kroshka;

internal sealed class Settings
{
    public string Root { get; set; } = @"C:\";
    public bool Monitoring { get; set; }
    public bool Paused { get; set; }
    public string Appearance { get; set; } = "Classic";
    public bool GraphCollapsed { get; set; }
    public string Data { get; set; } = "";
    public List<string> ExtraDrives { get; set; } = new();
    public string SelectedDrive { get; set; } = "C:\\";
    public static Settings Load(string dir)
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(dir, "settings.json"))) ?? new(); }
        catch { return new(); }
    }
    public void Save(string dir)
    {
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
}
internal sealed record Sample(long Ts, long Free, long Total, long Ram, long RamTotal, long Page, long? Vss);
internal static class SystemMeter
{
    [StructLayout(LayoutKind.Sequential)] struct Memory
    { public uint Length, Load; public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
    [StructLayout(LayoutKind.Sequential)] struct PageInfo { public uint Size, Reserved; public UIntPtr Total, Used, Peak; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref Memory m);
    delegate bool PageCallback(IntPtr ctx, ref PageInfo info, [MarshalAs(UnmanagedType.LPWStr)] string name);
    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EnumPageFilesW(PageCallback cb, IntPtr ctx);
    public static Sample Read(string root)
    {
        var d = new DriveInfo(Path.GetPathRoot(root)!); var m = new Memory { Length = (uint)Marshal.SizeOf<Memory>() };
        GlobalMemoryStatusEx(ref m); long pages = 0;
        PageCallback cb = (IntPtr ctx, ref PageInfo p, string name) =>
        { if (name.Contains(d.Name, StringComparison.OrdinalIgnoreCase)) pages += checked((long)p.Total.ToUInt64() * Environment.SystemPageSize); return true; };
        bool ok = EnumPageFilesW(cb, IntPtr.Zero); GC.KeepAlive(cb);
        return new(Store.Now, d.AvailableFreeSpace, d.TotalSize, (long)(m.Total - m.Available), (long)m.Total, ok ? pages : -1, null);
    }
    public static long? Vss(string root)
    {
        object? locator = null, svc = null, volumes = null, shadows = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            svc = ((dynamic)locator!).ConnectServer(".", "root\\cimv2");
            string drive = Path.GetPathRoot(root)!.TrimEnd('\\');
            volumes = ((dynamic)svc).ExecQuery("SELECT DeviceID FROM Win32_Volume WHERE DriveLetter='" + drive + "'");
            string? volume = null;
            foreach (dynamic v in (System.Collections.IEnumerable)volumes!) { volume = (string)v.DeviceID; Marshal.ReleaseComObject(v); }
            if (volume == null) return null;
            shadows = ((dynamic)svc).ExecQuery("SELECT * FROM Win32_ShadowStorage"); long sum = 0;
            foreach (dynamic shadow in (System.Collections.IEnumerable)shadows)
            {
                string refPath = (string)shadow.DiffVolume;
                if (refPath.Replace("\\\\", "\\").Contains(volume, StringComparison.OrdinalIgnoreCase)) sum += Convert.ToInt64(shadow.AllocatedSpace);
                Marshal.ReleaseComObject(shadow);
            }
            return sum;
        }
        catch { return null; }
        finally { foreach (var x in new[] { shadows, volumes, svc, locator }) if (x != null && Marshal.IsComObject(x)) Marshal.ReleaseComObject(x); }
    }
}

internal sealed class Monitor : IDisposable
{
    public Store Db { get; }
    public Settings Config { get; }
    public string DataDir { get; }
    public volatile bool Scanning, Paused, Stop;
    public volatile string Status = "Готов к наблюдению";
    public long Scanned;
    public long ScanEstimate;
    public volatile string ScanPhase = "Подготовка";
    public int Skipped;
    public Sample? Latest;
    public volatile bool NeedsReconcile;
    public volatile bool AccessLimited;
    volatile string? gapReason;
    string? reportedGap;
    bool reportedAccess;
    FileSystemWatcher? watcher;
    readonly object queueGate = new();
    readonly HashSet<string> pending = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> newDirectories = new(StringComparer.OrdinalIgnoreCase);
    readonly AutoResetEvent wake = new(false);
    Thread? worker;
    long generation;
    bool scanRequested, first;
    long lastSample, lastRetention, lastScan, lastVss;
    string? failure;
    public Monitor(string data, Settings config)
    {
        DataDir = Path.GetFullPath(data); Config = config;
        Directory.CreateDirectory(data); Db = new Store(Path.Combine(data, "history.db"));
        first = Db.Meta("baseline") != "done";
        Paused = config.Paused;
        long.TryParse(Db.Meta("last_scan_count"), out ScanEstimate);
        if (Paused) Status = "Пауза · изменения пока не записываются";
        NeedsReconcile = !first;
    }
    public static string Key(string p) => Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant();
    bool Excluded(string p) => p.Equals(DataDir, StringComparison.OrdinalIgnoreCase) || p.StartsWith(DataDir + "\\", StringComparison.OrdinalIgnoreCase)
        || p.Equals(Program.DefaultDataDir, StringComparison.OrdinalIgnoreCase) || p.StartsWith(Program.DefaultDataDir + "\\", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(p).Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)
        || p.Equals(@"C:\pagefile.sys", StringComparison.OrdinalIgnoreCase) || p.Equals(@"C:\swapfile.sys", StringComparison.OrdinalIgnoreCase);
    internal static bool SkipDirectory(FileSystemInfo entry, FileAttributes attr)
    {
        // Do not recall online-only directory listings. Locally present OneDrive folders are safe to enumerate.
        if (((int)attr & (0x400000 | 0x40000 | 0x1000)) != 0) return true;
        if ((attr & FileAttributes.ReparsePoint) == 0) return false;
        try { return entry.LinkTarget != null; } catch { return true; }
    }
    public void Start()
    {
        if (worker != null) return;
        watcher = new FileSystemWatcher(Config.Root) { IncludeSubdirectories = true, InternalBufferSize = 65536,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size };
        watcher.Created += (_, e) => Enqueue(e.FullPath); watcher.Changed += (_, e) => Enqueue(e.FullPath);
        watcher.Deleted += (_, e) => Enqueue(e.FullPath);
        watcher.Renamed += (_, e) => { Enqueue(e.OldFullPath); Enqueue(e.FullPath); };
        watcher.Error += (_, e) => { MarkGap("Поток событий Windows: " + e.GetException().Message); };
        watcher.EnableRaisingEvents = true;
        scanRequested = first; Config.Monitoring = true; Config.Save(DataDir);
        Db.Note(first ? "Начало наблюдения. Первая карта файлов ещё строится; история до установки неизвестна." : "Запуск после перерыва. Полный обход НЕ запускается. Изменения за время выключенной утилиты могут отсутствовать; для восстановления карты доступна ручная сверка.");
        if (!first && !Paused) Status = "Наблюдаю изменения · был перерыв в истории";
        worker = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "Kroshka collector" }; worker.Start();
    }
    public void Enqueue(string path)
    {
        if (Excluded(path)) return;
        lock (queueGate)
        {
            if (pending.Count < 10000) pending.Add(Key(path));
            else MarkGap("Очередь событий достигла 10 000 путей; часть уведомлений пропущена.");
        }
    }
    void MarkGap(string reason) { gapReason = reason; NeedsReconcile = true; }
    public void RequestScan() { scanRequested = true; wake.Set(); }
    public void TogglePause()
    {
        Paused = !Paused;
        Config.Paused = Paused; Config.Save(DataDir);
        if (!Paused && !Scanning) { NeedsReconcile = true; Db.Note("Наблюдение возобновлено после паузы; точное время накопленных изменений неизвестно. Полный обход не запускается."); }
        Status = Paused ? "Пауза · изменения пока не записываются" : Scanning
            ? (first ? "Знакомлюсь с диском · строю первую карту" : "Сверяю карту") : "Возобновление наблюдения";
        wake.Set();
    }
    void Run()
    {
        try
        {
            while (!Stop)
            {
                if (!Paused)
                {
                    if (Store.Now - lastSample >= 60) TakeSample();
                    if (scanRequested)
                    { scanRequested = false; Scan(); }
                    if (!Stop && !Paused) Drain();
                    if (gapReason != null && reportedGap != gapReason) { Db.Note(gapReason); reportedGap = gapReason; }
                    if (AccessLimited && !reportedAccess) { Db.Note("Некоторые изменившиеся файлы недоступны по правам Windows. Они пропускаются; повторный обход не снимает ограничения доступа."); reportedAccess = true; }
                    if (Store.Now - lastRetention > 86400) { Db.Retain(); lastRetention = Store.Now; }
                }
                wake.WaitOne(5000);
            }
        }
        catch (Exception ex) { failure = ex.Message; Status = "Мониторинг остановлен: " + ex.Message; try { Db.Note(Status); } catch { } }
    }
    void TakeSample()
    {
        var sample = SystemMeter.Read(Config.Root);
        if (Store.Now - lastVss > 600)
        { lastVss = Store.Now; sample = sample with { Vss = SystemMeter.Vss(Config.Root) }; }
        Latest = sample;
        Db.SaveSample(sample, Config.Root);
        lastSample = Store.Now;
    }
    // Scanner never opens file contents. It yields regularly and stores the index on disk.
    IEnumerable<FileInfo> Walk(string root)
    {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0 && !Stop)
        {
            string dir = stack.Pop(); if (Excluded(dir)) continue;
            IEnumerator<FileSystemInfo>? en = null;
            try { en = new DirectoryInfo(dir).EnumerateFileSystemInfos().GetEnumerator(); }
            catch { Interlocked.Increment(ref Skipped); }
            if (en == null) continue;
            using (en)
            {
                while (!Stop)
                {
                    FileSystemInfo entry;
                    try { if (!en.MoveNext()) break; entry = en.Current; }
                    catch { Interlocked.Increment(ref Skipped); break; }
                    if (Excluded(entry.FullName)) continue;
                    FileAttributes attr;
                    try { attr = entry.Attributes; } catch { Interlocked.Increment(ref Skipped); continue; }
                    if ((attr & FileAttributes.Directory) != 0)
                    {
                        if (SkipDirectory(entry, attr)) { Interlocked.Increment(ref Skipped); continue; }
                        stack.Push(entry.FullName);
                    }
                    else if (entry is FileInfo file) yield return file;
                }
            }
        }
    }
    void Scan()
    {
        var scanWatch = Stopwatch.StartNew();
        Scanning = true; Scanned = 0; Skipped = 0; NeedsReconcile = false; generation = DateTime.UtcNow.Ticks;
        gapReason = reportedGap = null;
        ScanPhase = "Читаю файлы";
        bool wasFirst = first;
        Status = first ? "Знакомлюсь с диском · строю первую карту" : "Сверяю карту · в том числе изменения за перерыв";
        Db.Note(Status);
        try
        {
            Db.ScanCache(true);
            var batch = new List<ScanFile>(2000);
            foreach (var file in FastWalker.Walk(Config.Root, Excluded, () => Stop, () => Paused, () => Interlocked.Increment(ref Skipped)))
            {
                while (Paused && !Stop) wake.WaitOne(1000);
                if (Stop) return;
                batch.Add(file);
                if (batch.Count >= 2000) { FlushFast(batch, !wasFirst); batch.Clear(); if (Stop) return; }
                if (Store.Now - lastSample >= 60) TakeSample();
            }
            if (batch.Count > 0) FlushFast(batch, !wasFirst);
            if (Stop) return;
            // Only delete stale records if Windows positively reports that the path is absent.
            ScanPhase = "Сверяю удалённые файлы";
            string cursor = "";
            while (!Stop)
            {
                while (Paused && !Stop) wake.WaitOne(1000);
                if (Stop) return;
                var stale = Db.Query("SELECT path FROM files WHERE path>? AND seen<>? ORDER BY path LIMIT 300", cursor, generation);
                if (stale.Count == 0) break;
                foreach (var row in stale)
                {
                    string p = (string)row[0]!; cursor = p;
                    try { File.GetAttributes(p); }
                    catch (FileNotFoundException) { Db.RemoveTree(p, !wasFirst, "Сверка"); }
                    catch (DirectoryNotFoundException) { Db.RemoveTree(p, !wasFirst, "Сверка"); }
                    catch { }
                }
                Thread.Yield();
            }
            if (Stop) return;
            if (wasFirst) Db.Meta("baseline_ts", (Store.Now / 300 * 300).ToString());
            first = false; Db.Meta("baseline", "done"); lastScan = Store.Now;
            Db.Meta("last_scan_count", Scanned.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Db.Meta("last_scan_seconds", scanWatch.Elapsed.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            ScanEstimate = Scanned; ScanPhase = "Готово";
            // An overflow during scanning must stay visible; a further manual scan can repair it.
            Status = NeedsReconcile ? "Есть пропуски событий · нажми «Сверить»" : "Наблюдаю · тихо и локально";
            Db.Note($"Карта готова за {scanWatch.Elapsed.TotalSeconds:N1} с: {Scanned:N0} файлов; пропущено папок/записей: {Skipped}. Дальше — только изменения; полная сверка вручную.");
        }
        finally { Scanning = false; Db.ScanCache(false); }
    }
    void FlushFast(List<ScanFile> batch, bool record)
    { Db.PutBatch(batch, generation, record, "Сверка"); Interlocked.Add(ref Scanned, batch.Count); }
    internal object Benchmark(bool legacy)
    {
        var clock = Stopwatch.StartNew(); using var p = Process.GetCurrentProcess(); var cpu = p.TotalProcessorTime;
        generation = DateTime.UtcNow.Ticks;
        if (legacy)
        {
            var batch = new List<(string Path, long Size)>(200);
            foreach (var file in Walk(Config.Root))
            {
                batch.Add((Key(file.FullName), file.Length));
                if (batch.Count >= 200) { FlushScan(batch, false); batch.Clear(); Thread.Sleep(120); }
            }
            if (batch.Count > 0) FlushScan(batch, false);
        }
        else
        {
            Db.ScanCache(true);
            var batch = new List<ScanFile>(2000);
            foreach (var file in FastWalker.Walk(Config.Root, Excluded, () => Stop, () => Paused, () => Skipped++))
            {
                batch.Add(file); if (batch.Count >= 2000) { FlushFast(batch, false); batch.Clear(); }
            }
            if (batch.Count > 0) FlushFast(batch, false);
        }
        clock.Stop(); p.Refresh();
        return new { Mode = legacy ? "legacy" : "fast", Config.Root, Files = Scanned, Seconds = clock.Elapsed.TotalSeconds,
            FilesPerSecond = Scanned / clock.Elapsed.TotalSeconds, CpuSeconds = (p.TotalProcessorTime - cpu).TotalSeconds,
            PeakWorkingSetMiB = p.PeakWorkingSet64 / 1048576d, LogicalBytes = Db.Query("SELECT sum(size) FROM files")[0][0] };
    }
    void FlushScan(List<(string Path, long Size)> batch, bool record)
    {
        lock (Db.Gate)
        {
            Db.Exec("BEGIN");
            try { foreach (var f in batch) { Db.Put(f.Path, f.Size, generation, record, "Сверка"); Interlocked.Increment(ref Scanned); } Db.Exec("COMMIT"); }
            catch { Db.Exec("ROLLBACK"); throw; }
        }
    }
    void Drain()
    {
        string[] todo;
        lock (queueGate) { todo = pending.Take(400).ToArray(); foreach (var p in todo) pending.Remove(p); }
        lock (Db.Gate)
        {
            Db.Exec("BEGIN");
            try { foreach (string p in todo) Apply(p); Db.Exec("COMMIT"); }
            catch { Db.Exec("ROLLBACK"); throw; }
        }
        foreach (string dir in newDirectories.ToArray())
        {
            newDirectories.Remove(dir);
            var batch = new List<(string Path, long Size)>();
            foreach (var file in Walk(dir))
            {
                if (Stop || Paused) { NeedsReconcile = true; break; }
                if (Store.Now - lastSample >= 60) TakeSample();
                try { batch.Add((Key(file.FullName), file.Length)); } catch { NeedsReconcile = true; }
                if (batch.Count >= 1000) { FlushScan(batch, true); batch.Clear(); Thread.Yield(); }
            }
            if (batch.Count > 0) FlushScan(batch, true);
        }
    }
    public void RecordManualRemovals(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            if (!Key(path).StartsWith(Key(Config.Root) + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (FileRemoval.ConfirmedMissing(path)) Db.RecordManualRemoval(path, !first);
        }
        wake.Set();
    }
    internal void Apply(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            if ((attr & FileAttributes.Directory) != 0)
            {
                // A moved-in directory may arrive as a single notification.
                if (SkipDirectory(new DirectoryInfo(path), attr)) return;
                if (newDirectories.Count < 1024) newDirectories.Add(path); else MarkGap("Очередь новых папок переполнена; часть папок требует сверки.");
                return;
            }
            Db.Put(Key(path), new FileInfo(path).Length, generation, !first, NeedsReconcile ? "Неполная история" : "Наблюдение");
        }
        catch (FileNotFoundException) { Db.RemoveTree(Key(path), !first, "Наблюдение"); }
        catch (DirectoryNotFoundException) { Db.RemoveTree(Key(path), !first, "Наблюдение"); }
        catch (UnauthorizedAccessException) { AccessLimited = true; }
        catch (IOException ex) { MarkGap("Не удалось проверить изменённый путь: " + path + " · " + ex.Message); }
    }
    public string Health => failure != null ? Status : Scanning ? $"{Status} · {Scanned:N0} файлов" : Paused ? Status : NeedsReconcile ? "Есть несверенные изменения · нажми «Сверить»" : Status + (AccessLimited ? " · часть файлов недоступна" : "");
    public void Dispose()
    {
        Stop = true; watcher?.Dispose(); wake.Set();
        if (worker == null || worker.Join(3000)) { Db.Dispose(); wake.Dispose(); }
        // Process exit closes remaining handles; never close a DB under an active scanner.
    }
}
