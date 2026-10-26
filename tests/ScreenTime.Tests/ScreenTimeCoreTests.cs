using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;
using ScreenTime.Core.Persistence;
using ScreenTime.Core.Services;

namespace ScreenTime.Tests;

[TestClass]
public sealed class ScreenTimeCoreTests
{
    private string _tempDir = string.Empty;
    private IAppLogger _logger = null!;
    private SqliteUsageRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ScreenTimeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logger = new FileAppLogger(Path.Combine(_tempDir, "logs"));
        _repository = new SqliteUsageRepository(Path.Combine(_tempDir, "test_screentime.db"), _logger);
        _repository.Initialize();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _repository.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup locks
        }
    }

    [TestMethod]
    public void ApplicationResolver_MapsFriendlyNamesAndLearningCategory()
    {
        var resolver = new ApplicationResolver(Path.Combine(_tempDir, "icons"), _logger);
        var categories = _repository.GetCategories();

        // 1. Chrome -> "Google Chrome", niemals Dateipfad
        var chromeInfo = new ForegroundProcessInfo
        {
            ProcessId = 100,
            ProcessName = "chrome.exe",
            ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        };
        var chromeApp = resolver.Resolve(chromeInfo, categories);
        Assert.AreEqual("Google Chrome", chromeApp.DisplayName);
        Assert.AreEqual("google-chrome", chromeApp.AppIdentifier);

        // 2. PDF Reader -> "Adobe Acrobat Reader" in Kategorie "Lernen & Lesen" (15 Min. Idle-Schwellwert)
        var pdfInfo = new ForegroundProcessInfo
        {
            ProcessId = 200,
            ProcessName = "AcroRd32",
            ExecutablePath = @"C:\Program Files (x86)\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe"
        };
        var pdfApp = resolver.Resolve(pdfInfo, categories);
        var learningCat = categories.First(c => c.Key == "learning");
        Assert.AreEqual("Adobe Acrobat Reader", pdfApp.DisplayName);
        Assert.AreEqual(learningCat.Id, pdfApp.CategoryId);
        Assert.AreEqual(900, learningCat.DefaultIdleThresholdSeconds);

        // 3. Mehrere Prozesse derselben App (AcroRd32 und Acrobat) erhalten denselben AppIdentifier
        var acrobatMain = resolver.Resolve(new ForegroundProcessInfo { ProcessId = 201, ProcessName = "Acrobat" }, categories);
        Assert.AreEqual(pdfApp.AppIdentifier, acrobatMain.AppIdentifier);

        // 4. Unbekannte App erhält lesbaren Fallback-Namen statt leerem String oder Pfad
        var unknownApp = resolver.Resolve(new ForegroundProcessInfo
        {
            ProcessId = 300,
            ProcessName = "MyCustomStudyTool_x64.exe"
        }, categories);
        Assert.AreEqual("My Custom Study Tool x64", unknownApp.DisplayName);
        Assert.AreEqual(learningCat.Id, unknownApp.CategoryId); // "study" im Namen erkannt
    }

    [TestMethod]
    public void TrackingService_CountsLongPdfReadingUpToCategoryThreshold_AndPausesAfterThreshold()
    {
        var fgProvider = new FakeForegroundAppProvider();
        var idleDetector = new FakeIdleDetector();
        var powerMonitor = new FakePowerMonitor();
        var resolver = new ApplicationResolver(Path.Combine(_tempDir, "icons"), _logger);

        using var tracker = new TrackingService(
            fgProvider, idleDetector, resolver, _repository, powerMonitor, _logger);

        var startTime = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(1));

        // Nutzer öffnet SumatraPDF (Kategorie "Lernen & Lesen" -> 900s = 15 Min. Schwellwert)
        fgProvider.CurrentProcess = new ForegroundProcessInfo
        {
            ProcessId = 555,
            ProcessName = "SumatraPDF",
            TimestampUtc = startTime.ToUniversalTime()
        };
        idleDetector.CurrentIdleDuration = TimeSpan.Zero;
        tracker.ProcessTick(startTime.ToUniversalTime());

        // Nach 10 Minuten (600s) ohne Maus/Tastatur liest der Nutzer immer noch das PDF -> muss voll zählen!
        idleDetector.CurrentIdleDuration = TimeSpan.FromMinutes(10);
        tracker.ProcessTick(startTime.AddMinutes(10).ToUniversalTime());
        Assert.IsFalse(tracker.GetLiveStatus().IsUserIdle, "Nach 10 Min. PDF-Lesen darf die App noch nicht als Idle gelten (Schwellwert 15 Min.).");

        // Nach 16 Minuten (960s) ohne Eingabe ist der 15-Minuten-Schwellwert überschritten ->
        // die ersten 15 Minuten (900s) zählen als aktiv, ab Minute 15 pausiert die Zählung!
        idleDetector.CurrentIdleDuration = TimeSpan.FromMinutes(16);
        tracker.ProcessTick(startTime.AddMinutes(16).ToUniversalTime());
        Assert.IsTrue(tracker.GetLiveStatus().IsUserIdle, "Nach 16 Min. ohne Eingabe muss der 15-Min.-Schwellwert greifen.");

        // Auch nach 25 Minuten ohne Eingabe darf keine weitere Zeit aufaddiert werden
        idleDetector.CurrentIdleDuration = TimeSpan.FromMinutes(25);
        tracker.ProcessTick(startTime.AddMinutes(25).ToUniversalTime());

        var intervals = _repository.GetIntervalsInRange(startTime.AddHours(-1).ToUniversalTime(), startTime.AddHours(2).ToUniversalTime());
        Assert.AreEqual(1, intervals.Count);
        Assert.AreEqual(900, intervals[0].DurationSeconds, "Genau die 900 Sekunden (15 Min.) bis zum Schwellwert müssen gezählt werden.");
        Assert.AreEqual(InterruptionReason.IdleTimeout, intervals[0].EndReason);

        // Sobald Maus/Tastatur wieder bewegt wird (Minute 26), startet ein neues Intervall
        idleDetector.CurrentIdleDuration = TimeSpan.FromSeconds(1);
        tracker.ProcessTick(startTime.AddMinutes(26).ToUniversalTime());
        tracker.ProcessTick(startTime.AddMinutes(30).ToUniversalTime());

        intervals = _repository.GetIntervalsInRange(startTime.AddHours(-1).ToUniversalTime(), startTime.AddHours(2).ToUniversalTime());
        Assert.AreEqual(2, intervals.Count);
        Assert.AreEqual(240, intervals[1].DurationSeconds); // 4 Minuten (Min. 26 bis 30)
    }

    [TestMethod]
    public void StatisticsService_SixWeekAverage_IgnoresDaysBeforeFirstRecordedDay()
    {
        var statsService = new StatisticsService(_repository);
        var categories = _repository.GetCategories();
        long learningId = categories.First(c => c.Key == "learning").Id;
        long prodId = categories.First(c => c.Key == "productivity").Id;

        var app1 = _repository.UpsertApplication(new TrackedApplication
        {
            AppIdentifier = "obsidian",
            DisplayName = "Obsidian",
            ProcessName = "Obsidian",
            CategoryId = learningId
        });

        var app2 = _repository.UpsertApplication(new TrackedApplication
        {
            AppIdentifier = "vscode",
            DisplayName = "Visual Studio Code",
            ProcessName = "Code",
            CategoryId = prodId
        });

        // Angenommen heute ist der 25.10.2026, und der erste Erfassungstag war vor 4 Tagen (also insgesamt 5 Kalendertage inkl. heute:
        // Tag -4: 60 Min, Tag -3: 0 Min (Null-Nutzungstag NACH Erfassungsbeginn), Tag -2: 120 Min, Tag -1: 60 Min, Heute: 60 Min -> Summe = 300 Min über 5 Tage -> Ø = 60 Min/Tag)
        DateOnly refToday = new(2026, 10, 25);

        InsertLocalDayUsage(app1.Id, refToday.AddDays(-4), minutes: 60);
        // refToday.AddDays(-3) hat 0 Minuten Nutzung, liegt aber NACH dem ersten Erfassungstag!
        InsertLocalDayUsage(app1.Id, refToday.AddDays(-2), minutes: 120);
        InsertLocalDayUsage(app2.Id, refToday.AddDays(-1), minutes: 60);
        InsertLocalDayUsage(app2.Id, refToday, minutes: 60);

        var sixWeekStats = statsService.GetSixWeekAverageStatistics(refToday);

        Assert.AreEqual(5, sixWeekStats.SixWeekEvaluatedDays, "Es dürfen nur die 5 Tage ab dem ersten Erfassungstag gezählt werden, nicht 42.");
        Assert.AreEqual(TimeSpan.FromMinutes(60), sixWeekStats.SixWeekAverageDailyDuration);

        // Prüfe Konsistenz zwischen Gesamtzeit, Kategorien und Apps im 30-Tage-Verlauf
        var monthStats = statsService.GetLast30DaysStatistics(refToday);
        Assert.AreEqual(TimeSpan.FromMinutes(300), monthStats.TotalDuration);
        long catSumSeconds = monthStats.Categories.Sum(c => (long)c.TotalDuration.TotalSeconds);
        long appSumSeconds = monthStats.Apps.Sum(a => (long)a.TotalDuration.TotalSeconds);
        Assert.AreEqual((long)monthStats.TotalDuration.TotalSeconds, catSumSeconds);
        Assert.AreEqual((long)monthStats.TotalDuration.TotalSeconds, appSumSeconds);
    }

    [TestMethod]
    public void Repository_RecoversUnclosedIntervalsOnRestart()
    {
        var app = _repository.UpsertApplication(new TrackedApplication
        {
            AppIdentifier = "anki",
            DisplayName = "Anki",
            ProcessName = "anki",
            CategoryId = 1
        });

        var startUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
        var checkpointUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        _repository.InsertUsageInterval(new UsageInterval
        {
            ApplicationId = app.Id,
            StartTimeUtc = startUtc,
            EndTimeUtc = checkpointUtc,
            DurationSeconds = 900,
            EndReason = InterruptionReason.Active
        });

        // Simuliere Neustart
        _repository.RecoverUnclosedIntervals();

        var recovered = _repository.GetIntervalsInRange(startUtc.AddMinutes(-1), DateTimeOffset.UtcNow);
        Assert.AreEqual(1, recovered.Count);
        Assert.AreEqual(InterruptionReason.RecoveredAfterRestart, recovered[0].EndReason);
        Assert.AreEqual(900, recovered[0].DurationSeconds);
    }

    [TestMethod]
    public void CustomCategory_ProgramAndWebsiteEntries_AndVideoStreamingActiveCounting()
    {
        var fgProvider = new FakeForegroundAppProvider();
        var idleDetector = new FakeIdleDetector();
        var powerMonitor = new FakePowerMonitor();
        var resolver = new ApplicationResolver(Path.Combine(_tempDir, "icons"), _logger);

        // 1. Benutzer erstellt eine eigene Kategorie mit 20 Min. (1200s) Schwellwert
        var customCat = _repository.CreateCategory("Uni & Vorlesungen", "#5856D6", "\uE8FD", 1200);
        Assert.IsTrue(customCat.IsCustom);
        Assert.AreEqual(1200, customCat.DefaultIdleThresholdSeconds);

        // 2. Benutzer trägt ein Programm am PC und eine Webseite (Link) in diese Kategorie ein
        var progEntry = _repository.AddOrUpdateProgramEntry("Mein PDF Studio", @"C:\Apps\MyPdfStudio.exe", customCat.Id, null, null);
        Assert.AreEqual(customCat.Id, progEntry.CategoryId);
        Assert.IsFalse(progEntry.IsWebsite);

        var webEntry = _repository.AddOrUpdateWebsiteEntry("Moodle Lernportal", "https://moodle.uni-beispiel.de/course", customCat.Id, null);
        Assert.AreEqual(customCat.Id, webEntry.CategoryId);
        Assert.IsTrue(webEntry.IsWebsite);
        Assert.AreEqual("web:moodle.uni-beispiel.de", webEntry.AppIdentifier);

        // 3. Wenn im Browser (chrome) die Webseite geöffnet ist, wird sie erkannt und der Kategorie zugeordnet
        using var tracker = new TrackingService(fgProvider, idleDetector, resolver, _repository, powerMonitor, _logger);
        var t0 = new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.FromHours(1)).ToUniversalTime();

        fgProvider.CurrentProcess = new ForegroundProcessInfo
        {
            ProcessId = 777,
            ProcessName = "chrome",
            TransientWindowTitle = "Vorlesung 3 - Moodle Lernportal - Google Chrome",
            TimestampUtc = t0
        };
        idleDetector.CurrentIdleDuration = TimeSpan.Zero;
        tracker.ProcessTick(t0);

        Assert.AreEqual("Moodle Lernportal", tracker.GetLiveStatus().CurrentAppDisplayName);
        Assert.AreEqual("Uni & Vorlesungen", tracker.GetLiveStatus().CurrentCategoryName);
        Assert.AreEqual(1200, tracker.GetLiveStatus().ActiveIdleThresholdSeconds);

        // 4. Video-Streaming zählt auch nach Überschreiten des Maus-/Tastatur-Schwellwerts als aktive Bildschirmzeit!
        idleDetector.CurrentIdleDuration = TimeSpan.FromMinutes(35); // > 20 Min. ohne Maus/Tastatur
        idleDetector.MediaStreamingActive = true; // Aber Video-/Audio-Stream läuft!
        tracker.ProcessTick(t0.AddMinutes(35));

        Assert.IsFalse(tracker.GetLiveStatus().IsUserIdle, "Während Video-Streaming aktiv ist, darf die Sitzung nicht als Leerlauf gewertet werden.");
        Assert.IsTrue(tracker.GetLiveStatus().IsMediaOrStreamingActive);
    }

    private void InsertLocalDayUsage(long appId, DateOnly date, int minutes)
    {
        var localStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Local));
        var localEnd = localStart.AddMinutes(minutes);
        _repository.InsertUsageInterval(new UsageInterval
        {
            ApplicationId = appId,
            StartTimeUtc = localStart.ToUniversalTime(),
            EndTimeUtc = localEnd.ToUniversalTime(),
            DurationSeconds = minutes * 60,
            EndReason = InterruptionReason.AppSwitch
        });
    }

    private sealed class FakeForegroundAppProvider : IForegroundAppProvider
    {
        public ForegroundProcessInfo? CurrentProcess { get; set; }
        public ForegroundProcessInfo? GetCurrentForegroundProcess() => CurrentProcess;
    }

    private sealed class FakeIdleDetector : IIdleDetector
    {
        public TimeSpan CurrentIdleDuration { get; set; }
        public bool MediaStreamingActive { get; set; }
        public TimeSpan GetIdleDuration() => CurrentIdleDuration;
        public bool IsMediaOrStreamingActive() => MediaStreamingActive;
        public bool IsIdle(int thresholdSeconds, out TimeSpan currentIdleDuration)
        {
            currentIdleDuration = CurrentIdleDuration;
            return currentIdleDuration.TotalSeconds >= thresholdSeconds;
        }
    }

    private sealed class FakePowerMonitor : ISystemPowerMonitor
    {
        public bool IsSessionLockedOrSuspended { get; set; }
        public event EventHandler<InterruptionReason>? SessionInterrupted;
        public event EventHandler? SessionResumed;
        public void TriggerInterrupt(InterruptionReason reason)
        {
            IsSessionLockedOrSuspended = true;
            SessionInterrupted?.Invoke(this, reason);
        }
        public void TriggerResume()
        {
            IsSessionLockedOrSuspended = false;
            SessionResumed?.Invoke(this, EventArgs.Empty);
        }
        public void Dispose() { }
    }
}
