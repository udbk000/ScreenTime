using ScreenTime.Core.Abstractions;

namespace ScreenTime.Core.Services;

/// <summary>
/// Datenschutzfreundlicher lokaler Logger mit Ringpuffer für die Diagnoseansicht.
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private readonly LinkedList<string> _recentEntries = new();
    private const int MaxMemoryEntries = 250;

    public FileAppLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, "screentime.log");
    }

    public void Info(string message) => WriteEntry("INFO", message);

    public void Warn(string message) => WriteEntry("WARN", message);

    public void Error(string message, Exception? ex = null)
    {
        string detail = ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
        WriteEntry("ERROR", detail);
    }

    public IReadOnlyList<string> GetRecentEntries(int maxCount = 100)
    {
        lock (_lock)
        {
            return _recentEntries.TakeLast(maxCount).Reverse().ToList();
        }
    }

    private void WriteEntry(string level, string message)
    {
        string line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            _recentEntries.AddLast(line);
            while (_recentEntries.Count > MaxMemoryEntries)
            {
                _recentEntries.RemoveFirst();
            }

            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Logging-Fehler dürfen den Tracker niemals unterbrechen
            }
        }
    }
}
