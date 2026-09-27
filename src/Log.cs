using System.Diagnostics;

namespace LightsaberCursor;

/// Minimal diagnostics log at %APPDATA%\LightsaberCursor\log.txt (kept small; enabled with --log).
internal static class Log
{
    public static bool Enabled { get; set; }
    static readonly string FilePath = Path.Combine(AppSettings.Folder, "log.txt");
    static readonly Dictionary<string, (double Total, double Max, int Count)> timings = new();
    static readonly Stopwatch since = Stopwatch.StartNew();

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { }
    }

    /// Accumulates a timing sample and flushes a summary line every 2 seconds.
    public static void Time(string name, double ms)
    {
        if (!Enabled) return;
        timings.TryGetValue(name, out var t);
        timings[name] = (t.Total + ms, Math.Max(t.Max, ms), t.Count + 1);
        if (since.Elapsed.TotalSeconds < 2) return;
        since.Restart();
        Write(string.Join("  ", timings.Select(kv => $"{kv.Key}: n={kv.Value.Count} avg={kv.Value.Total / kv.Value.Count:F1}ms max={kv.Value.Max:F1}ms")));
        timings.Clear();
    }
}
