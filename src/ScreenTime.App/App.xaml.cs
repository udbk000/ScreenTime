using Microsoft.UI.Xaml;
using ScreenTime.App.ViewModels;
using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Persistence;
using ScreenTime.Core.Services;

namespace ScreenTime.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private IAppLogger? _logger;
    private IUsageRepository? _repository;
    private ISystemPowerMonitor? _powerMonitor;
    private ITrackingService? _trackingService;

    public static MainViewModel ViewModel { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenTime");
        Directory.CreateDirectory(appDataRoot);

        string logDir = Path.Combine(appDataRoot, "logs");
        string iconDir = Path.Combine(appDataRoot, "icons");
        string dbPath = Path.Combine(appDataRoot, "screentime.db");

        _logger = new FileAppLogger(logDir);
        _logger.Info("ScreenTime wird gestartet...");

        _repository = new SqliteUsageRepository(dbPath, _logger);
        _repository.Initialize();

        var fgProvider = new ForegroundAppProvider(_logger);
        var idleDetector = new IdleDetector();
        var appResolver = new ApplicationResolver(iconDir, _logger);
        _powerMonitor = new SystemPowerMonitor(_logger);

        _trackingService = new TrackingService(
            fgProvider,
            idleDetector,
            appResolver,
            _repository,
            _powerMonitor,
            _logger);

        var statisticsService = new StatisticsService(_repository);
        var startupManager = new StartupManager(_logger);

        ViewModel = new MainViewModel(
            _repository,
            statisticsService,
            _trackingService,
            appResolver,
            startupManager,
            _logger);

        _trackingService.Start();
        ViewModel.RefreshAll();

        _mainWindow = new MainWindow(ViewModel, _trackingService);
        bool startMinimized = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        if (!startMinimized)
        {
            _mainWindow.Activate();
        }
    }

    public void ShutdownApplication()
    {
        _logger?.Info("ScreenTime wird ordentlich beendet...");
        _trackingService?.Dispose();
        _powerMonitor?.Dispose();
        _repository?.Dispose();
        Environment.Exit(0);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger?.Error("Nicht abgefangener UI-Ausnahmefehler", e.Exception);
        e.Handled = true;
    }
}
