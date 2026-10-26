using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;
using ScreenTime.Core.Services;

namespace ScreenTime.App.ViewModels;

public enum HistoryPeriodMode
{
    Today = 0,
    Last7Days = 1,
    Last30Days = 2,
    SixWeeks = 3
}

/// <summary>
/// Zentrales MVVM-ViewModel der Anwendung. Bereitet alle Statistiken, Verläufe,
/// App-Details, Kategorien und Einstellungen für die WinUI-3-Views auf.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public const string AppVersion = "1.1.0";
    public const string GithubHandle = "udbk000";
    public const string GithubUrl = "https://github.com/udbk000";

    private readonly IUsageRepository _repository;
    private readonly IStatisticsService _statisticsService;
    private readonly ITrackingService _trackingService;
    private readonly IApplicationResolver _appResolver;
    private readonly StartupManager _startupManager;
    private readonly IAppLogger _logger;

    private DayDetailStatistics _todayStatistics = new();
    private PeriodStatistics _weekStatistics = new();
    private PeriodStatistics _monthStatistics = new();
    private PeriodStatistics _sixWeekStatistics = new();
    private DayDetailStatistics _selectedDayStatistics = new();
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Now);
    private HistoryPeriodMode _historyMode = HistoryPeriodMode.Last30Days;

    private AppDetailStatistics? _selectedAppDetail;
    private CategoryUsageSummary? _selectedCategoryDetail;
    private string _appSearchQuery = string.Empty;
    private long? _appCategoryFilterId;
    private HistoryPeriodMode _appsAndCategoriesRange = HistoryPeriodMode.Last30Days;

    private LiveTrackingStatus _liveStatus = new();
    private AppSettings _settings = new();
    private IReadOnlyList<AppCategory> _allCategories = Array.Empty<AppCategory>();

    public event EventHandler<ThemePreference>? ThemeChanged;

    public MainViewModel(
        IUsageRepository repository,
        IStatisticsService statisticsService,
        ITrackingService trackingService,
        IApplicationResolver appResolver,
        StartupManager startupManager,
        IAppLogger logger)
    {
        _repository = repository;
        _statisticsService = statisticsService;
        _trackingService = trackingService;
        _appResolver = appResolver;
        _startupManager = startupManager;
        _logger = logger;

        _settings = _repository.GetSettings();
        _allCategories = _repository.GetCategories();
        _liveStatus = _trackingService.GetLiveStatus();
    }

    public DayDetailStatistics TodayStatistics
    {
        get => _todayStatistics;
        private set => SetProperty(ref _todayStatistics, value);
    }

    public PeriodStatistics WeekStatistics
    {
        get => _weekStatistics;
        private set => SetProperty(ref _weekStatistics, value);
    }

    public PeriodStatistics MonthStatistics
    {
        get => _monthStatistics;
        private set => SetProperty(ref _monthStatistics, value);
    }

    public PeriodStatistics SixWeekStatistics
    {
        get => _sixWeekStatistics;
        private set => SetProperty(ref _sixWeekStatistics, value);
    }

    public DayDetailStatistics SelectedDayStatistics
    {
        get => _selectedDayStatistics;
        private set => SetProperty(ref _selectedDayStatistics, value);
    }

    public DateOnly SelectedDate
    {
        get => _selectedDate;
        private set => SetProperty(ref _selectedDate, value);
    }

    public HistoryPeriodMode HistoryMode
    {
        get => _historyMode;
        set
        {
            if (SetProperty(ref _historyMode, value))
            {
                OnPropertyChanged(nameof(ActiveHistoryPeriod));
            }
        }
    }

    public PeriodStatistics ActiveHistoryPeriod => _historyMode switch
    {
        HistoryPeriodMode.Last7Days => _weekStatistics,
        HistoryPeriodMode.SixWeeks => _sixWeekStatistics,
        _ => _monthStatistics
    };

    public HistoryPeriodMode AppsAndCategoriesRange
    {
        get => _appsAndCategoriesRange;
        set
        {
            if (SetProperty(ref _appsAndCategoriesRange, value))
            {
                UpdateSelectedCategoryFromRange();
            }
        }
    }

    public AppDetailStatistics? SelectedAppDetail
    {
        get => _selectedAppDetail;
        private set => SetProperty(ref _selectedAppDetail, value);
    }

    public CategoryUsageSummary? SelectedCategoryDetail
    {
        get => _selectedCategoryDetail;
        private set => SetProperty(ref _selectedCategoryDetail, value);
    }

    public string AppSearchQuery
    {
        get => _appSearchQuery;
        set => SetProperty(ref _appSearchQuery, value ?? string.Empty);
    }

    public long? AppCategoryFilterId
    {
        get => _appCategoryFilterId;
        set => SetProperty(ref _appCategoryFilterId, value);
    }

    public LiveTrackingStatus LiveStatus
    {
        get => _liveStatus;
        set => SetProperty(ref _liveStatus, value);
    }

    public AppSettings Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    public IReadOnlyList<AppCategory> AllCategories
    {
        get => _allCategories;
        private set => SetProperty(ref _allCategories, value);
    }

    public void RefreshAll()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        AllCategories = _repository.GetCategories();
        Settings = _repository.GetSettings();
        LiveStatus = _trackingService.GetLiveStatus();

        TodayStatistics = _statisticsService.GetDayStatistics(today);
        WeekStatistics = _statisticsService.GetLast7DaysStatistics(today);
        MonthStatistics = _statisticsService.GetLast30DaysStatistics(today);
        SixWeekStatistics = _statisticsService.GetSixWeekAverageStatistics(today);

        if (_selectedDate > today)
        {
            _selectedDate = today;
        }
        SelectedDayStatistics = _selectedDate == today
            ? TodayStatistics
            : _statisticsService.GetDayStatistics(_selectedDate);

        if (_selectedAppDetail is not null)
        {
            SelectedAppDetail = _statisticsService.GetAppDetailStatistics(_selectedAppDetail.Application.Id, today);
        }

        UpdateSelectedCategoryFromRange();
    }

    public void SelectDay(DateOnly date)
    {
        SelectedDate = date;
        SelectedDayStatistics = _statisticsService.GetDayStatistics(date);
    }

    public void SelectApp(long applicationId)
    {
        SelectedAppDetail = _statisticsService.GetAppDetailStatistics(applicationId);
    }

    public void SelectCategory(long categoryId)
    {
        var sourceCategories = GetRangeCategories(_appsAndCategoriesRange);
        SelectedCategoryDetail = sourceCategories.FirstOrDefault(c => c.CategoryId == categoryId);
    }

    private void UpdateSelectedCategoryFromRange()
    {
        var sourceCategories = GetRangeCategories(_appsAndCategoriesRange);
        if (_selectedCategoryDetail is not null)
        {
            SelectedCategoryDetail = sourceCategories.FirstOrDefault(c => c.CategoryId == _selectedCategoryDetail.CategoryId)
                                  ?? sourceCategories.FirstOrDefault();
        }
        else
        {
            SelectedCategoryDetail = sourceCategories.FirstOrDefault();
        }
    }

    public IReadOnlyList<CategoryUsageSummary> GetRangeCategories(HistoryPeriodMode range) => range switch
    {
        HistoryPeriodMode.Today => _todayStatistics.Categories,
        HistoryPeriodMode.Last7Days => _weekStatistics.Categories,
        HistoryPeriodMode.SixWeeks => _sixWeekStatistics.Categories,
        _ => _monthStatistics.Categories
    };

    public IReadOnlyList<AppUsageSummary> GetFilteredApps(HistoryPeriodMode range)
    {
        IReadOnlyList<AppUsageSummary> source = range switch
        {
            HistoryPeriodMode.Today => _todayStatistics.Apps,
            HistoryPeriodMode.Last7Days => _weekStatistics.Apps,
            HistoryPeriodMode.SixWeeks => _sixWeekStatistics.Apps,
            _ => _monthStatistics.Apps
        };

        // Ergänze auch bekannte Apps mit 0 Sek. im gewählten Zeitraum, falls nach allen Apps gesucht wird
        var result = source.ToList();
        var existingIds = result.Select(a => a.ApplicationId).ToHashSet();
        var allTrackedApps = _repository.GetAllApplications();
        var catDict = _allCategories.ToDictionary(c => c.Id);
        var defaultCat = _allCategories.LastOrDefault() ?? new AppCategory { Id = 1, Name = "Sonstiges", ColorHex = "#8E8E93" };

        foreach (var tracked in allTrackedApps)
        {
            if (!existingIds.Contains(tracked.Id))
            {
                var cat = catDict.GetValueOrDefault(tracked.CategoryId, defaultCat);
                result.Add(new AppUsageSummary
                {
                    ApplicationId = tracked.Id,
                    AppIdentifier = tracked.AppIdentifier,
                    DisplayName = tracked.DisplayName,
                    ProcessName = tracked.ProcessName,
                    ExecutablePath = tracked.ExecutablePath,
                    IconCachePath = tracked.IconCachePath,
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    CategoryColorHex = cat.ColorHex,
                    CategoryIconGlyph = cat.IconGlyph,
                    EffectiveIdleThresholdSeconds = _statisticsService.GetEffectiveIdleThresholdSeconds(tracked, cat, _settings),
                    CustomIdleThresholdSeconds = tracked.CustomIdleThresholdSeconds,
                    TotalDuration = TimeSpan.Zero,
                    PercentageOfTotal = 0,
                    SessionCount = 0
                });
            }
        }

        IEnumerable<AppUsageSummary> query = result;

        if (_appCategoryFilterId.HasValue && _appCategoryFilterId.Value > 0)
        {
            query = query.Where(a => a.CategoryId == _appCategoryFilterId.Value);
        }

        if (!string.IsNullOrWhiteSpace(_appSearchQuery))
        {
            string q = _appSearchQuery.Trim();
            query = query.Where(a =>
                a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.CategoryName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderByDescending(a => a.TotalDuration)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SaveAppCustomization(long appId, string displayName, long categoryId, int? customIdleSeconds)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }
        _repository.UpdateApplicationCustomization(appId, displayName, categoryId, customIdleSeconds);
        _logger.Info($"App-Einstellungen gespeichert für '{displayName}' (Kategorie-ID: {categoryId}, Eigenes Idle-Limit: {(customIdleSeconds.HasValue ? $"{customIdleSeconds}s" : "Kategorie-Standard")}).");
        RefreshAll();
    }

    public void UpdateCategoryIdleThreshold(long categoryId, int thresholdSeconds)
    {
        var cat = _repository.GetCategoryById(categoryId);
        if (cat is null)
        {
            return;
        }
        cat.DefaultIdleThresholdSeconds = Math.Clamp(thresholdSeconds, 15, 7200);
        _repository.UpdateCategory(cat);
        _logger.Info($"Kategorie-Schwellwert für '{cat.Name}' auf {cat.DefaultIdleThresholdSeconds}s ({cat.DefaultIdleThresholdSeconds / 60.0:0.#} Min.) aktualisiert.");
        RefreshAll();
    }

    public AppCategory CreateCustomCategory(string name, string colorHex, string iconGlyph, int defaultIdleSeconds)
    {
        var created = _repository.CreateCategory(name, colorHex, iconGlyph, defaultIdleSeconds);
        _logger.Info($"Neue benutzerdefinierte Kategorie '{created.Name}' erstellt (Idle-Limit: {created.DefaultIdleThresholdSeconds}s).");
        RefreshAll();
        SelectCategory(created.Id);
        return created;
    }

    public void DeleteCustomCategory(long categoryId)
    {
        _repository.DeleteCategory(categoryId);
        _logger.Info($"Benutzerdefinierte Kategorie (ID {categoryId}) gelöscht.");
        _selectedCategoryDetail = null;
        RefreshAll();
    }

    public TrackedApplication AddProgramToCategory(long categoryId, string displayName, string executableOrProcessName, int? customIdleSeconds = null)
    {
        string raw = executableOrProcessName.Trim().Trim('"');
        string procName = Path.GetFileNameWithoutExtension(raw);
        if (string.IsNullOrWhiteSpace(procName))
        {
            procName = raw;
        }

        string appId = ApplicationResolver.GetCanonicalAppIdentifier(procName);
        string? iconPath = (raw.Contains('\\') || raw.Contains('/')) && File.Exists(raw)
            ? _appResolver.EnsureIconCached(appId, raw)
            : null;

        var app = _repository.AddOrUpdateProgramEntry(displayName, raw, categoryId, customIdleSeconds, iconPath);
        _logger.Info($"Programm '{app.DisplayName}' ({app.ProcessName}) wurde der Kategorie-ID {categoryId} zugeordnet.");
        RefreshAll();
        SelectCategory(categoryId);
        return app;
    }

    public TrackedApplication AddWebsiteToCategory(long categoryId, string displayName, string websiteUrlOrDomain, int? customIdleSeconds = null)
    {
        var app = _repository.AddOrUpdateWebsiteEntry(displayName, websiteUrlOrDomain, categoryId, customIdleSeconds);
        _logger.Info($"Webseite '{app.DisplayName}' ({app.ExecutablePath}) wurde der Kategorie-ID {categoryId} zugeordnet.");
        RefreshAll();
        SelectCategory(categoryId);
        return app;
    }

    public IReadOnlyList<TrackedApplication> GetAllTrackedApplications() => _repository.GetAllApplications();

    public void UpdateTheme(ThemePreference theme)
    {
        _settings.Theme = theme;
        _repository.SaveSettings(_settings);
        ThemeChanged?.Invoke(this, theme);
        _logger.Info($"Farbschema geändert auf: {theme}");
    }

    public void SaveGeneralSettings(int globalIdleSeconds, bool launchAtStartup, bool minimizeToTray, int retentionDays)
    {
        _settings.GlobalIdleThresholdSeconds = Math.Clamp(globalIdleSeconds, 15, 7200);
        _settings.LaunchAtStartup = launchAtStartup;
        _settings.MinimizeToTrayOnClose = minimizeToTray;
        _settings.DataRetentionDays = Math.Max(0, retentionDays);

        _repository.SaveSettings(_settings);
        _startupManager.SetStartupEnabled(launchAtStartup);
        _logger.Info($"Einstellungen gespeichert (Globales Idle-Limit: {_settings.GlobalIdleThresholdSeconds}s, Autostart: {launchAtStartup}, Tray: {minimizeToTray}).");
        RefreshAll();
    }

    public void GenerateSixWeekDemoData()
    {
        DemoDataSeeder.SeedRealisticHistory(_repository, daysBack: 42);
        _logger.Info("Beispielverlauf für 6 Wochen (42 Tage) in die Datenbank eingefügt.");
        RefreshAll();
    }

    public void ClearAllUsageData()
    {
        _repository.ClearAllUsageData();
        _logger.Warn("Alle erfassten Nutzungsintervalle wurden vom Benutzer zurückgesetzt.");
        RefreshAll();
    }

    public IReadOnlyList<string> GetRecentLogEntries() => _logger.GetRecentEntries(100);
}
