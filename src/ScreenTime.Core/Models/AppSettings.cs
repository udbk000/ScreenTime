namespace ScreenTime.Core.Models;

/// <summary>
/// Farbschema-Einstellung für die App.
/// </summary>
public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// Persistente lokale Einstellungen der Anwendung.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Globaler Fallback-Idle-Schwellwert in Sekunden (Startwert lt. Doku: 60s, sofern Kategorie nichts vorgibt).
    /// </summary>
    public int GlobalIdleThresholdSeconds { get; set; } = 60;

    /// <summary>
    /// Ausgewähltes Farbschema (Systemstandard, Hell, Dunkel).
    /// </summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// Ob die App beim Windows-Start automatisch gestartet werden soll.
    /// </summary>
    public bool LaunchAtStartup { get; set; } = false;

    /// <summary>
    /// Ob das Schließen des Hauptfensters die App in den System-Tray minimiert statt sie zu beenden.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>
    /// Aufbewahrungsdauer lokaler Detaildaten in Tagen (0 = unbegrenzt).
    /// </summary>
    public int DataRetentionDays { get; set; } = 365;

    /// <summary>
    /// Polling-Intervall in Millisekunden (Standard: 1000 ms).
    /// </summary>
    public int PollingIntervalMs { get; set; } = 1000;
}
