namespace ScreenTime.Core.Models;

/// <summary>
/// Repräsentiert eine erkannte Desktop-Anwendung oder eine vom Benutzer eingetragene Webseite (Link)
/// mit verständlichem Anzeigenamen, Icon-Cache-Verweis, Kategoriezuordnung und optionalem eigenem Idle-Schwellwert.
/// </summary>
public sealed class TrackedApplication
{
    public long Id { get; set; }

    /// <summary>
    /// Stabiler Identifikator (z. B. normalisierter Prozessschlüssel oder "web:youtube.com").
    /// </summary>
    public string AppIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Verständlicher Anzeigename (z. B. "Google Chrome" oder "YouTube").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Technischer Prozessname ohne Pfad (z. B. "chrome") oder Webseiten-Kennzeichnung ("Webseite").
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Optionaler vollständiger Pfad zur ausführbaren Datei oder bei Webseiten der hinterlegte Link/Domain.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// Pfad zur lokal zwischengespeicherten PNG-Icondatei.
    /// </summary>
    public string? IconCachePath { get; set; }

    /// <summary>
    /// Zugeordnete Kategorie-ID.
    /// </summary>
    public long CategoryId { get; set; }

    /// <summary>
    /// Optionaler App-spezifischer Idle-Schwellwert in Sekunden.
    /// Wenn null, gilt der Schwellwert der Kategorie bzw. der globale Standardwert.
    /// </summary>
    public int? CustomIdleThresholdSeconds { get; set; }

    /// <summary>
    /// Gibt an, ob der Nutzer den Eintrag manuell angelegt oder angepasst hat.
    /// </summary>
    public bool IsUserCustomized { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gibt an, ob es sich um einen benutzerdefinierten Webseiten-Eintrag (Link) handelt.
    /// </summary>
    public bool IsWebsite => AppIdentifier.StartsWith("web:", StringComparison.OrdinalIgnoreCase);
}
