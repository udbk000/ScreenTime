using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ScreenTime.App.Helpers;
using ScreenTime.App.ViewModels;
using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;
using WinRT.Interop;

namespace ScreenTime.App;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ITrackingService _trackingService;
    private readonly DispatcherTimer _uiRefreshTimer;
    private SystemTrayService? _trayService;
    private WidgetWindow? _widgetWindow;
    private AppWindow? _appWindow;
    private nint _hwnd;
    private bool _isInitializingUi;
    private bool _allowRealClose;

    public MainWindow(MainViewModel viewModel, ITrackingService trackingService)
    {
        this.InitializeComponent();
        _viewModel = viewModel;
        _trackingService = trackingService;

        ConfigureWindowAndTray();
        ApplyThemePreference(_viewModel.Settings.Theme);
        _viewModel.ThemeChanged += (_, theme) => ApplyThemePreference(theme);

        _trackingService.StatusUpdated += OnTrackingStatusUpdated;

        _uiRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _uiRefreshTimer.Tick += (_, _) =>
        {
            _viewModel.RefreshAll();
            RefreshActiveViewUi();
            _widgetWindow?.RefreshWidgetUi();
        };
        _uiRefreshTimer.Start();

        RefreshAllViewsUi();
    }

    private void ConfigureWindowAndTray()
    {
        try
        {
            this.Title = string.Empty;
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(CustomTitleBarBorder);

            _hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow = appWindow;

            SettingsVersionBadgeText.Text = $"Version {MainViewModel.AppVersion}";
            InitializeNewCategoryRgbPalette();

            appWindow.Resize(new Windows.Graphics.SizeInt32(1320, 860));
            appWindow.Closing += OnAppWindowClosing;

            _trayService = new SystemTrayService(
                _hwnd,
                onRestoreRequested: () =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowWindow(_hwnd, 9); // SW_RESTORE
                        this.Activate();
                        _viewModel.RefreshAll();
                        RefreshAllViewsUi();
                    });
                },
                onToggleWidgetRequested: () =>
                {
                    DispatcherQueue.TryEnqueue(ToggleDesktopWidget);
                },
                onExitRequested: () =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _allowRealClose = true;
                        _widgetWindow?.Close();
                        _trayService?.Dispose();
                        ((App)Application.Current).ShutdownApplication();
                    });
                });
            _trayService.Initialize();

            WindowIconHelper.ApplyWindowIcon(_hwnd, _appWindow, _viewModel.Settings.Theme, _trayService);

            RootGrid.ActualThemeChanged += (_, _) =>
            {
                bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
                CustomTitleBarBorder.Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White);
                SettingsContactIconBadge.Background = ColorHelperEx.GetPrimaryAccentBrush(isDark);
                ColorHelperEx.ApplyWindowTitleBarTheme(_hwnd, _appWindow, _viewModel.Settings.Theme);
                WindowIconHelper.ApplyWindowIcon(_hwnd, _appWindow, _viewModel.Settings.Theme, _trayService);
                RefreshAllViewsUi();
            };
        }
        catch
        {
            // Falls Window-Interop in speziellen Umgebungen eingeschränkt ist, normal weiterlaufen
        }
    }

    private void ToggleDesktopWidget()
    {
        if (_widgetWindow is not null)
        {
            try
            {
                _widgetWindow.Close();
            }
            catch
            {
                // Ignore
            }
            _widgetWindow = null;
            return;
        }

        _widgetWindow = new WidgetWindow(_viewModel, onOpenMainRequested: () =>
        {
            ShowWindow(_hwnd, 9);
            this.Activate();
        });
        _widgetWindow.Closed += (_, _) => _widgetWindow = null;
        _widgetWindow.Activate();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowRealClose && _viewModel.Settings.MinimizeToTrayOnClose)
        {
            args.Cancel = true;
            ShowWindow(_hwnd, 0); // SW_HIDE (Minimiert in den System-Tray)
        }
        else
        {
            _uiRefreshTimer.Stop();
            _trayService?.Dispose();
            ((App)Application.Current).ShutdownApplication();
        }
    }

    private void ApplyThemePreference(ThemePreference preference)
    {
        RootGrid.RequestedTheme = preference switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        bool isDark = ColorHelperEx.IsEffectiveDarkMode(preference);
        CustomTitleBarBorder.Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White);
        SettingsContactIconBadge.Background = ColorHelperEx.GetPrimaryAccentBrush(isDark);

        ColorHelperEx.ApplyWindowTitleBarTheme(_hwnd, _appWindow, preference);
        WindowIconHelper.ApplyWindowIcon(_hwnd, _appWindow, preference, _trayService);

        QuickThemeLabel.Text = preference switch
        {
            ThemePreference.Light => "Modus: Hell",
            ThemePreference.Dark => "Modus: Dunkel",
            _ => "Modus: System"
        };

        QuickThemeIcon.Glyph = preference switch
        {
            ThemePreference.Light => "\uE706",
            ThemePreference.Dark => "\uE708",
            _ => "\uE793"
        };

        RefreshAllViewsUi();
        _widgetWindow?.RefreshWidgetUi();
    }

    private void OnQuickThemeToggleClicked(object sender, RoutedEventArgs e)
    {
        ThemePreference next = _viewModel.Settings.Theme switch
        {
            ThemePreference.System => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            _ => ThemePreference.System
        };
        _viewModel.UpdateTheme(next);
        SyncSettingsControlsFromModel();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshAll();
        RefreshAllViewsUi();
    }

    private void OnTrackingStatusUpdated(object? sender, LiveTrackingStatus status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _viewModel.LiveStatus = status;
            UpdateLiveHeaderAndDiagnostics(status);
        });
    }

    private void UpdateLiveHeaderAndDiagnostics(LiveTrackingStatus status)
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);

        if (status.IsSessionLockedOrSuspended)
        {
            LiveStatusDot.Fill = ColorHelperEx.GetSecondaryAccentBrush(isDark);
            LiveStatusHeaderText.Text = "Pausiert (Sperrbildschirm / Standby)";
            LiveIdleHeaderText.Text = "";
        }
        else if (status.IsUserIdle)
        {
            LiveStatusDot.Fill = ColorHelperEx.GetSecondaryAccentBrush(isDark);
            LiveStatusHeaderText.Text = $"Leerlauf erkannt ({status.CurrentAppDisplayName})";
            LiveIdleHeaderText.Text = $"• Keine Eingabe seit {(int)status.TimeSinceLastInput.TotalSeconds}s (Limit: {status.ActiveIdleThresholdSeconds / 60.0:0.#} Min.)";
        }
        else
        {
            LiveStatusDot.Fill = ColorHelperEx.GetPrimaryAccentBrush(isDark);
            LiveStatusHeaderText.Text = $"Aktiv: {status.CurrentAppDisplayName}";
            string streamTag = status.IsMediaOrStreamingActive ? " • Stream/Medien aktiv" : "";
            LiveIdleHeaderText.Text = $"• {status.CurrentCategoryName} (Idle-Limit: {FormatSecondsShort(status.ActiveIdleThresholdSeconds)}){streamTag}";
        }

        _trayService?.UpdateTooltip($"ScreenTime – Heute: {_viewModel.TodayStatistics.FormattedTotalDuration} | {LiveStatusHeaderText.Text}");
        _widgetWindow?.RefreshWidgetUi();

        DiagCurrentAppText.Text = $"Aktuelle App: {status.CurrentAppDisplayName}";
        DiagCurrentProcessText.Text = $"Technischer Prozess: {status.CurrentProcessName}";
        DiagCurrentCategoryText.Text = $"Kategorie: {status.CurrentCategoryName}";
        DiagIdleDurationText.Text = $"Zeit seit letzter Maus-/Tastatureingabe: {(int)status.TimeSinceLastInput.TotalSeconds} Sek.";
        DiagThresholdText.Text = $"Aktiver Idle-Schwellwert: {status.ActiveIdleThresholdSeconds} Sek. ({status.ActiveIdleThresholdSeconds / 60.0:0.#} Min.)";
        DiagSessionStateText.Text = status.IsSessionLockedOrSuspended
            ? "Status: Gesperrt / Standby (Zählung pausiert)"
            : status.IsUserIdle
                ? "Status: Leerlauf überschritten (Zählung pausiert)"
                : $"Status: Aktive Zählung läuft ({AppUsageSummary.FormatDuration(status.CurrentSessionDuration)})";
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        SwitchToSection(tag);
    }

    private void SwitchToSection(string tag)
    {
        DashboardView.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = tag == "history" ? Visibility.Visible : Visibility.Collapsed;
        AppsView.Visibility = tag == "apps" ? Visibility.Visible : Visibility.Collapsed;
        CategoriesView.Visibility = tag == "categories" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

        foreach (var menuObj in MainNav.MenuItems)
        {
            if (menuObj is NavigationViewItem navItem && (navItem.Tag as string) == tag && !ReferenceEquals(MainNav.SelectedItem, navItem))
            {
                MainNav.SelectedItem = navItem;
                break;
            }
        }

        _viewModel.RefreshAll();
        RefreshActiveViewUi();
    }

    private void RefreshAllViewsUi()
    {
        UpdateLiveHeaderAndDiagnostics(_viewModel.LiveStatus);
        RenderDashboardUi();
        RenderHistoryUi();
        RenderAppsUi();
        RenderCategoriesUi();
        RenderSettingsUi();
    }

    private void RefreshActiveViewUi()
    {
        UpdateLiveHeaderAndDiagnostics(_viewModel.LiveStatus);

        if (DashboardView.Visibility == Visibility.Visible)
        {
            RenderDashboardUi();
        }
        else if (HistoryView.Visibility == Visibility.Visible)
        {
            RenderHistoryUi();
        }
        else if (AppsView.Visibility == Visibility.Visible)
        {
            RenderAppsUi();
        }
        else if (CategoriesView.Visibility == Visibility.Visible)
        {
            RenderCategoriesUi();
        }
        else if (SettingsView.Visibility == Visibility.Visible)
        {
            DiagLogTextBlock.Text = string.Join(Environment.NewLine, _viewModel.GetRecentLogEntries());
        }
    }

    // =========================================================================
    // 1. DASHBOARD RENDERING
    // =========================================================================
    private void RenderDashboardUi()
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        var today = _viewModel.TodayStatistics;
        var week = _viewModel.WeekStatistics;

        DashboardSubtitleText.Text = $"{today.DateTitle} • Aktive Nutzung getrennt von Leerlauf";
        DashboardTodayTotalText.Text = today.FormattedTotalDuration;
        DashboardSixWeekValueText.Text = $"{today.FormattedSixWeekAverage} / Tag";
        DashboardComparisonText.Text = today.ComparisonDescription;
        DashboardWeekAverageText.Text = $"Ø {week.FormattedAverageDailyDuration} pro Tag";

        RenderCategoryStackedBar(DashboardCategoryStackedBar, today.Categories, isDark);

        DashboardCategoryPillsPanel.Children.Clear();
        foreach (var cat in today.Categories.Where(c => c.TotalDuration.TotalSeconds > 0).Take(4))
        {
            var pill = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            pill.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = ColorHelperEx.GetCategoryBrush(cat.CategoryId, cat.ColorHex, isDark),
                VerticalAlignment = VerticalAlignment.Center
            });
            pill.Children.Add(new TextBlock
            {
                Text = $"{cat.CategoryName}: {cat.FormattedDuration}",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });
            DashboardCategoryPillsPanel.Children.Add(pill);
        }

        if (DashboardCategoryPillsPanel.Children.Count == 0)
        {
            DashboardCategoryPillsPanel.Children.Add(new TextBlock
            {
                Text = "Heute wurden noch keine Kategorien erfasst.",
                FontSize = 12,
                Opacity = 0.6
            });
        }

        RenderDailyBarChart(DashboardWeekChartGrid, week.DailyPoints, week.SixWeekAverageDailyDuration, highlightDate: today.Date, compactLabels: false);
        RenderHourlyChart(DashboardHourlyChartGrid, today.HourlyBreakdown, isDark);

        PopulateAppListPanel(DashboardTopAppsList, today.Apps.Take(6).ToList(), onAppClick: appId =>
        {
            _viewModel.SelectApp(appId);
            SwitchToSection("apps");
        });

        PopulateCategoryListPanel(DashboardCategoriesList, today.Categories.Where(c => c.TotalDuration.TotalSeconds > 0).Take(6).ToList(), onCatClick: catId =>
        {
            _viewModel.SelectCategory(catId);
            SwitchToSection("categories");
        });
    }

    // =========================================================================
    // 2. VERLAUF (7 TAGE / 30 TAGE / 6 WOCHEN & TAGESDETAILS) RENDERING
    // =========================================================================
    private void RenderHistoryUi()
    {
        var period = _viewModel.ActiveHistoryPeriod;
        var selectedDay = _viewModel.SelectedDayStatistics;

        HistorySubtitleText.Text = $"{period.PeriodTitle} ({period.StartDate:dd.MM.yyyy} – {period.EndDate:dd.MM.yyyy})";
        HistoryPeriodDailyAvgText.Text = period.FormattedAverageDailyDuration;
        HistoryPeriodTotalText.Text = $"Gesamtzeit im Zeitraum: {period.FormattedTotalDuration} ({period.EvaluatedDaysForAverage} erfasste Tage)";

        HistorySixWeekAvgText.Text = period.FormattedSixWeekAverage;
        HistorySixWeekDaysBadge.Text = $"Basis: {period.SixWeekEvaluatedDays} {(period.SixWeekEvaluatedDays == 1 ? "Tag" : "Tage")} seit Erfassungsbeginn";
        HistorySixWeekExplanationText.Text = period.SixWeekExplanation;

        HistoryChartTitleText.Text = _viewModel.HistoryMode switch
        {
            HistoryPeriodMode.Last7Days => "WOCHENANSICHT – LETZTE 7 TAGE",
            HistoryPeriodMode.SixWeeks => "SECHS-WOCHEN-VERLAUF – LETZTE 42 TAGE",
            _ => "MONATSVERLAUF – LETZTE 30 TAGE"
        };

        bool compact = period.DailyPoints.Count > 14;
        RenderDailyBarChart(HistoryMainBarChartGrid, period.DailyPoints, period.SixWeekAverageDailyDuration, _viewModel.SelectedDate, compactLabels: compact);

        // Ausgewählter Tag unten
        SelectedDayTitleText.Text = selectedDay.DateTitle;
        SelectedDayComparisonText.Text = selectedDay.ComparisonDescription;
        SelectedDayTotalDurationText.Text = selectedDay.FormattedTotalDuration;

        PopulateCategoryListPanel(SelectedDayCategoriesPanel, selectedDay.Categories.Where(c => c.TotalDuration.TotalSeconds > 0).ToList(), onCatClick: catId =>
        {
            _viewModel.SelectCategory(catId);
            SwitchToSection("categories");
        });

        PopulateAppListPanel(SelectedDayAppsPanel, selectedDay.Apps.ToList(), onAppClick: appId =>
        {
            _viewModel.SelectApp(appId);
            SwitchToSection("apps");
        });

        // Zeitraum-Zusammenfassung
        HistorySummaryBlockTitle.Text = $"Häufig genutzte Kategorien & Apps ({period.PeriodTitle})";
        PopulateCategoryListPanel(HistoryPeriodCategoriesPanel, period.Categories.Where(c => c.TotalDuration.TotalSeconds > 0).ToList(), onCatClick: catId =>
        {
            _viewModel.SelectCategory(catId);
            SwitchToSection("categories");
        });
        PopulateAppListPanel(HistoryPeriodTopAppsPanel, period.Apps.Take(10).ToList(), onAppClick: appId =>
        {
            _viewModel.SelectApp(appId);
            SwitchToSection("apps");
        });
    }

    // =========================================================================
    // 3. APPS & APP-DETAILANSICHT RENDERING
    // =========================================================================
    private void RenderAppsUi()
    {
        var apps = _viewModel.GetFilteredApps(_viewModel.AppsAndCategoriesRange);

        if (_viewModel.SelectedAppDetail is null && apps.Count > 0)
        {
            _viewModel.SelectApp(apps[0].ApplicationId);
        }

        PopulateAppListPanel(AppsMasterListPanel, apps, onAppClick: appId =>
        {
            _viewModel.SelectApp(appId);
            RenderAppsUi();
        }, selectedAppId: _viewModel.SelectedAppDetail?.Application.Id);

        RenderSelectedAppDetailPane();
    }

    private void RenderSelectedAppDetailPane()
    {
        var detail = _viewModel.SelectedAppDetail;
        if (detail is null)
        {
            AppDetailNameText.Text = "Keine App ausgewählt";
            AppDetailCategoryText.Text = "-";
            AppDetailTodayText.Text = "0 Min.";
            AppDetail7DaysText.Text = "0 Min.";
            AppDetail30DaysText.Text = "0 Min.";
            return;
        }

        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        AppDetailNameText.Text = detail.Application.DisplayName;
        AppDetailCategoryText.Text = detail.Category.Name;
        AppDetailCategoryBadge.Background = ColorHelperEx.GetCategoryBrush(detail.Category.Id, detail.Category.ColorHex, isDark);
        AppDetailFallbackIcon.Glyph = detail.Category.IconGlyph;

        if (!string.IsNullOrWhiteSpace(detail.Application.IconCachePath) && File.Exists(detail.Application.IconCachePath))
        {
            try
            {
                AppDetailImage.Source = new BitmapImage(new Uri(detail.Application.IconCachePath));
                AppDetailImage.Visibility = Visibility.Visible;
                AppDetailFallbackIcon.Visibility = Visibility.Collapsed;
            }
            catch
            {
                AppDetailImage.Visibility = Visibility.Collapsed;
                AppDetailFallbackIcon.Visibility = Visibility.Visible;
            }
        }
        else
        {
            AppDetailImage.Visibility = Visibility.Collapsed;
            AppDetailFallbackIcon.Visibility = Visibility.Visible;
        }

        string thresholdOrigin = detail.Application.CustomIdleThresholdSeconds.HasValue
            ? "App-spezifisch"
            : $"Kategorie ({detail.Category.Name})";
        AppDetailIdleThresholdInfo.Text = $"Idle-Schwellwert: {FormatSecondsShort(detail.EffectiveIdleThresholdSeconds)} ({thresholdOrigin})";

        AppDetailTodayText.Text = detail.FormattedTodayDuration;
        AppDetail7DaysText.Text = detail.Formatted7DaysTotal;
        AppDetail7DaysAvgText.Text = $"Ø {AppUsageSummary.FormatDuration(detail.Last7DaysDailyAverage)} / Tag";
        AppDetail30DaysText.Text = detail.Formatted30DaysTotal;
        AppDetail30DaysAvgText.Text = $"Ø {AppUsageSummary.FormatDuration(detail.Last30DaysDailyAverage)} / Tag";

        RenderDailyBarChart(AppDetail7DayChartGrid, detail.Last7DaysPoints, detail.Last7DaysDailyAverage, null, compactLabels: false);
        RenderDailyBarChart(AppDetail30DayChartGrid, detail.Last30DaysPoints, detail.Last30DaysDailyAverage, null, compactLabels: true);

        _isInitializingUi = true;
        try
        {
            AppEditDisplayNameBox.Text = detail.Application.DisplayName;
            AppEditCategoryCombo.Items.Clear();
            int selectedIdx = 0;
            for (int i = 0; i < _viewModel.AllCategories.Count; i++)
            {
                var cat = _viewModel.AllCategories[i];
                AppEditCategoryCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{cat.Name} (Standard-Idle: {cat.DefaultIdleThresholdSeconds / 60.0:0.#} Min.)",
                    Tag = cat.Id
                });
                if (cat.Id == detail.Application.CategoryId)
                {
                    selectedIdx = i;
                }
            }
            AppEditCategoryCombo.SelectedIndex = selectedIdx;

            AppEditCustomIdleMinutesBox.Text = detail.Application.CustomIdleThresholdSeconds.HasValue
                ? (detail.Application.CustomIdleThresholdSeconds.Value / 60.0).ToString("0.#", CultureInfo.InvariantCulture)
                : string.Empty;

            AppDiagIdentifierText.Text = $"Stabiler App-Identifikator: {detail.Application.AppIdentifier}";
            AppDiagProcessText.Text = $"Prozessname: {detail.Application.ProcessName}";
            AppDiagPathText.Text = $"Ausführbarer Pfad: {detail.Application.ExecutablePath ?? "Nicht verfügbar"}";
        }
        finally
        {
            _isInitializingUi = false;
        }
    }

    // =========================================================================
    // 4. KATEGORIEN & DRILL-DOWN RENDERING
    // =========================================================================
    private void RenderCategoriesUi()
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        var cats = _viewModel.GetRangeCategories(_viewModel.AppsAndCategoriesRange);
        CategoriesRangeTitleText.Text = _viewModel.AppsAndCategoriesRange switch
        {
            HistoryPeriodMode.Today => "ANTEILE DER KATEGORIEN (HEUTE)",
            HistoryPeriodMode.Last7Days => "ANTEILE DER KATEGORIEN (LETZTE 7 TAGE)",
            _ => "ANTEILE DER KATEGORIEN (LETZTE 30 TAGE)"
        };

        RenderCategoryStackedBar(CategoriesViewStackedBar, cats, isDark);

        if (_viewModel.SelectedCategoryDetail is null && cats.Count > 0)
        {
            _viewModel.SelectCategory(cats[0].CategoryId);
        }

        PopulateCategoryListPanel(CategoriesMasterListPanel, cats, onCatClick: catId =>
        {
            _viewModel.SelectCategory(catId);
            RenderCategoriesUi();
        }, selectedCategoryId: _viewModel.SelectedCategoryDetail?.CategoryId, includeZeroCategories: true);

        RenderSelectedCategoryDetailPane();
    }

    private void RenderSelectedCategoryDetailPane()
    {
        var cat = _viewModel.SelectedCategoryDetail;
        if (cat is null)
        {
            return;
        }

        CategoryDetailTitleText.Text = cat.CategoryName;
        CategoryDetailSubtitleText.Text = $"{cat.FormattedPercentage} der gesamten aktiven Nutzungszeit • {cat.Apps.Count} {(cat.Apps.Count == 1 ? "Eintrag" : "Einträge")}";
        CategoryDetailTotalText.Text = cat.FormattedDuration;
        CategoryDetailIdleMinutesBox.Text = (cat.DefaultIdleThresholdSeconds / 60.0).ToString("0.#", CultureInfo.InvariantCulture);

        bool isCustom = cat.CategoryKey.StartsWith("custom_", StringComparison.OrdinalIgnoreCase);
        DeleteCustomCategoryButton.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        // Befülle die Auswahlliste bereits erkannter PC-Programme
        _isInitializingUi = true;
        try
        {
            ExistingPcProgramCombo.Items.Clear();
            foreach (var app in _viewModel.GetAllTrackedApplications().Where(a => !a.IsWebsite))
            {
                ExistingPcProgramCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{app.DisplayName} ({app.ProcessName})",
                    Tag = app
                });
            }
        }
        finally
        {
            _isInitializingUi = false;
        }

        PopulateAppListPanel(CategoryDetailAppsPanel, cat.Apps, onAppClick: appId =>
        {
            _viewModel.SelectApp(appId);
            SwitchToSection("apps");
        });
    }

    // =========================================================================
    // 5. EINSTELLUNGEN & DIAGNOSE RENDERING
    // =========================================================================
    private void RenderSettingsUi()
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        SyncSettingsControlsFromModel();

        SettingsCategoryThresholdsPanel.Children.Clear();
        foreach (var cat in _viewModel.AllCategories)
        {
            var rowBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
            };

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = ColorHelperEx.GetCategoryBrush(cat.Id, cat.ColorHex, isDark)
            };
            badge.Child = new FontIcon
            {
                Glyph = cat.IconGlyph,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            leftStack.Children.Add(badge);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = cat.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });
            titleStack.Children.Add(new TextBlock
            {
                Text = $"Aktueller Schwellwert: {cat.DefaultIdleThresholdSeconds / 60.0:0.#} Minuten ({cat.DefaultIdleThresholdSeconds} Sek.)",
                FontSize = 11,
                Opacity = 0.7
            });
            leftStack.Children.Add(titleStack);
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            var minBox = new TextBox
            {
                Text = (cat.DefaultIdleThresholdSeconds / 60.0).ToString("0.#", CultureInfo.InvariantCulture),
                PlaceholderText = "Minuten",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(minBox, 1);
            grid.Children.Add(minBox);

            long catId = cat.Id;
            var saveBtn = new Button
            {
                Content = "Übernehmen",
                VerticalAlignment = VerticalAlignment.Center
            };
            saveBtn.Click += (_, _) =>
            {
                if (double.TryParse(minBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double mins) && mins > 0)
                {
                    _viewModel.UpdateCategoryIdleThreshold(catId, (int)Math.Round(mins * 60));
                    RenderSettingsUi();
                }
            };
            Grid.SetColumn(saveBtn, 2);
            grid.Children.Add(saveBtn);

            rowBorder.Child = grid;
            SettingsCategoryThresholdsPanel.Children.Add(rowBorder);
        }

        DiagLogTextBlock.Text = string.Join(Environment.NewLine, _viewModel.GetRecentLogEntries());
    }

    private void SyncSettingsControlsFromModel()
    {
        _isInitializingUi = true;
        try
        {
            var s = _viewModel.Settings;
            ThemeRadioSystem.IsChecked = s.Theme == ThemePreference.System;
            ThemeRadioLight.IsChecked = s.Theme == ThemePreference.Light;
            ThemeRadioDark.IsChecked = s.Theme == ThemePreference.Dark;

            SettingsLaunchAtStartupToggle.IsOn = s.LaunchAtStartup;
            SettingsMinimizeToTrayToggle.IsOn = s.MinimizeToTrayOnClose;
            SettingsGlobalIdleSecondsBox.Text = s.GlobalIdleThresholdSeconds.ToString(CultureInfo.InvariantCulture);
            SettingsRetentionDaysBox.Text = s.DataRetentionDays.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _isInitializingUi = false;
        }
    }

    // =========================================================================
    // DIAGRAMM- & LISTEN-BAUSTEINE (System-Farbpalette)
    // =========================================================================
    private static void RenderCategoryStackedBar(Grid targetGrid, IReadOnlyList<CategoryUsageSummary> categories, bool isDark)
    {
        targetGrid.Children.Clear();
        targetGrid.ColumnDefinitions.Clear();

        var activeCats = categories.Where(c => c.TotalDuration.TotalSeconds > 0).ToList();
        if (activeCats.Count == 0)
        {
            targetGrid.Children.Add(new Border
            {
                Background = ColorHelperEx.GetMutedTrackBrush(isDark),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            });
            return;
        }

        for (int i = 0; i < activeCats.Count; i++)
        {
            var cat = activeCats[i];
            double weight = Math.Max(1.0, cat.TotalDuration.TotalSeconds);
            targetGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(weight, GridUnitType.Star)
            });

            double leftRadius = i == 0 ? 6 : 0;
            double rightRadius = i == activeCats.Count - 1 ? 6 : 0;
            var seg = new Border
            {
                Background = ColorHelperEx.GetCategoryBrush(cat.CategoryId, cat.ColorHex, isDark),
                CornerRadius = new CornerRadius(leftRadius, rightRadius, rightRadius, leftRadius),
                Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0)
            };
            ToolTipService.SetToolTip(seg, $"{cat.CategoryName}: {cat.FormattedDuration} ({cat.FormattedPercentage})");
            Grid.SetColumn(seg, i);
            targetGrid.Children.Add(seg);
        }
    }

    private void RenderDailyBarChart(
        Grid chartGrid,
        IReadOnlyList<DailyUsagePoint> points,
        TimeSpan referenceAverage,
        DateOnly? highlightDate,
        bool compactLabels)
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        chartGrid.Children.Clear();
        chartGrid.ColumnDefinitions.Clear();
        chartGrid.RowDefinitions.Clear();

        if (points.Count == 0)
        {
            return;
        }

        chartGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        chartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double maxSeconds = Math.Max(
            points.Max(p => p.TotalDuration.TotalSeconds),
            referenceAverage.TotalSeconds);
        if (maxSeconds < 60)
        {
            maxSeconds = 3600; // Mindestens 1h Skala bei leerem Diagramm
        }

        var barsHost = new Grid { ColumnSpacing = compactLabels ? 3 : 8 };
        for (int i = 0; i < points.Count; i++)
        {
            barsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var pt = points[i];
            double ratio = Math.Clamp(pt.TotalDuration.TotalSeconds / maxSeconds, 0.0, 1.0);
            bool isSelected = highlightDate.HasValue && pt.Date == highlightDate.Value;

            var colStack = new Grid();
            colStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            colStack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });

            var barWrapper = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(0, 4, 0, 0)
            };

            Brush barBrush = isSelected
                ? ColorHelperEx.GetPrimaryAccentBrush(isDark)
                : pt.TotalDuration.TotalSeconds > 0
                    ? ColorHelperEx.GetSecondaryAccentBrush(isDark)
                    : ColorHelperEx.GetMutedTrackBrush(isDark);

            var barVisual = new Border
            {
                Height = Math.Max(4, ratio * 115),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(4, 4, 2, 2),
                Background = barBrush
            };

            barWrapper.Child = barVisual;
            ToolTipService.SetToolTip(barWrapper, $"{pt.FullDateLabel}: {pt.FormattedDuration}");

            DateOnly clickedDate = pt.Date;
            barWrapper.PointerPressed += (_, _) =>
            {
                _viewModel.SelectDay(clickedDate);
                if (DashboardView.Visibility == Visibility.Visible)
                {
                    SwitchToSection("history");
                }
                else
                {
                    RenderHistoryUi();
                }
            };

            Grid.SetRow(barWrapper, 0);
            colStack.Children.Add(barWrapper);

            bool showLabel = !compactLabels || (i % 5 == 0) || (i == points.Count - 1) || isSelected;
            if (showLabel)
            {
                var lbl = new TextBlock
                {
                    Text = pt.ShortDayLabel,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = isSelected ? 1.0 : 0.65,
                    FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal
                };
                Grid.SetRow(lbl, 1);
                colStack.Children.Add(lbl);
            }

            Grid.SetColumn(colStack, i);
            barsHost.Children.Add(colStack);
        }

        Grid.SetRow(barsHost, 0);
        chartGrid.Children.Add(barsHost);
    }

    private static void RenderHourlyChart(Grid chartGrid, IReadOnlyList<HourlyUsageBucket> buckets, bool isDark)
    {
        chartGrid.Children.Clear();
        chartGrid.ColumnDefinitions.Clear();

        double maxMinutes = Math.Max(15.0, buckets.Count > 0 ? buckets.Max(b => b.Minutes) : 15.0);

        for (int i = 0; i < buckets.Count; i++)
        {
            chartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var b = buckets[i];
            double ratio = Math.Clamp(b.Minutes / maxMinutes, 0.0, 1.0);

            var cell = new Grid { Margin = new Thickness(1, 0, 1, 0) };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

            var bar = new Border
            {
                Height = Math.Max(3, ratio * 75),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(3, 3, 1, 1),
                Background = b.Minutes > 0
                    ? ColorHelperEx.GetPrimaryAccentBrush(isDark, 225)
                    : ColorHelperEx.GetMutedTrackBrush(isDark)
            };
            ToolTipService.SetToolTip(bar, $"{b.Label} Uhr: {AppUsageSummary.FormatDuration(b.Duration)}");
            Grid.SetRow(bar, 0);
            cell.Children.Add(bar);

            if (i % 3 == 0)
            {
                var lbl = new TextBlock
                {
                    Text = $"{b.Hour:00}",
                    FontSize = 10,
                    Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(lbl, 1);
                cell.Children.Add(lbl);
            }

            Grid.SetColumn(cell, i);
            chartGrid.Children.Add(cell);
        }
    }

    private void PopulateAppListPanel(
        StackPanel targetPanel,
        IReadOnlyList<AppUsageSummary> apps,
        Action<long> onAppClick,
        long? selectedAppId = null)
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        targetPanel.Children.Clear();

        if (apps.Count == 0)
        {
            targetPanel.Children.Add(new TextBlock
            {
                Text = "Keine aktiven Anwendungen im gewählten Zeitraum erfasst.",
                FontSize = 13,
                Opacity = 0.6,
                Margin = new Thickness(0, 6, 0, 6)
            });
            return;
        }

        foreach (var app in apps)
        {
            bool isSelected = selectedAppId.HasValue && selectedAppId.Value == app.ApplicationId;
            Brush defaultBg = isSelected
                ? ColorHelperEx.GetPrimaryAccentBrush(isDark, 65)
                : (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            Brush defaultBorder = isSelected
                ? ColorHelperEx.GetPrimaryAccentBrush(isDark, 230)
                : new SolidColorBrush(Colors.Transparent);

            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Background = defaultBg,
                BorderBrush = defaultBorder,
                BorderThickness = new Thickness(isSelected ? 2 : 1)
            };

            var rowGrid = new Grid { ColumnSpacing = 12 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // App-Icon oder Fallback-Symbol
            var iconHolder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = ColorHelperEx.GetCategoryBrush(app.CategoryId, app.CategoryColorHex, isDark, 42),
                VerticalAlignment = VerticalAlignment.Center
            };

            bool iconLoaded = false;
            if (!string.IsNullOrWhiteSpace(app.IconCachePath) && File.Exists(app.IconCachePath))
            {
                try
                {
                    iconHolder.Child = new Image
                    {
                        Source = new BitmapImage(new Uri(app.IconCachePath)),
                        Width = 26,
                        Height = 26,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    iconLoaded = true;
                }
                catch
                {
                    iconLoaded = false;
                }
            }

            if (!iconLoaded)
            {
                iconHolder.Child = new FontIcon
                {
                    Glyph = app.CategoryIconGlyph,
                    FontSize = 16,
                    Foreground = ColorHelperEx.GetCategoryBrush(app.CategoryId, app.CategoryColorHex, isDark),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            Grid.SetColumn(iconHolder, 0);
            rowGrid.Children.Add(iconHolder);

            // Mitte: Anzeigename + Kategorie + Fortschrittsbalken
            var midStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock
            {
                Text = app.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = $"• {app.CategoryName}",
                FontSize = 12,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center
            });
            midStack.Children.Add(titleRow);

            var barBg = new Border
            {
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = ColorHelperEx.GetMutedTrackBrush(isDark)
            };
            var barGrid = new Grid();
            double pct = Math.Clamp(app.PercentageOfTotal, 0, 100);
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, pct), GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 100 - pct), GridUnitType.Star) });

            var barFill = new Border
            {
                CornerRadius = new CornerRadius(2.5),
                Background = ColorHelperEx.GetCategoryBrush(app.CategoryId, app.CategoryColorHex, isDark)
            };
            Grid.SetColumn(barFill, 0);
            barGrid.Children.Add(barFill);
            barBg.Child = barGrid;
            midStack.Children.Add(barBg);

            Grid.SetColumn(midStack, 1);
            rowGrid.Children.Add(midStack);

            // Rechts: Nutzungszeit & Prozentanteil
            var rightStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            rightStack.Children.Add(new TextBlock
            {
                Text = app.FormattedDuration,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            rightStack.Children.Add(new TextBlock
            {
                Text = app.FormattedPercentage,
                FontSize = 11,
                Opacity = 0.65,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            Grid.SetColumn(rightStack, 2);
            rowGrid.Children.Add(rightStack);

            card.Child = rowGrid;
            long id = app.ApplicationId;
            card.PointerEntered += (_, _) =>
            {
                if (!isSelected)
                {
                    card.Background = ColorHelperEx.GetPrimaryAccentBrush(isDark, 28);
                    card.BorderBrush = ColorHelperEx.GetPrimaryAccentBrush(isDark, 110);
                }
            };
            card.PointerExited += (_, _) =>
            {
                card.Background = defaultBg;
                card.BorderBrush = defaultBorder;
            };
            card.PointerPressed += (_, _) =>
            {
                card.Background = ColorHelperEx.GetPrimaryAccentBrush(isDark, 75);
                card.BorderBrush = ColorHelperEx.GetPrimaryAccentBrush(isDark, 235);
                card.BorderThickness = new Thickness(2);
                onAppClick(id);
            };
            targetPanel.Children.Add(card);
        }
    }

    private void PopulateCategoryListPanel(
        StackPanel targetPanel,
        IReadOnlyList<CategoryUsageSummary> categories,
        Action<long> onCatClick,
        long? selectedCategoryId = null,
        bool includeZeroCategories = false)
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        targetPanel.Children.Clear();
        var list = includeZeroCategories
            ? categories
            : categories.Where(c => c.TotalDuration.TotalSeconds > 0).ToList();

        if (list.Count == 0)
        {
            targetPanel.Children.Add(new TextBlock
            {
                Text = "Noch keine Kategorie-Nutzung im gewählten Zeitraum.",
                FontSize = 13,
                Opacity = 0.6,
                Margin = new Thickness(0, 6, 0, 6)
            });
            return;
        }

        foreach (var cat in list)
        {
            bool isSelected = selectedCategoryId.HasValue && selectedCategoryId.Value == cat.CategoryId;
            Brush defaultBg = isSelected
                ? ColorHelperEx.GetCategoryBrush(cat.CategoryId, cat.ColorHex, isDark, 68)
                : (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
            Brush defaultBorder = isSelected
                ? ColorHelperEx.GetCategoryBrush(cat.CategoryId, cat.ColorHex, isDark, 235)
                : new SolidColorBrush(Colors.Transparent);

            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 10),
                Background = defaultBg,
                BorderBrush = defaultBorder,
                BorderThickness = new Thickness(isSelected ? 2 : 1)
            };

            var rowGrid = new Grid { ColumnSpacing = 12 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBox = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = ColorHelperEx.GetCategoryBrush(cat.CategoryId, cat.ColorHex, isDark),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon
                {
                    Glyph = cat.IconGlyph,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(iconBox, 0);
            rowGrid.Children.Add(iconBox);

            var midStack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            midStack.Children.Add(new TextBlock
            {
                Text = cat.CategoryName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            });
            midStack.Children.Add(new TextBlock
            {
                Text = $"{cat.Apps.Count} {(cat.Apps.Count == 1 ? "App" : "Apps")} • Idle-Limit: {cat.DefaultIdleThresholdSeconds / 60.0:0.#} Min.",
                FontSize = 11,
                Opacity = 0.65
            });
            Grid.SetColumn(midStack, 1);
            rowGrid.Children.Add(midStack);

            var rightStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            rightStack.Children.Add(new TextBlock
            {
                Text = cat.FormattedDuration,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            rightStack.Children.Add(new TextBlock
            {
                Text = cat.FormattedPercentage,
                FontSize = 11,
                Opacity = 0.65,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            Grid.SetColumn(rightStack, 2);
            rowGrid.Children.Add(rightStack);

            card.Child = rowGrid;
            long id = cat.CategoryId;
            string hex = cat.ColorHex;
            card.PointerEntered += (_, _) =>
            {
                if (!isSelected)
                {
                    card.Background = ColorHelperEx.GetCategoryBrush(id, hex, isDark, 30);
                    card.BorderBrush = ColorHelperEx.GetCategoryBrush(id, hex, isDark, 120);
                }
            };
            card.PointerExited += (_, _) =>
            {
                card.Background = defaultBg;
                card.BorderBrush = defaultBorder;
            };
            card.PointerPressed += (_, _) =>
            {
                card.Background = ColorHelperEx.GetCategoryBrush(id, hex, isDark, 75);
                card.BorderBrush = ColorHelperEx.GetCategoryBrush(id, hex, isDark, 240);
                card.BorderThickness = new Thickness(2);
                onCatClick(id);
            };
            targetPanel.Children.Add(card);
        }
    }

    // =========================================================================
    // EVENT HANDLERS
    // =========================================================================
    private void OnJumpToMonthHistoryClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.HistoryMode = HistoryPeriodMode.Last30Days;
        SwitchToSection("history");
    }

    private void OnJumpToSixWeeksClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.HistoryMode = HistoryPeriodMode.SixWeeks;
        SwitchToSection("history");
    }

    private void OnJumpToWeekHistoryClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.HistoryMode = HistoryPeriodMode.Last7Days;
        SwitchToSection("history");
    }

    private void OnJumpToAppsClicked(object sender, RoutedEventArgs e) => SwitchToSection("apps");

    private void OnJumpToCategoriesClicked(object sender, RoutedEventArgs e) => SwitchToSection("categories");

    private void OnJumpToSettingsClicked(object sender, RoutedEventArgs e) => SwitchToSection("settings");

    private void OnToggleWidgetClicked(object sender, RoutedEventArgs e) => ToggleDesktopWidget();

    private void OnHistoryPeriodModeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _viewModel.HistoryMode = tag switch
            {
                "Week" => HistoryPeriodMode.Last7Days,
                "SixWeeks" => HistoryPeriodMode.SixWeeks,
                _ => HistoryPeriodMode.Last30Days
            };
            RenderHistoryUi();
        }
    }

    private void OnPrevDayClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectDay(_viewModel.SelectedDate.AddDays(-1));
        RenderHistoryUi();
    }

    private void OnTodayDayClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectDay(DateOnly.FromDateTime(DateTime.Now));
        RenderHistoryUi();
    }

    private void OnNextDayClicked(object sender, RoutedEventArgs e)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly next = _viewModel.SelectedDate.AddDays(1);
        if (next <= today)
        {
            _viewModel.SelectDay(next);
            RenderHistoryUi();
        }
    }

    private void OnAppsRangeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _viewModel.AppsAndCategoriesRange = tag switch
            {
                "Today" => HistoryPeriodMode.Today,
                "Week" => HistoryPeriodMode.Last7Days,
                _ => HistoryPeriodMode.Last30Days
            };
            RenderAppsUi();
        }
    }

    private void OnAppSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.AppSearchQuery = AppSearchBox.Text;
        RenderAppsUi();
    }

    private void OnSaveAppCustomizationClicked(object sender, RoutedEventArgs e)
    {
        var detail = _viewModel.SelectedAppDetail;
        if (detail is null)
        {
            return;
        }

        string newName = string.IsNullOrWhiteSpace(AppEditDisplayNameBox.Text)
            ? detail.Application.DisplayName
            : AppEditDisplayNameBox.Text.Trim();

        long categoryId = detail.Application.CategoryId;
        if (AppEditCategoryCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is long cid)
        {
            categoryId = cid;
        }

        int? customIdleSeconds = null;
        if (!string.IsNullOrWhiteSpace(AppEditCustomIdleMinutesBox.Text) &&
            double.TryParse(AppEditCustomIdleMinutesBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double mins) &&
            mins > 0)
        {
            customIdleSeconds = (int)Math.Round(mins * 60);
        }

        _viewModel.SaveAppCustomization(detail.Application.Id, newName, categoryId, customIdleSeconds);
        RenderAppsUi();
    }

    private void OnCategoriesRangeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _viewModel.AppsAndCategoriesRange = tag switch
            {
                "Today" => HistoryPeriodMode.Today,
                "Week" => HistoryPeriodMode.Last7Days,
                _ => HistoryPeriodMode.Last30Days
            };
            RenderCategoriesUi();
        }
    }

    private void OnSaveCategoryThresholdClicked(object sender, RoutedEventArgs e)
    {
        var cat = _viewModel.SelectedCategoryDetail;
        if (cat is null)
        {
            return;
        }

        if (double.TryParse(CategoryDetailIdleMinutesBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double mins) && mins > 0)
        {
            _viewModel.UpdateCategoryIdleThreshold(cat.CategoryId, (int)Math.Round(mins * 60));
            RenderCategoriesUi();
        }
    }

    private void InitializeNewCategoryRgbPalette()
    {
        if (NewCategoryRgbPaletteGrid is null)
        {
            return;
        }

        // 16 harmonische RGB-Farben (2 Reihen x 8 Spalten) zum direkten Anklicken ohne Dropdown-Popup
        (byte R, byte G, byte B, string Name)[] rgbPalette =
        {
            (0, 120, 212, "Blau"),
            (0, 183, 195, "Cyan"),
            (16, 137, 62, "Grün"),
            (0, 204, 106, "Smaragd"),
            (255, 185, 0, "Goldgelb"),
            (255, 140, 0, "Orange"),
            (232, 17, 35, "Rot"),
            (227, 0, 140, "Magenta"),
            (136, 23, 152, "Violett"),
            (116, 77, 169, "Lavendel"),
            (73, 130, 5, "Limette"),
            (3, 131, 135, "Petrol"),
            (202, 80, 16, "Kupfer"),
            (86, 124, 115, "Salbei"),
            (105, 121, 126, "Schiefer"),
            (74, 84, 89, "Anthrazit")
        };

        NewCategoryRgbPaletteGrid.Children.Clear();
        NewCategoryRgbPaletteGrid.ColumnDefinitions.Clear();
        NewCategoryRgbPaletteGrid.RowDefinitions.Clear();

        for (int c = 0; c < 8; c++)
        {
            NewCategoryRgbPaletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        NewCategoryRgbPaletteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        NewCategoryRgbPaletteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < rgbPalette.Length; i++)
        {
            var item = rgbPalette[i];
            var swatch = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, item.R, item.G, item.B)),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(2)
            };
            ToolTipService.SetToolTip(swatch, $"{item.Name} (RGB: {item.R}, {item.G}, {item.B})");

            byte r = item.R;
            byte g = item.G;
            byte b = item.B;
            swatch.PointerPressed += (_, _) => SetNewCategoryRgbColor(r, g, b);

            Grid.SetRow(swatch, i / 8);
            Grid.SetColumn(swatch, i % 8);
            NewCategoryRgbPaletteGrid.Children.Add(swatch);
        }

        // Standardmäßig mit der aktuellen System-Akzentfarbe des PCs initialisieren
        var sysAccent = ColorHelperEx.GetSystemAccentColor();
        SetNewCategoryRgbColor(sysAccent.R, sysAccent.G, sysAccent.B);
    }

    private void SetNewCategoryRgbColor(byte r, byte g, byte b)
    {
        _isInitializingUi = true;
        try
        {
            if (NewCategoryRSlider is not null) NewCategoryRSlider.Value = r;
            if (NewCategoryGSlider is not null) NewCategoryGSlider.Value = g;
            if (NewCategoryBSlider is not null) NewCategoryBSlider.Value = b;
        }
        finally
        {
            _isInitializingUi = false;
        }

        UpdateNewCategoryRgbPreview(r, g, b);
    }

    private void OnNewCategoryRgbSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializingUi || NewCategoryRSlider is null || NewCategoryGSlider is null || NewCategoryBSlider is null)
        {
            return;
        }

        byte r = (byte)Math.Clamp((int)Math.Round(NewCategoryRSlider.Value), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(NewCategoryGSlider.Value), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(NewCategoryBSlider.Value), 0, 255);
        UpdateNewCategoryRgbPreview(r, g, b);
    }

    private void UpdateNewCategoryRgbPreview(byte r, byte g, byte b)
    {
        if (NewCategoryRValueText is not null) NewCategoryRValueText.Text = r.ToString(CultureInfo.InvariantCulture);
        if (NewCategoryGValueText is not null) NewCategoryGValueText.Text = g.ToString(CultureInfo.InvariantCulture);
        if (NewCategoryBValueText is not null) NewCategoryBValueText.Text = b.ToString(CultureInfo.InvariantCulture);

        string hex = $"#{r:X2}{g:X2}{b:X2}";
        if (NewCategoryColorPreviewBorder is not null)
        {
            NewCategoryColorPreviewBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
        if (NewCategoryColorHexText is not null)
        {
            NewCategoryColorHexText.Text = $"{hex} (RGB: {r}, {g}, {b})";
        }
    }

    private string GetSelectedNewCategoryHexColor()
    {
        byte r = NewCategoryRSlider is not null ? (byte)Math.Clamp((int)Math.Round(NewCategoryRSlider.Value), 0, 255) : (byte)0;
        byte g = NewCategoryGSlider is not null ? (byte)Math.Clamp((int)Math.Round(NewCategoryGSlider.Value), 0, 255) : (byte)120;
        byte b = NewCategoryBSlider is not null ? (byte)Math.Clamp((int)Math.Round(NewCategoryBSlider.Value), 0, 255) : (byte)212;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private void OnCreateCustomCategoryClicked(object sender, RoutedEventArgs e)
    {
        string name = NewCategoryNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            CategoryCreateFeedbackText.Text = "Bitte einen Namen für die Kategorie eingeben.";
            return;
        }

        double mins = 10;
        if (!string.IsNullOrWhiteSpace(NewCategoryIdleMinutesBox.Text))
        {
            double.TryParse(NewCategoryIdleMinutesBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out mins);
        }
        int idleSeconds = Math.Max(15, (int)Math.Round(Math.Max(0.5, mins) * 60));

        string colorHex = GetSelectedNewCategoryHexColor();

        var created = _viewModel.CreateCustomCategory(name, colorHex, "\uE8FD", idleSeconds);
        NewCategoryNameBox.Text = string.Empty;
        CategoryCreateFeedbackText.Text = $"✓ Kategorie '{created.Name}' ({mins:0.#} Min. Schwellwert, {colorHex}) angelegt!";
        RenderCategoriesUi();
        RenderSettingsUi();
    }

    private void OnDeleteCustomCategoryClicked(object sender, RoutedEventArgs e)
    {
        var cat = _viewModel.SelectedCategoryDetail;
        if (cat is null || !cat.CategoryKey.StartsWith("custom_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _viewModel.DeleteCustomCategory(cat.CategoryId);
        CategoryCreateFeedbackText.Text = $"✓ Kategorie '{cat.CategoryName}' wurde gelöscht.";
        RenderCategoriesUi();
        RenderSettingsUi();
    }

    private void OnCategoryEntryTypeRadioChanged(object sender, RoutedEventArgs e)
    {
        if (AddProgramEntryPanel is null || AddWebsiteEntryPanel is null)
        {
            return;
        }

        bool isProgram = EntryTypeProgramRadio.IsChecked == true;
        AddProgramEntryPanel.Visibility = isProgram ? Visibility.Visible : Visibility.Collapsed;
        AddWebsiteEntryPanel.Visibility = isProgram ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnExistingPcProgramComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingUi)
        {
            return;
        }

        if (ExistingPcProgramCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is TrackedApplication app)
        {
            ProgramPathOrProcessBox.Text = !string.IsNullOrWhiteSpace(app.ExecutablePath) ? app.ExecutablePath : app.ProcessName;
            ProgramFriendlyNameBox.Text = app.DisplayName;
        }
    }

    private void OnBrowseProgramForCategoryClicked(object sender, RoutedEventArgs e)
    {
        string? file = WindowIconHelper.PickExecutableFile(_hwnd);
        if (!string.IsNullOrWhiteSpace(file))
        {
            ProgramPathOrProcessBox.Text = file;
            if (string.IsNullOrWhiteSpace(ProgramFriendlyNameBox.Text))
            {
                ProgramFriendlyNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(file);
            }
        }
    }

    private void OnAddProgramToCategoryClicked(object sender, RoutedEventArgs e)
    {
        var cat = _viewModel.SelectedCategoryDetail;
        if (cat is null)
        {
            return;
        }

        string exeOrProc = ProgramPathOrProcessBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(exeOrProc))
        {
            CategoryEntryAddedFeedbackText.Text = "Bitte ein Programm (.exe) oder einen Prozessnamen auswählen.";
            return;
        }

        string displayName = ProgramFriendlyNameBox.Text.Trim();
        var added = _viewModel.AddProgramToCategory(cat.CategoryId, displayName, exeOrProc);
        ProgramPathOrProcessBox.Text = string.Empty;
        ProgramFriendlyNameBox.Text = string.Empty;
        CategoryEntryAddedFeedbackText.Text = $"✓ Programm '{added.DisplayName}' zu '{cat.CategoryName}' hinzugefügt!";
        RenderCategoriesUi();
        RenderAppsUi();
    }

    private void OnAddWebsiteToCategoryClicked(object sender, RoutedEventArgs e)
    {
        var cat = _viewModel.SelectedCategoryDetail;
        if (cat is null)
        {
            return;
        }

        string url = WebsiteUrlInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            CategoryEntryAddedFeedbackText.Text = "Bitte eine Webseiten-URL oder Domain (z. B. youtube.com) eingeben.";
            return;
        }

        string displayName = WebsiteDisplayNameInputBox.Text.Trim();
        var added = _viewModel.AddWebsiteToCategory(cat.CategoryId, displayName, url);
        WebsiteUrlInputBox.Text = string.Empty;
        WebsiteDisplayNameInputBox.Text = string.Empty;
        CategoryEntryAddedFeedbackText.Text = $"✓ Webseite '{added.DisplayName}' ({added.ProcessName}) zu '{cat.CategoryName}' hinzugefügt!";
        RenderCategoriesUi();
        RenderAppsUi();
    }

    private void OnOpenGithubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = MainViewModel.GithubUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore browser launch errors
        }
    }

    private void OnThemeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializingUi || sender is not RadioButton rb || rb.Tag is not string tag)
        {
            return;
        }

        ThemePreference pref = tag switch
        {
            "Light" => ThemePreference.Light,
            "Dark" => ThemePreference.Dark,
            _ => ThemePreference.System
        };
        _viewModel.UpdateTheme(pref);
    }

    private void OnSaveGeneralSettingsClicked(object sender, RoutedEventArgs e)
    {
        int globalIdle = int.TryParse(SettingsGlobalIdleSecondsBox.Text, out int gi) ? gi : 60;
        int retention = int.TryParse(SettingsRetentionDaysBox.Text, out int rd) ? rd : 365;

        _viewModel.SaveGeneralSettings(
            globalIdle,
            SettingsLaunchAtStartupToggle.IsOn,
            SettingsMinimizeToTrayToggle.IsOn,
            retention);

        SettingsSavedFeedbackText.Text = "✓ Gespeichert";
        RenderSettingsUi();
    }

    private void OnGenerateDemoDataClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.GenerateSixWeekDemoData();
        SettingsSavedFeedbackText.Text = "✓ 6-Wochen-Beispieldaten generiert";
        RefreshAllViewsUi();
    }

    private void OnClearAllDataClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearAllUsageData();
        SettingsSavedFeedbackText.Text = "✓ Nutzungsdaten zurückgesetzt";
        RefreshAllViewsUi();
    }

    private static string FormatSecondsShort(int seconds)
    {
        if (seconds >= 60)
        {
            return $"{seconds / 60.0:0.#} Min.";
        }
        return $"{seconds} Sek.";
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
