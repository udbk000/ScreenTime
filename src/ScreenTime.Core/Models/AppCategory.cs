namespace ScreenTime.Core.Models;

/// <summary>
/// Repräsentiert eine App-Kategorie (Systemkategorie oder benutzerdefinierte Kategorie)
/// inklusive eigenem Standard-Idle-Schwellwert.
/// </summary>
public sealed class AppCategory
{
    public long Id { get; set; }

    /// <summary>
    /// Eindeutiger technischer Schlüssel (z. B. "learning", "custom_123").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Anzeigename in der Benutzeroberfläche.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Hex-Farbcode im Apple-ScreenTime-Stil (z. B. "#007AFF").
    /// </summary>
    public string ColorHex { get; set; } = "#007AFF";

    /// <summary>
    /// Segoe Fluent Icons Glyph für die Kategorie.
    /// </summary>
    public string IconGlyph { get; set; } = "\uE71D";

    /// <summary>
    /// Standard-Idle-Schwellwert in Sekunden für alle Apps/Webseiten in dieser Kategorie,
    /// sofern kein eigener benutzerdefinierter Schwellwert definiert ist.
    /// </summary>
    public int DefaultIdleThresholdSeconds { get; set; } = 120;

    /// <summary>
    /// Sortierreihenfolge in Übersichten.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Gibt an, ob diese Kategorie vom Benutzer selbst erstellt wurde.
    /// </summary>
    public bool IsCustom => Key.StartsWith("custom_", StringComparison.OrdinalIgnoreCase);
}
