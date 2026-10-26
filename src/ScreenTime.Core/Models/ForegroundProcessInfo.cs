namespace ScreenTime.Core.Models;

/// <summary>
/// Technische Momentaufnahme des aktuellen Vordergrundprozesses.
/// Der Fenstertitel wird ausschließlich flüchtig im Arbeitsspeicher für den Abgleich
/// mit vom Benutzer eingetragenen Webseiten-Links genutzt und niemals in SQLite gespeichert.
/// </summary>
public sealed class ForegroundProcessInfo
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string? ExecutablePath { get; init; }

    public string? PackageFullName { get; init; }

    public nint WindowHandle { get; init; }

    /// <summary>
    /// Flüchtiger Fenstertitel im RAM (wird nicht persistiert), um benutzerdefinierte Webseiten im Browser zu erkennen.
    /// </summary>
    public string? TransientWindowTitle { get; init; }

    /// <summary>
    /// Gibt an, ob das Vordergrundfenster aktuell im Vollbildmodus läuft (z. B. Video-Stream oder Präsentation).
    /// </summary>
    public bool IsFullscreen { get; init; }

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
