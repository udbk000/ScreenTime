using ScreenTime.Core.Models;

namespace ScreenTime.Core.Abstractions;

/// <summary>
/// Ermittelt das aktuelle Vordergrundfenster und die zugehörigen Prozessinformationen.
/// </summary>
public interface IForegroundAppProvider
{
    ForegroundProcessInfo? GetCurrentForegroundProcess();
}

/// <summary>
/// Erkennt Inaktivität anhand der letzten Maus-/Tastatureingabe sowie aktiver Audio-/Video-Wiedergabe.
/// </summary>
public interface IIdleDetector
{
    /// <summary>
    /// Gibt die vergangene Zeit seit der letzten Maus- oder Tastatureingabe zurück.
    /// </summary>
    TimeSpan GetIdleDuration();

    /// <summary>
    /// Prüft, ob aktuell Audio-/Medienwiedergabe oder ein aktiver Stream stattfindet,
    /// sodass auch ohne Maus-/Tastatureingabe aktive Bildschirmzeit gezählt wird.
    /// </summary>
    bool IsMediaOrStreamingActive();

    /// <summary>
    /// Prüft, ob die Inaktivitätsdauer den für die aktuelle App/Kategorie gültigen Schwellwert überschreitet.
    /// </summary>
    bool IsIdle(int thresholdSeconds, out TimeSpan currentIdleDuration);
}

/// <summary>
/// Bestimmt den verständlichen Anzeigenamen, den stabilen App-Identifikator, die Initialkategorie und das Icon einer App.
/// </summary>
public interface IApplicationResolver
{
    TrackedApplication Resolve(ForegroundProcessInfo processInfo, IReadOnlyList<AppCategory> categories, IReadOnlyList<TrackedApplication>? existingApps = null);
    string? EnsureIconCached(string appIdentifier, string? executablePath);
}

/// <summary>
/// Überwacht Windows-Sperrbildschirm, Abmeldung und Energie-/Standby-Zustände.
/// </summary>
public interface ISystemPowerMonitor : IDisposable
{
    bool IsSessionLockedOrSuspended { get; }
    event EventHandler<InterruptionReason>? SessionInterrupted;
    event EventHandler? SessionResumed;
}

/// <summary>
/// Lokales, datenschutzfreundliches Diagnose- und Fehlerprotokoll.
/// </summary>
public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    IReadOnlyList<string> GetRecentEntries(int maxCount = 100);
}
