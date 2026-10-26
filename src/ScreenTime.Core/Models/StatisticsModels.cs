namespace ScreenTime.Core.Models;

/// <summary>
/// Nutzungszusammenfassung einer einzelnen App in einem gewählten Zeitraum.
/// </summary>
public sealed class AppUsageSummary
{
    public long ApplicationId { get; init; }
    public string AppIdentifier { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public string? IconCachePath { get; init; }
    public long CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string CategoryColorHex { get; init; } = "#007AFF";
    public string CategoryIconGlyph { get; init; } = "\uE71D";
    public int EffectiveIdleThresholdSeconds { get; init; }
    public int? CustomIdleThresholdSeconds { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public double PercentageOfTotal { get; init; }
    public int SessionCount { get; init; }

    public string FormattedDuration => FormatDuration(TotalDuration);
    public string FormattedPercentage => $"{PercentageOfTotal:0.#} %";

    public static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 60)
        {
            int secs = Math.Max(0, (int)Math.Round(span.TotalSeconds));
            return $"{secs} Sek.";
        }
        int totalMinutes = (int)Math.Floor(span.TotalMinutes);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        if (hours > 0)
        {
            return minutes > 0 ? $"{hours} Std. {minutes} Min." : $"{hours} Std.";
        }
        return $"{minutes} Min.";
    }
}

/// <summary>
/// Nutzungszusammenfassung einer Kategorie inkl. der darin enthaltenen Apps.
/// </summary>
public sealed class CategoryUsageSummary
{
    public long CategoryId { get; init; }
    public string CategoryKey { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#007AFF";
    public string IconGlyph { get; init; } = "\uE71D";
    public int DefaultIdleThresholdSeconds { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public double PercentageOfTotal { get; init; }
    public IReadOnlyList<AppUsageSummary> Apps { get; init; } = Array.Empty<AppUsageSummary>();

    public string FormattedDuration => AppUsageSummary.FormatDuration(TotalDuration);
    public string FormattedPercentage => $"{PercentageOfTotal:0.#} %";
}

/// <summary>
/// Stündlicher Nutzungswert (0..23 Uhr) für ein Tagesdiagramm.
/// </summary>
public sealed class HourlyUsageBucket
{
    public int Hour { get; init; }
    public string Label => $"{Hour:00}:00";
    public TimeSpan Duration { get; init; }
    public double Minutes => Duration.TotalMinutes;
}

/// <summary>
/// Tageswert für Verlaufsdiagramme (7 Tage, 30 Tage, 6 Wochen).
/// </summary>
public sealed class DailyUsagePoint
{
    public DateOnly Date { get; init; }
    public string ShortDayLabel { get; init; } = string.Empty;
    public string FullDateLabel { get; init; } = string.Empty;
    public TimeSpan TotalDuration { get; init; }
    public bool IsBeforeFirstRecordedDay { get; init; }
    public IReadOnlyList<CategoryUsageSummary> TopCategories { get; init; } = Array.Empty<CategoryUsageSummary>();

    public string FormattedDuration => AppUsageSummary.FormatDuration(TotalDuration);
}

/// <summary>
/// Vollständige Auswertung für einen einzelnen Tag.
/// </summary>
public sealed class DayDetailStatistics
{
    public DateOnly Date { get; init; }
    public string DateTitle { get; init; } = string.Empty;
    public TimeSpan TotalDuration { get; init; }
    public TimeSpan SixWeekAverageDailyDuration { get; init; }
    public double DeltaPercentVsSixWeekAverage { get; init; }
    public string ComparisonDescription { get; init; } = string.Empty;
    public IReadOnlyList<HourlyUsageBucket> HourlyBreakdown { get; init; } = Array.Empty<HourlyUsageBucket>();
    public IReadOnlyList<CategoryUsageSummary> Categories { get; init; } = Array.Empty<CategoryUsageSummary>();
    public IReadOnlyList<AppUsageSummary> Apps { get; init; } = Array.Empty<AppUsageSummary>();

    public string FormattedTotalDuration => AppUsageSummary.FormatDuration(TotalDuration);
    public string FormattedSixWeekAverage => AppUsageSummary.FormatDuration(SixWeekAverageDailyDuration);
}

/// <summary>
/// Auswertung für einen mehrtägigen Zeitraum (z. B. 7 Tage, 30 Tage) und den 6-Wochen-Durchschnitt.
/// </summary>
public sealed class PeriodStatistics
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string PeriodTitle { get; init; } = string.Empty;
    public TimeSpan TotalDuration { get; init; }
    public TimeSpan AverageDailyDuration { get; init; }
    public TimeSpan SixWeekAverageDailyDuration { get; init; }
    public int EvaluatedDaysForAverage { get; init; }
    public int SixWeekEvaluatedDays { get; init; }
    public DateOnly? FirstRecordedDate { get; init; }
    public string SixWeekExplanation { get; init; } = string.Empty;
    public IReadOnlyList<DailyUsagePoint> DailyPoints { get; init; } = Array.Empty<DailyUsagePoint>();
    public IReadOnlyList<CategoryUsageSummary> Categories { get; init; } = Array.Empty<CategoryUsageSummary>();
    public IReadOnlyList<AppUsageSummary> Apps { get; init; } = Array.Empty<AppUsageSummary>();

    public string FormattedTotalDuration => AppUsageSummary.FormatDuration(TotalDuration);
    public string FormattedAverageDailyDuration => AppUsageSummary.FormatDuration(AverageDailyDuration);
    public string FormattedSixWeekAverage => AppUsageSummary.FormatDuration(SixWeekAverageDailyDuration);
}

/// <summary>
/// Detailauswertung für eine einzelne Anwendung (Heute, 7 Tage, 30 Tage).
/// </summary>
public sealed class AppDetailStatistics
{
    public TrackedApplication Application { get; init; } = new();
    public AppCategory Category { get; init; } = new();
    public int EffectiveIdleThresholdSeconds { get; init; }
    public TimeSpan TodayDuration { get; init; }
    public TimeSpan Last7DaysTotalDuration { get; init; }
    public TimeSpan Last7DaysDailyAverage { get; init; }
    public TimeSpan Last30DaysTotalDuration { get; init; }
    public TimeSpan Last30DaysDailyAverage { get; init; }
    public IReadOnlyList<DailyUsagePoint> Last7DaysPoints { get; init; } = Array.Empty<DailyUsagePoint>();
    public IReadOnlyList<DailyUsagePoint> Last30DaysPoints { get; init; } = Array.Empty<DailyUsagePoint>();

    public string FormattedTodayDuration => AppUsageSummary.FormatDuration(TodayDuration);
    public string Formatted7DaysTotal => AppUsageSummary.FormatDuration(Last7DaysTotalDuration);
    public string Formatted30DaysTotal => AppUsageSummary.FormatDuration(Last30DaysTotalDuration);
}

/// <summary>
/// Aktueller Live-Zustand des Trackers für Dashboard &amp; Diagnoseansicht.
/// </summary>
public sealed class LiveTrackingStatus
{
    public bool IsTrackingActive { get; init; }
    public bool IsUserIdle { get; init; }
    public bool IsMediaOrStreamingActive { get; init; }
    public bool IsSessionLockedOrSuspended { get; init; }
    public string CurrentAppDisplayName { get; init; } = "Keine aktive App";
    public string CurrentProcessName { get; init; } = "-";
    public string CurrentCategoryName { get; init; } = "-";
    public TimeSpan TimeSinceLastInput { get; init; }
    public int ActiveIdleThresholdSeconds { get; init; }
    public TimeSpan CurrentSessionDuration { get; init; }
    public DateTimeOffset LastUpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
