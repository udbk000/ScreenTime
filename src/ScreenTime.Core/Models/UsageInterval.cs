namespace ScreenTime.Core.Models;

/// <summary>
/// Grund für das Beenden oder Segmentieren eines aktiven Nutzungsintervalls.
/// </summary>
public enum InterruptionReason
{
    Active = 0,
    AppSwitch = 1,
    IdleTimeout = 2,
    SessionLock = 3,
    SystemSuspend = 4,
    ProcessExited = 5,
    AppShutdown = 6,
    RecoveredAfterRestart = 7,
    MidnightRollover = 8
}

/// <summary>
/// Repräsentiert ein zusammenhängendes aktives Nutzungsintervall einer Anwendung.
/// </summary>
public sealed class UsageInterval
{
    public long Id { get; set; }

    public long ApplicationId { get; set; }

    public DateTimeOffset StartTimeUtc { get; set; }

    public DateTimeOffset EndTimeUtc { get; set; }

    /// <summary>
    /// Berechnete aktive Dauer in Sekunden.
    /// </summary>
    public int DurationSeconds { get; set; }

    public InterruptionReason EndReason { get; set; } = InterruptionReason.Active;
}
