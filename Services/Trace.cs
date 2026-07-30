using System;
using System.Diagnostics;
using System.IO;

namespace TaskNinja.Services;

/// <summary>
/// Lazy diagnostic logger. Off by default (Enabled=false). When on,
/// writes to %AppData%\TaskNinja\debug.log and System.Diagnostics.Trace.
/// Mirrors the ClipNinja pattern so debugging feels familiar.
/// </summary>
public static class Trace
{
    public static bool Enabled { get; set; } = false;
    private static string? _logPath;
    private static readonly object _lock = new();

    public static void Log(string category, string message)
    {
        if (!Enabled) return;
        try
        {
            if (_logPath is null)
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TaskNinja");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, "debug.log");
            }
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}";
            lock (_lock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            System.Diagnostics.Trace.WriteLine(line);
        }
        catch { /* logging never throws */ }
    }

    /// <summary>Stopwatch helper. Wrap with `using (Trace.Time(...)) { ... }`.</summary>
    public static IDisposable Time(string category, string message) =>
        new TimedScope(category, message);

    private class TimedScope : IDisposable
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly string _category;
        private readonly string _message;
        public TimedScope(string c, string m) { _category = c; _message = m; }
        public void Dispose()
        {
            _sw.Stop();
            Log(_category, $"{_message} took {_sw.ElapsedMilliseconds}ms");
        }
    }
}
