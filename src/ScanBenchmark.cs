using System.Diagnostics;
using System.Text.Json;

namespace Kroshka;
internal static class ScanBenchmark
{
    public static int Run(string data, string root, bool legacy)
    {
        try
        {
            using var m = new Monitor(data, new Settings { Root = root });
            var result = m.Benchmark(legacy);
            File.WriteAllText(Path.Combine(data, "benchmark.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(data, "benchmark-error.txt"), ex.ToString()); return 1; }
    }
}
