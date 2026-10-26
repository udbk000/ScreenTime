using ScreenTime.Core.Models;

namespace ScreenTime.Core.Abstractions;

/// <summary>
/// Speichert und liest Nutzungsdaten, Kategorien, Apps und Einstellungen aus der lokalen SQLite-Datenbank.
/// </summary>
public interface IUsageRepository : IDisposable
{
    void Initialize();

    // Kategorien
    IReadOnlyList<AppCategory> GetCategories();
    AppCategory? GetCategoryById(long id);
    AppCategory CreateCategory(string name, string colorHex, string iconGlyph, int defaultIdleSeconds);
    void UpdateCategory(AppCategory category);
    void DeleteCategory(long categoryId);

    // Anwendungen & Webseiten
    TrackedApplication? GetApplicationByIdentifier(string appIdentifier);
    TrackedApplication? GetApplicationById(long id);
    IReadOnlyList<TrackedApplication> GetAllApplications();
    TrackedApplication UpsertApplication(TrackedApplication app);
    void UpdateApplicationCustomization(long appId, string displayName, long categoryId, int? customIdleThresholdSeconds);
    TrackedApplication AddOrUpdateProgramEntry(string displayName, string executableOrProcessName, long categoryId, int? customIdleSeconds, string? iconCachePath);
    TrackedApplication AddOrUpdateWebsiteEntry(string displayName, string websiteUrlOrDomain, long categoryId, int? customIdleSeconds);

    // Nutzungsintervalle
    long InsertUsageInterval(UsageInterval interval);
    void UpdateUsageInterval(UsageInterval interval);
    IReadOnlyList<UsageInterval> GetIntervalsInRange(DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc);
    IReadOnlyList<UsageInterval> GetIntervalsForAppInRange(long applicationId, DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc);
    DateTimeOffset? GetFirstRecordedTimestampUtc();
    void RecoverUnclosedIntervals();
    void PurgeOldIntervals(int retentionDays);
    void ClearAllUsageData();

    // Einstellungen
    AppSettings GetSettings();
    void SaveSettings(AppSettings settings);
}

/// <summary>
/// Berechnet Tages-, Wochen-, Monats- und 6-Wochen-Durchschnittswerte sowie Kategorie- und App-Statistiken.
/// </summary>
public interface IStatisticsService
{
    DayDetailStatistics GetDayStatistics(DateOnly date);
    PeriodStatistics GetLast7DaysStatistics(DateOnly? referenceToday = null);
    PeriodStatistics GetLast30DaysStatistics(DateOnly? referenceToday = null);
    PeriodStatistics GetSixWeekAverageStatistics(DateOnly? referenceToday = null);
    AppDetailStatistics? GetAppDetailStatistics(long applicationId, DateOnly? referenceToday = null);
    int GetEffectiveIdleThresholdSeconds(TrackedApplication? app, AppCategory? category, AppSettings settings);
}

/// <summary>
/// Koordiniert Messung, Zeitintervalle, Idle-Erkennung und Persistenz.
/// </summary>
public interface ITrackingService : IDisposable
{
    void Start();
    void Stop();
    void ProcessTick(DateTimeOffset nowUtc);
    LiveTrackingStatus GetLiveStatus();
    event EventHandler<LiveTrackingStatus>? StatusUpdated;
}
