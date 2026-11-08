using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace Kroshka;

internal static class Program
{
    public static readonly string DefaultDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroshka");
    public static string DataDir = DefaultDataDir;
    public static bool Preview;
    public static string Exe => Environment.ProcessPath!;
    [STAThread] static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        int di = Array.IndexOf(args, "--data"); if (di >= 0 && di + 1 < args.Length) DataDir = Path.GetFullPath(args[di + 1]);
        try
        {
            string location = Path.Combine(AppContext.BaseDirectory, "data-location.txt");
            if (di < 0 && File.Exists(location)) DataDir = Path.GetFullPath(File.ReadAllText(location).Trim());
            Directory.CreateDirectory(DataDir);
            DataDir = PhysicalPath.DirectoryName(DataDir);
            if (di < 0 && (!File.Exists(location) || File.ReadAllText(location).Trim() != DataDir))
            {
                File.WriteAllText(location + ".tmp", DataDir);
                File.Move(location + ".tmp", location, true);
            }
        }
        catch (Exception ex) { MessageBox.Show("Не удалось определить папку истории:\n" + ex.Message, "Крошка"); return 1; }
        if (args.Contains("--open-history"))
        {
            try { Process.Start(new ProcessStartInfo(DataDir) { UseShellExecute = true, Verb = "open" }); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(DataDir, "folder-open-error.txt"), ex.ToString()); return 1; }
        }
        if (args.Contains("--self-test")) return Tests.Run(DataDir);
        if (args.Contains("--recycle-self-test")) return Tests.RecycleSmoke(DataDir);
        if (args.Contains("--tree-benchmark"))
        {
            string file = args[Array.IndexOf(args, "--db") + 1];
            using var db = new Store(file, true);
            long from = long.TryParse(db.Meta("baseline_ts"), out long baseline) ? baseline : Store.Now - 86400;
            var result = new List<object>();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string folder in new[] { Path.GetPathRoot(profile)!, Path.GetDirectoryName(profile)!, profile, Path.Combine(profile,"Downloads") })
            {
                var watch = Stopwatch.StartNew(); var listing = db.FolderChildrenAll(folder, from, Store.Now / 300 * 300);
                result.Add(new { folder, seconds = watch.Elapsed.TotalSeconds, children = listing.Count, before = listing.Total.Before, after = listing.Total.After });
            }
            File.WriteAllText(Path.Combine(DataDir, "tree-benchmark.json"), JsonSerializer.Serialize(result)); return 0;
        }
        if (args.Contains("--benchmark"))
        {
            string root = args.SkipWhile(a => a != "--root").Skip(1).FirstOrDefault() ?? throw new ArgumentException("--root is required");
            return ScanBenchmark.Run(DataDir, root, args.Contains("--legacy"));
        }
        if (args.Contains("--probe"))
        {
            try
            {
                string root = args.SkipWhile(a => a != "--root").Skip(1).FirstOrDefault() ?? @"C:\";
                Directory.CreateDirectory(DataDir);
                var s = SystemMeter.Read(root) with { Vss = SystemMeter.Vss(root) };
                File.WriteAllText(Path.Combine(DataDir, "system-probe.json.tmp"), JsonSerializer.Serialize(s));
                File.Move(Path.Combine(DataDir, "system-probe.json.tmp"), Path.Combine(DataDir, "system-probe.json"), true);
                return 0;
            }
            catch { return 1; }
        }
        Preview = args.Contains("--preview");
        string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataDir.ToLowerInvariant())))[..20];
        using var single = new Mutex(true, "Local\\Kroshka-" + identity, out bool owns);
        if (!owns)
        {
            try { using var signal = EventWaitHandle.OpenExisting("Local\\KroshkaShow-" + identity); signal.Set(); }
            catch { MessageBox.Show("Крошка уже запускается. Открой её через значок в трее.", "Крошка"); }
            return 0;
        }
        try
        {
            Directory.CreateDirectory(DataDir);
            var settings = Settings.Load(DataDir);
            Theme.Select(settings.Appearance);
            using var monitor = new Monitor(DataDir, settings);
            if (Preview) Demo.Fill(monitor);
            if (Preview && args.Contains("--preview-scan"))
            {
                monitor.Scanning = true; monitor.Scanned = 111600; monitor.ScanPhase = "Читаю файлы";
                monitor.Config.Monitoring = true;
                if (args.Contains("--preview-estimate")) monitor.ScanEstimate = 240000;
                if (args.Contains("--preview-paused")) monitor.Paused = true;
            }
            var window = new MainForm(monitor);
            int capture = Array.IndexOf(args,"--preview-image");
            if (Preview && capture>=0 && capture+1<args.Length)
                window.Shown += async (_,_) => { await window.CaptureDemo(args[capture+1]); Application.ExitThread(); }; 
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\KroshkaShow-" + identity);
            var registration = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) =>
            { if (window.IsHandleCreated && !window.IsDisposed) { try { window.BeginInvoke(window.OpenWindow); } catch (InvalidOperationException) { } } }, null, -1, false);
            if (settings.Monitoring && !Preview) monitor.Start();
            if (args.Contains("--tray")) window.StartInTray = true;
            Application.Run(window); registration.Unregister(null); return 0;
        }
        catch (Exception ex) { MessageBox.Show("Не удалось запустить Крошку:\n" + ex.Message, "Крошка", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
    public static string Version => typeof(Program).Assembly.GetName().Version!.ToString(3);

}

internal static class Demo
{
    public static void Fill(Monitor m)
    {
        m.Status = "Предпросмотр · вымышленные данные";
        if (m.Db.Meta("demo") == "done") return;
        long start = new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds();
        long gib = 1L << 30;
        for (int minute = 0; minute < 890; minute++)
        
        {
            double i=minute/10.0;
            long free = (long)((42.3 - i * .055 + Math.Sin(i * .14) * .8 + (i > 48 ? 3.1 : 0) - (i > 70 ? 2 : 0)) * gib);
            m.Db.Exec("INSERT INTO samples VALUES(?,?,?,?,?,?,?)", start + minute * 60, free, 465 * gib, 17 * gib, 32 * gib, 16 * gib, i % 10 == 0 ? 9 * gib : null);
        }
        m.Db.Delta(@"c:\users\demo\appdata\local\nvidia\dxcache\shader-cache.bin", 1210L << 20, "Наблюдение", start + 5400);
        m.Db.Delta(@"c:\users\demo\downloads\installer.exe", 843L << 20, "Наблюдение", start + 21600);
        m.Db.Delta(@"c:\users\demo\documents\render\preview.mp4", -(3150L << 20), "Наблюдение", start + 28800);
        m.Db.Delta(@"c:\users\demo\appdata\local\temp\build.tmp", 216L << 20, "Сверка", start + 30000);
        m.Db.Put(@"c:\users\demo\appdata\local\nvidia\dxcache\shader-cache.bin", 1210L << 20, 1, false, "demo");
        m.Db.Put(@"c:\users\demo\downloads\installer.exe", 843L << 20, 1, false, "demo");
        m.Db.Put(@"c:\users\demo\appdata\local\temp\build.tmp", 216L << 20, 1, false, "demo");
        m.Db.Put(@"c:\users\demo\documents\project.blend", 2L << 30, 1, false, "demo");
        m.Db.Put(@"c:\windows\system32\system.dll", 300L << 20, 1, false, "demo");
        m.Db.Meta("baseline", "done"); m.Db.Meta("baseline_ts", start.ToString());
        m.Db.Note("Это демонстрационные данные для проверки интерфейса. Они не смешиваются с реальной историей.");
        m.Db.Meta("demo", "done"); m.Status = "Предпросмотр · вымышленные данные";
        m.Latest = new(Store.Now, (long)(38.7 * gib), 465 * gib, 17 * gib, 32 * gib, 16 * gib, 9 * gib);
    }
}


