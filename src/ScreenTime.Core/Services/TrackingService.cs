using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;

namespace ScreenTime.Core.Services;

/// <summary>
/// Koordiniert die laufende Erfassung der aktiven Vordergrund-App,
/// wendet App-/Kategorie-spezifische Idle-Schwellwerte an und speichert Intervalle regelmäßig in SQLite.
/// </summary>
public sealed class TrackingService : ITrackingService
{
    private readonly IForegroundAppProvider _foregroundProvider;
    private readonly IIdleDetector _idleDetector;
    private readonly IApplicationResolver _appResolver;
    private readonly IUsageRepository _repository;
    private readonly ISystemPowerMonitor _powerMonitor;
    private readonly IAppLogger _logger;

    private readonly object _syncLock = new();
    private Timer? _timer;
    private UsageInterval? _activeInterval;
    private TrackedApplication? _activeApp;
    private AppCategory? _activeCategory;
    private DateTimeOffset _lastCheckpointUtc = DateTimeOffset.MinValue;
    private LiveTrackingStatus _liveStatus = new();
    private bool _isRunning;
    private bool _disposed;

    public event EventHandler<LiveTrackingStatus>? StatusUpdated;

    public TrackingService(
        IForegroundAppProvider foregroundProvider,
        IIdleDetector idleDetector,
        IApplicationResolver appResolver,
        IUsageRepository repository,
        ISystemPowerMonitor powerMonitor,
        IAppLogger logger)
    {
        _foregroundProvider = foregroundProvider ?? throw new ArgumentNullException(nameof(foregroundProvider));
        _idleDetector = idleDetector ?? throw new ArgumentNullException(nameof(idleDetector));
        _appResolver = appResolver ?? throw new ArgumentNullException(nameof(appResolver));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _powerMonitor = powerMonitor ?? throw new ArgumentNullException(nameof(powerMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _powerMonitor.SessionInterrupted += OnSessionInterrupted;
        _powerMonitor.SessionResumed += OnSessionResumed;
    }

    public void Start()
    {
        lock (_syncLock)
        {
            if (_isRunning)
            {
                return;
            }

            _repository.Initialize();
            _repository.RecoverUnclosedIntervals();

            var settings = _repository.GetSettings();
            if (settings.DataRetentionDays > 0)
            {
                _repository.PurgeOldIntervals(settings.DataRetentionDays);
            }

            _isRunning = true;
            int intervalMs = Math.Clamp(settings.PollingIntervalMs, 250, 5000);
            _timer = new Timer(_ => SafeTick(), null, 0, intervalMs);
            _logger.Info($"TrackingService gestartet (Polling: {intervalMs} ms).");
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _timer?.Dispose();
            _timer = null;

            CloseActiveInterval(DateTimeOffset.UtcNow, InterruptionReason.AppShutdown);
            _logger.Info("TrackingService gestoppt und aktives Intervall gespeichert.");
        }
    }

    public LiveTrackingStatus GetLiveStatus()
    {
        lock (_syncLock)
        {
            return _liveStatus;
        }
    }

    private void SafeTick()
    {
        try
        {
            ProcessTick(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Error("Unerwarteter Fehler im Tracking-Durchlauf", ex);
        }
    }

    public void ProcessTick(DateTimeOffset nowUtc)
    {
        LiveTrackingStatus statusSnapshot;

        lock (_syncLock)
        {
            if (_powerMonitor.IsSessionLockedOrSuspended)
            {
                CloseActiveInterval(nowUtc, InterruptionReason.SessionLock);
                _liveStatus = new LiveTrackingStatus
                {
                    IsTrackingActive = _isRunning,
                    IsUserIdle = true,
                    IsSessionLockedOrSuspended = true,
                    CurrentAppDisplayName = "Sitzung gesperrt / Standby",
                    CurrentProcessName = "-",
                    CurrentCategoryName = "-",
                    LastUpdatedUtc = nowUtc
                };
                statusSnapshot = _liveStatus;
            }
            else
            {
                var fgProcess = _foregroundProvider.GetCurrentForegroundProcess();
                if (fgProcess is null)
                {
                    CloseActiveInterval(nowUtc, InterruptionReason.ProcessExited);
                    _liveStatus = new LiveTrackingStatus
                    {
                        IsTrackingActive = _isRunning,
                        IsUserIdle = false,
                        IsSessionLockedOrSuspended = false,
                        CurrentAppDisplayName = "Kein aktives Fenster",
                        CurrentProcessName = "-",
                        CurrentCategoryName = "-",
                        LastUpdatedUtc = nowUtc
                    };
                    statusSnapshot = _liveStatus;
                }
                else
                {
                    var categories = _repository.GetCategories();
                    var settings = _repository.GetSettings();
                    var existingApps = _repository.GetAllApplications();

                    var resolvedCandidate = _appResolver.Resolve(fgProcess, categories, existingApps);
                    var persistedApp = _repository.UpsertApplication(resolvedCandidate);
                    var category = categories.FirstOrDefault(c => c.Id == persistedApp.CategoryId)
                                ?? categories.FirstOrDefault();

                    int effectiveThreshold = GetEffectiveIdleThresholdSeconds(persistedApp, category, settings);
                    bool rawIdle = _idleDetector.IsIdle(effectiveThreshold, out TimeSpan idleDuration);
                    bool isMediaStreaming = _idleDetector.IsMediaOrStreamingActive()
                                         || fgProcess.IsFullscreen
                                         || IsStreamingWindowTitle(fgProcess.TransientWindowTitle);

                    // Video-Streaming / aktive Medienwiedergabe zählt als aktive Bildschirmzeit, auch ohne Maus-/Tastatureingabe
                    bool isIdle = rawIdle && !isMediaStreaming;

                    if (isIdle)
                    {
                        if (_activeInterval is not null)
                        {
                            // Zeit bis zum Schwellwert zählt als aktiv; ab Überschreitung pausiert die Zählung
                            TimeSpan excessIdle = idleDuration - TimeSpan.FromSeconds(effectiveThreshold);
                            DateTimeOffset thresholdReachedUtc = excessIdle > TimeSpan.Zero
                                ? nowUtc - excessIdle
                                : nowUtc;

                            if (thresholdReachedUtc < _activeInterval.StartTimeUtc)
                            {
                                thresholdReachedUtc = _activeInterval.StartTimeUtc;
                            }

                            CloseActiveInterval(thresholdReachedUtc, InterruptionReason.IdleTimeout);
                            _logger.Info($"Leerlauf erkannt für '{persistedApp.DisplayName}' (Schwellwert: {effectiveThreshold}s). Zählung pausiert.");
                        }

                        _liveStatus = new LiveTrackingStatus
                        {
                            IsTrackingActive = _isRunning,
                            IsUserIdle = true,
                            IsSessionLockedOrSuspended = false,
                            CurrentAppDisplayName = persistedApp.DisplayName,
                            CurrentProcessName = persistedApp.ProcessName,
                            CurrentCategoryName = category?.Name ?? "Sonstiges",
                            TimeSinceLastInput = idleDuration,
                            ActiveIdleThresholdSeconds = effectiveThreshold,
                            CurrentSessionDuration = TimeSpan.Zero,
                            LastUpdatedUtc = nowUtc
                        };
                        statusSnapshot = _liveStatus;
                    }
                    else
                    {
                        // Nutzer ist aktiv (innerhalb des App-/Kategorie-Schwellwerts)
                        if (_activeInterval is not null && _activeInterval.ApplicationId != persistedApp.Id)
                        {
                            // Fensterwechsel: vorheriges Intervall exakt zum aktuellen Zeitpunkt abschließen
                            CloseActiveInterval(nowUtc, InterruptionReason.AppSwitch);
                        }

                        if (_activeInterval is not null)
                        {
                            // Prüfen auf Mitternachts-Übergang in lokaler Zeitzone
                            var startLocal = _activeInterval.StartTimeUtc.ToLocalTime();
                            var nowLocal = nowUtc.ToLocalTime();
                            if (startLocal.Date < nowLocal.Date)
                            {
                                var midnightLocal = new DateTimeOffset(nowLocal.Date, nowLocal.Offset);
                                var midnightUtc = midnightLocal.ToUniversalTime();
                                CloseActiveInterval(midnightUtc, InterruptionReason.MidnightRollover);
                                StartNewInterval(persistedApp, category, midnightUtc, nowUtc);
                            }
                            else
                            {
                                _activeInterval.EndTimeUtc = nowUtc;
                                _activeInterval.DurationSeconds = Math.Max(0, (int)(nowUtc - _activeInterval.StartTimeUtc).TotalSeconds);

                                // Regelmäßiger Checkpoint alle 10 Sekunden in SQLite
                                if ((nowUtc - _lastCheckpointUtc).TotalSeconds >= 10)
                                {
                                    _repository.UpdateUsageInterval(_activeInterval);
                                    _lastCheckpointUtc = nowUtc;
                                }
                            }
                        }
                        else
                        {
                            StartNewInterval(persistedApp, category, nowUtc, nowUtc);
                        }

                        _liveStatus = new LiveTrackingStatus
                        {
                            IsTrackingActive = _isRunning,
                            IsUserIdle = false,
                            IsMediaOrStreamingActive = isMediaStreaming,
                            IsSessionLockedOrSuspended = false,
                            CurrentAppDisplayName = persistedApp.DisplayName,
                            CurrentProcessName = persistedApp.ProcessName,
                            CurrentCategoryName = category?.Name ?? "Sonstiges",
                            TimeSinceLastInput = idleDuration,
                            ActiveIdleThresholdSeconds = effectiveThreshold,
                            CurrentSessionDuration = _activeInterval is not null
                                ? nowUtc - _activeInterval.StartTimeUtc
                                : TimeSpan.Zero,
                            LastUpdatedUtc = nowUtc
                        };
                        statusSnapshot = _liveStatus;
                    }
                }
            }
        }

        StatusUpdated?.Invoke(this, statusSnapshot);
    }

    private static bool IsStreamingWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }
        return title.Contains("YouTube", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Netflix", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Twitch", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Prime Video", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Disney+", StringComparison.OrdinalIgnoreCase)
            || title.Contains("ARD Mediathek", StringComparison.OrdinalIgnoreCase)
            || title.Contains("ZDFmediathek", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Crunchyroll", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Vimeo", StringComparison.OrdinalIgnoreCase);
    }

    public static int GetEffectiveIdleThresholdSeconds(TrackedApplication? app, AppCategory? category, AppSettings settings)
    {
        if (app?.CustomIdleThresholdSeconds is int custom && custom > 0)
        {
            return Math.Max(15, custom);
        }

        if (category is not null && category.DefaultIdleThresholdSeconds > 0)
        {
            return Math.Max(15, category.DefaultIdleThresholdSeconds);
        }

        return Math.Max(15, settings.GlobalIdleThresholdSeconds);
    }

    private void StartNewInterval(TrackedApplication app, AppCategory? category, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        _activeApp = app;
        _activeCategory = category;
        _activeInterval = new UsageInterval
        {
            ApplicationId = app.Id,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            DurationSeconds = Math.Max(0, (int)(endUtc - startUtc).TotalSeconds),
            EndReason = InterruptionReason.Active
        };
        _repository.InsertUsageInterval(_activeInterval);
        _lastCheckpointUtc = endUtc;
    }

    private void CloseActiveInterval(DateTimeOffset endUtc, InterruptionReason reason)
    {
        if (_activeInterval is null)
        {
            return;
        }

        if (endUtc < _activeInterval.StartTimeUtc)
        {
            endUtc = _activeInterval.StartTimeUtc;
        }

        _activeInterval.EndTimeUtc = endUtc;
        _activeInterval.DurationSeconds = Math.Max(0, (int)(endUtc - _activeInterval.StartTimeUtc).TotalSeconds);
        _activeInterval.EndReason = reason;

        try
        {
            _repository.UpdateUsageInterval(_activeInterval);
        }
        catch (Exception ex)
        {
            _logger.Error("Fehler beim Abschließen des aktiven Nutzungsintervalls", ex);
        }
        finally
        {
            _activeInterval = null;
            _activeApp = null;
            _activeCategory = null;
        }
    }

    private void OnSessionInterrupted(object? sender, InterruptionReason reason)
    {
        lock (_syncLock)
        {
            CloseActiveInterval(DateTimeOffset.UtcNow, reason);
        }
    }

    private void OnSessionResumed(object? sender, EventArgs e)
    {
        SafeTick();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
        _powerMonitor.SessionInterrupted -= OnSessionInterrupted;
        _powerMonitor.SessionResumed -= OnSessionResumed;
    }
}
