using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenTime.App.Helpers;
using ScreenTime.App.ViewModels;
using ScreenTime.Core.Models;
using WinRT.Interop;

namespace ScreenTime.App;

public sealed partial class WidgetWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const nint HTCAPTION = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private readonly MainViewModel _viewModel;
    private readonly Action _onOpenMainRequested;
    private nint _hwnd;
    private AppWindow? _appWindow;
    private OverlappedPresenter? _presenter;

    public WidgetWindow(MainViewModel viewModel, Action onOpenMainRequested)
    {
        this.InitializeComponent();
        _viewModel = viewModel;
        _onOpenMainRequested = onOpenMainRequested;

        ConfigureBorderlessWidgetWindow();
        ApplyTheme(_viewModel.Settings.Theme);
        _viewModel.ThemeChanged += (_, theme) => ApplyTheme(theme);

        RefreshWidgetUi();
    }

    private void ConfigureBorderlessWidgetWindow()
    {
        try
        {
            _hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            WindowIconHelper.ApplyWindowIcon(_hwnd, _appWindow, _viewModel.Settings.Theme);

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                _presenter = presenter;
                _presenter.IsMaximizable = false;
                _presenter.IsMinimizable = false;
                _presenter.IsResizable = false;
                _presenter.IsAlwaysOnTop = true;
                _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            }

            // Entferne zusätzlich den nativen Fensterrahmen/Caption über Win32 und runde die Ecken via DWM
            int style = GetWindowLongW(_hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU);
            SetWindowLongW(_hwnd, GWL_STYLE, style);
            SetWindowPos(_hwnd, nint.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            _appWindow.Resize(new Windows.Graphics.SizeInt32(330, 295));

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea is not null)
            {
                int x = Math.Max(20, displayArea.WorkArea.Width - 350);
                int y = 24;
                _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }

            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            }
        }
        catch
        {
            // Ignore presenter customization errors
        }
    }

    public void ApplyTheme(ThemePreference preference)
    {
        WidgetRootGrid.RequestedTheme = preference switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        WindowIconHelper.ApplyWindowIcon(_hwnd, _appWindow, preference);
        RefreshWidgetUi();
    }

    public void RefreshWidgetUi()
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(_viewModel.Settings.Theme);
        var today = _viewModel.TodayStatistics;
        var status = _viewModel.LiveStatus;

        WidgetBrandBadge.Background = ColorHelperEx.GetPrimaryAccentBrush(isDark);
        WidgetTodayTotalText.Text = today.FormattedTotalDuration;
        WidgetComparisonText.Text = $"6-Wochen-Ø: {today.FormattedSixWeekAverage}/Tag";

        if (status.IsSessionLockedOrSuspended)
        {
            WidgetLiveDot.Fill = ColorHelperEx.GetSecondaryAccentBrush(isDark);
            WidgetLiveAppText.Text = "Sitzung gesperrt / Standby";
        }
        else if (status.IsUserIdle)
        {
            WidgetLiveDot.Fill = ColorHelperEx.GetSecondaryAccentBrush(isDark);
            WidgetLiveAppText.Text = $"Leerlauf ({status.CurrentAppDisplayName})";
        }
        else
        {
            WidgetLiveDot.Fill = ColorHelperEx.GetPrimaryAccentBrush(isDark);
            string streamSuffix = status.IsMediaOrStreamingActive ? " • Stream aktiv" : "";
            WidgetLiveAppText.Text = $"{status.CurrentAppDisplayName} ({status.CurrentCategoryName}{streamSuffix})";
        }

        // Gestapelter Kategorien-Balken in System-Palette
        WidgetCategoryBarGrid.Children.Clear();
        WidgetCategoryBarGrid.ColumnDefinitions.Clear();
        var activeCats = today.Categories.Where(c => c.TotalDuration.TotalSeconds > 0).ToList();
        for (int i = 0; i < activeCats.Count; i++)
        {
            var cat = activeCats[i];
            WidgetCategoryBarGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(1, cat.TotalDuration.TotalSeconds), GridUnitType.Star)
            });
            var seg = new Border
            {
                Background = ColorHelperEx.GetCategoryBrush(cat.CategoryId, cat.ColorHex, isDark),
                Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0)
            };
            Grid.SetColumn(seg, i);
            WidgetCategoryBarGrid.Children.Add(seg);
        }

        // Top 3 Apps in System-Palette
        WidgetTopAppsPanel.Children.Clear();
        foreach (var app in today.Apps.Take(3))
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = ColorHelperEx.GetCategoryBrush(app.CategoryId, app.CategoryColorHex, isDark),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);
            row.Children.Add(dot);

            var nameTxt = new TextBlock
            {
                Text = app.DisplayName,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameTxt, 1);
            row.Children.Add(nameTxt);

            var durTxt = new TextBlock
            {
                Text = app.FormattedDuration,
                FontSize = 11,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(durTxt, 2);
            row.Children.Add(durTxt);

            WidgetTopAppsPanel.Children.Add(row);
        }

        if (WidgetTopAppsPanel.Children.Count == 0)
        {
            WidgetTopAppsPanel.Children.Add(new TextBlock
            {
                Text = "Heute noch keine App-Nutzung.",
                FontSize = 11,
                Opacity = 0.6
            });
        }
    }

    private void OnWidgetRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_hwnd != nint.Zero && e.GetCurrentPoint(WidgetRootGrid).Properties.IsLeftButtonPressed)
        {
            ReleaseCapture();
            SendMessageW(_hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }
    }

    private void OnPinTopToggled(object sender, RoutedEventArgs e)
    {
        if (_presenter is not null)
        {
            _presenter.IsAlwaysOnTop = PinTopToggle.IsChecked == true;
        }
    }

    private void OnOpenMainWindowClicked(object sender, RoutedEventArgs e)
    {
        _onOpenMainRequested();
    }

    private void OnCloseWidgetClicked(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
