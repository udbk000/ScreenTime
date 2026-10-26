using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ScreenTime.Core.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ScreenTime.App.Helpers;

/// <summary>
/// Ermittelt die Systemfarbpalette des PCs (Windows UISettings Accent-/Theme-Farben)
/// und passt alle Diagramme, Balken, Badges sowie die Fenster-Titelleiste (Schwarz im Dark Mode, Weiß im Light Mode)
/// dynamisch an das aktive System-/App-Theme an.
/// </summary>
public static class ColorHelperEx
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public static bool IsEffectiveDarkMode(ThemePreference preference)
    {
        if (preference == ThemePreference.Dark)
        {
            return true;
        }
        if (preference == ThemePreference.Light)
        {
            return false;
        }

        try
        {
            var uiSettings = new UISettings();
            var bg = uiSettings.GetColorValue(UIColorType.Background);
            return (bg.R + bg.G + bg.B) < 384;
        }
        catch
        {
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }

    public static Color GetSystemAccentColor(UIColorType colorType = UIColorType.Accent)
    {
        try
        {
            var uiSettings = new UISettings();
            var c = uiSettings.GetColorValue(colorType);
            return Color.FromArgb(c.A, c.R, c.G, c.B);
        }
        catch
        {
            return Color.FromArgb(255, 0, 120, 212);
        }
    }

    /// <summary>
    /// Liefert den primären System-Akzent-Pinsel passend zum aktuellen Modus (Hell/Dunkel).
    /// </summary>
    public static SolidColorBrush GetPrimaryAccentBrush(bool isDark, byte alpha = 255)
    {
        var baseColor = GetSystemAccentColor(isDark ? UIColorType.AccentLight2 : UIColorType.Accent);
        return new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    /// <summary>
    /// Liefert den sekundären System-Akzent-Pinsel für Balken im Diagramm.
    /// </summary>
    public static SolidColorBrush GetSecondaryAccentBrush(bool isDark, byte alpha = 255)
    {
        var baseColor = GetSystemAccentColor(isDark ? UIColorType.AccentLight1 : UIColorType.AccentDark1);
        return new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    /// <summary>
    /// Liefert einen dezenten Hintergrund-Pinsel für leere Diagrammbalken / Fortschrittsleisten.
    /// </summary>
    public static SolidColorBrush GetMutedTrackBrush(bool isDark, byte alpha = 45)
    {
        return isDark
            ? new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
    }

    /// <summary>
    /// Erzeugt eine harmonische Abstufung aus der System-Akzentpalette des PCs für Kategorien und Diagrammsegmente,
    /// sodass alle Farben zu 100 % zur Windows-Systempalette im Light- und Dark-Mode passen.
    /// </summary>
    public static Color GetSystemCategoryColor(long categoryIndex, bool isDark, byte alpha = 255)
    {
        var accent = GetSystemAccentColor(UIColorType.Accent);
        var light1 = GetSystemAccentColor(UIColorType.AccentLight1);
        var light2 = GetSystemAccentColor(UIColorType.AccentLight2);
        var light3 = GetSystemAccentColor(UIColorType.AccentLight3);
        var dark1 = GetSystemAccentColor(UIColorType.AccentDark1);
        var dark2 = GetSystemAccentColor(UIColorType.AccentDark2);

        Color[] darkPalette =
        {
            light2,
            light1,
            accent,
            light3,
            BlendColor(light2, Color.FromArgb(255, 180, 200, 255), 0.35),
            BlendColor(light1, Color.FromArgb(255, 150, 220, 210), 0.35),
            dark1,
            Color.FromArgb(255, 150, 155, 165)
        };

        Color[] lightPalette =
        {
            accent,
            dark1,
            light1,
            dark2,
            BlendColor(accent, Color.FromArgb(255, 40, 80, 170), 0.35),
            BlendColor(dark1, Color.FromArgb(255, 30, 130, 130), 0.35),
            light2,
            Color.FromArgb(255, 120, 125, 135)
        };

        var palette = isDark ? darkPalette : lightPalette;
        int idx = (int)(Math.Abs(categoryIndex <= 0 ? 0 : categoryIndex - 1) % palette.Length);
        var chosen = palette[idx];
        return Color.FromArgb(alpha, chosen.R, chosen.G, chosen.B);
    }

    public static SolidColorBrush GetSystemCategoryBrush(long categoryIndex, bool isDark, byte alpha = 255)
        => new(GetSystemCategoryColor(categoryIndex, isDark, alpha));

    /// <summary>
    /// Nutzt für vom Benutzer erstellte Kategorien (mit eigener RGB-Farbwahl) exakt den gewählten RGB-Farbwert
    /// und für Standardkategorien die harmonische System-Farbpalette des PCs.
    /// </summary>
    public static SolidColorBrush GetCategoryBrush(long categoryId, string? colorHex, bool isDark, byte alpha = 255)
    {
        if (categoryId > 8 && !string.IsNullOrWhiteSpace(colorHex) && colorHex.StartsWith('#'))
        {
            return ToBrush(colorHex, alpha);
        }
        return GetSystemCategoryBrush(categoryId, isDark, alpha);
    }

    private static Color BlendColor(Color a, Color b, double ratioB)
    {
        double ratioA = 1.0 - ratioB;
        byte r = (byte)Math.Clamp((int)Math.Round(a.R * ratioA + b.R * ratioB), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(a.G * ratioA + b.G * ratioB), 0, 255);
        byte bl = (byte)Math.Clamp((int)Math.Round(a.B * ratioA + b.B * ratioB), 0, 255);
        return Color.FromArgb(255, r, g, bl);
    }

    public static Color ParseHex(string? hex, byte alpha = 255)
    {
        bool isDark = IsEffectiveDarkMode(ThemePreference.System);
        if (string.IsNullOrWhiteSpace(hex))
        {
            var acc = GetSystemAccentColor(isDark ? UIColorType.AccentLight2 : UIColorType.Accent);
            return Color.FromArgb(alpha, acc.R, acc.G, acc.B);
        }

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(clean[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(clean[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return Color.FromArgb(alpha, r, g, b);
        }

        var fallback = GetSystemAccentColor(isDark ? UIColorType.AccentLight2 : UIColorType.Accent);
        return Color.FromArgb(alpha, fallback.R, fallback.G, fallback.B);
    }

    public static SolidColorBrush ToBrush(string? hex, byte alpha = 255)
        => new(ParseHex(hex, alpha));

    /// <summary>
    /// Gestaltet die Titelleiste des Hauptfensters extrem minimal (kein Icon, kein Name sichtbar)
    /// und färbt sie strikt Schwarz im Dark Mode und Weiß im Light Mode.
    /// </summary>
    public static void ApplyWindowTitleBarTheme(nint hwnd, AppWindow? appWindow, ThemePreference preference)
    {
        bool isDark = IsEffectiveDarkMode(preference);

        Color bg = isDark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);
        Color fg = isDark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        Color hoverBg = isDark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 232, 232, 232);
        Color inactiveFg = isDark ? Color.FromArgb(255, 130, 130, 130) : Color.FromArgb(255, 130, 130, 130);

        if (appWindow is not null)
        {
            try
            {
                appWindow.Title = string.Empty;
            }
            catch
            {
                // Ignore
            }
        }

        if (appWindow is not null && AppWindowTitleBar.IsCustomizationSupported())
        {
            try
            {
                var titleBar = appWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true;
                titleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
                titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

                titleBar.BackgroundColor = bg;
                titleBar.ForegroundColor = fg;
                titleBar.InactiveBackgroundColor = bg;
                titleBar.InactiveForegroundColor = inactiveFg;

                titleBar.ButtonBackgroundColor = bg;
                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonHoverBackgroundColor = hoverBg;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedBackgroundColor = hoverBg;
                titleBar.ButtonPressedForegroundColor = fg;
                titleBar.ButtonInactiveBackgroundColor = bg;
                titleBar.ButtonInactiveForegroundColor = inactiveFg;
            }
            catch
            {
                // Fallback auf DWM-Attribute
            }
        }

        if (hwnd != nint.Zero)
        {
            try
            {
                SetWindowTextW(hwnd, string.Empty);

                int darkFlag = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));

                // COLORREF ist 0x00BBGGRR: 0x00000000 = Schwarz, 0x00FFFFFF = Weiß
                int captionColorRef = isDark ? 0x00000000 : 0x00FFFFFF;
                int textColorRef = isDark ? 0x00FFFFFF : 0x00000000;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColorRef, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColorRef, sizeof(int));
            }
            catch
            {
                // Ältere Windows-Builds ignorieren unbekannte DWM-Attribute
            }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint hWnd, string lpString);
}
