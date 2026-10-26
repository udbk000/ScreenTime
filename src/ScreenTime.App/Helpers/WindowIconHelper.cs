using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using ScreenTime.Core.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ScreenTime.App.Helpers;

/// <summary>
/// Erzeugt das App-Symbol (ICO) dynamisch anhand der aktuellen System-Farbpalette und des PC-Themes (Dark/Light Mode),
/// setzt es für Taskleiste, Alt-Tab und System-Tray und blendet es in der minimalen Fenster-Titelleiste aus.
/// </summary>
public static class WindowIconHelper
{
    private const uint WM_SETICON = 0x0080;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    public static string GetDynamicThemeIconPath(ThemePreference preference)
    {
        bool isDark = ColorHelperEx.IsEffectiveDarkMode(preference);
        Color accent = ColorHelperEx.GetSystemAccentColor(isDark ? UIColorType.AccentLight2 : UIColorType.Accent);
        Color accentSecondary = ColorHelperEx.GetSystemAccentColor(isDark ? UIColorType.AccentLight1 : UIColorType.AccentDark1);

        string localDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenTime",
            "icons");
        Directory.CreateDirectory(localDir);

        string dynamicIcoPath = Path.Combine(localDir, $"app_theme_{(isDark ? "dark" : "light")}_{accent.R}_{accent.G}_{accent.B}.ico");
        try
        {
            byte[] icoBytes = BuildThemeAdaptiveIcoBytes(isDark, accent, accentSecondary);
            File.WriteAllBytes(dynamicIcoPath, icoBytes);

            // Aktualisiere nach Möglichkeit auch Assets\app.ico im Ausgabeverzeichnis
            string assetIco = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (Directory.Exists(Path.GetDirectoryName(assetIco)))
            {
                try
                {
                    File.WriteAllBytes(assetIco, icoBytes);
                }
                catch
                {
                    // Ignorieren, falls Datei gerade vom System gesperrt ist
                }
            }
        }
        catch
        {
            string fallback = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(fallback))
            {
                return fallback;
            }
        }

        return dynamicIcoPath;
    }

    public static nint LoadAppIconHandle(int size = 32, ThemePreference preference = ThemePreference.System)
    {
        string icoPath = GetDynamicThemeIconPath(preference);
        if (File.Exists(icoPath))
        {
            nint hIcon = LoadImageW(nint.Zero, icoPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);
            if (hIcon != nint.Zero)
            {
                return hIcon;
            }
        }
        return LoadIconW(nint.Zero, (nint)32512);
    }

    public static void ApplyWindowIcon(
        nint hwnd,
        AppWindow? appWindow,
        ThemePreference preference = ThemePreference.System,
        SystemTrayService? trayService = null)
    {
        string icoPath = GetDynamicThemeIconPath(preference);
        if (File.Exists(icoPath) && appWindow is not null)
        {
            try
            {
                appWindow.SetIcon(icoPath);
                if (AppWindowTitleBar.IsCustomizationSupported())
                {
                    appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
                }
            }
            catch
            {
                // Fallback auf WM_SETICON
            }
        }

        if (hwnd != nint.Zero)
        {
            nint smallIcon = LoadAppIconHandle(16, preference);
            nint bigIcon = LoadAppIconHandle(32, preference);
            if (smallIcon != nint.Zero)
            {
                SendMessageW(hwnd, WM_SETICON, 0, smallIcon);
            }
            if (bigIcon != nint.Zero)
            {
                SendMessageW(hwnd, WM_SETICON, 1, bigIcon);
                trayService?.UpdateIcon(bigIcon);
            }
        }
    }

    /// <summary>
    /// Erzeugt eine mehrstufige 32-Bit-ARGB-.ico-Datei (16x16, 32x32, 48x48) komplett in den Systemfarben des PCs
    /// (Dunkler/Heller Hintergrund passend zum Theme + System-Akzentfarbe für Umrandung und Balken).
    /// </summary>
    private static byte[] BuildThemeAdaptiveIcoBytes(bool isDark, Color accentPrimary, Color accentSecondary)
    {
        int[] sizes = { 16, 32, 48 };
        byte[][] images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            images[i] = RenderSingleDibImage(sizes[i], isDark, accentPrimary, accentSecondary);
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICONDIR Header (6 Bytes)
        bw.Write((ushort)0); // Reserved
        bw.Write((ushort)1); // Type = 1 (Icon)
        bw.Write((ushort)sizes.Length);

        int dataOffset = 6 + (16 * sizes.Length);
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            int length = images[i].Length;
            bw.Write((byte)s);          // Width
            bw.Write((byte)s);          // Height
            bw.Write((byte)0);          // ColorCount
            bw.Write((byte)0);          // Reserved
            bw.Write((ushort)1);        // Planes
            bw.Write((ushort)32);       // BitCount (32-bit BGRA)
            bw.Write(length);           // BytesInRes
            bw.Write(dataOffset);       // ImageOffset
            dataOffset += length;
        }

        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(images[i]);
        }

        return ms.ToArray();
    }

    private static byte[] RenderSingleDibImage(int size, bool isDark, Color accentPrimary, Color accentSecondary)
    {
        int andMaskRowBytes = ((size + 31) / 32) * 4;
        int andMaskSize = andMaskRowBytes * size;
        int xorSize = size * size * 4;

        using var ms = new MemoryStream(40 + xorSize + andMaskSize);
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER (40 Bytes)
        bw.Write(40);               // biSize
        bw.Write(size);             // biWidth
        bw.Write(size * 2);         // biHeight (XOR + AND)
        bw.Write((ushort)1);        // biPlanes
        bw.Write((ushort)32);       // biBitCount
        bw.Write(0);                // biCompression (BI_RGB)
        bw.Write(xorSize + andMaskSize);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        // Theme-adaptive Hintergrundfarbe (Schwarz/Dunkelgrau im Dark Mode, Weiß/Hellgrau im Light Mode)
        Color bgTop = isDark ? Color.FromArgb(255, 22, 22, 26) : Color.FromArgb(255, 255, 255, 255);
        Color bgBottom = isDark ? Color.FromArgb(255, 8, 8, 10) : Color.FromArgb(255, 238, 240, 245);

        double radius = size * 0.24;
        double pad = size * 0.06;

        // DIB-Zeilen werden von unten nach oben geschrieben (y = size - 1 bis 0)
        for (int y = size - 1; y >= 0; y--)
        {
            double ny = (double)y / Math.Max(1, size - 1);
            for (int x = 0; x < size; x++)
            {
                double nx = (double)x / Math.Max(1, size - 1);

                double dist = SignedDistanceRoundedRect(
                    x + 0.5,
                    y + 0.5,
                    pad,
                    pad,
                    size - 2 * pad,
                    size - 2 * pad,
                    radius);

                if (dist > 0.75)
                {
                    bw.Write((uint)0); // Transparent außerhalb des abgerundeten Quadrats
                    continue;
                }

                double edgeAlpha = Math.Clamp(0.75 - dist, 0.0, 1.0);

                // Hintergrundverlauf + dezenter System-Akzent-Rand
                Color pixel = LerpColor(bgTop, bgBottom, ny);
                if (dist > -1.35)
                {
                    double borderMix = Math.Clamp((dist + 1.35) / 1.35, 0.0, 1.0) * (isDark ? 0.65 : 0.55);
                    pixel = LerpColor(pixel, accentPrimary, borderMix);
                }

                // Drei aufsteigende Statistik-Balken im System-Akzentfarbton
                bool inBar1 = nx >= 0.23 && nx <= 0.36 && ny >= 0.52 && ny <= 0.77;
                bool inBar2 = nx >= 0.43 && nx <= 0.56 && ny >= 0.36 && ny <= 0.77;
                bool inBar3 = nx >= 0.63 && nx <= 0.76 && ny >= 0.22 && ny <= 0.77;

                if (inBar1 || inBar2 || inBar3)
                {
                    Color barColor = inBar3
                        ? accentPrimary
                        : inBar2
                            ? LerpColor(accentPrimary, accentSecondary, 0.4)
                            : accentSecondary;
                    pixel = barColor;
                }

                byte finalA = (byte)Math.Clamp((int)Math.Round(edgeAlpha * 255), 0, 255);
                bw.Write(pixel.B);
                bw.Write(pixel.G);
                bw.Write(pixel.R);
                bw.Write(finalA);
            }
        }

        // Leere AND-Maske (Transparenz erfolgt über 32-Bit-Alpha)
        bw.Write(new byte[andMaskSize]);
        return ms.ToArray();
    }

    private static double SignedDistanceRoundedRect(double px, double py, double x, double y, double w, double h, double r)
    {
        double cx = x + w * 0.5;
        double cy = y + h * 0.5;
        double hx = w * 0.5 - r;
        double hy = h * 0.5 - r;
        double dx = Math.Max(Math.Abs(px - cx) - hx, 0.0);
        double dy = Math.Max(Math.Abs(py - cy) - hy, 0.0);
        double outside = Math.Sqrt(dx * dx + dy * dy);
        double inside = Math.Min(Math.Max(Math.Abs(px - cx) - hx, Math.Abs(py - cy) - hy), 0.0);
        return outside + inside - r;
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromArgb(
            255,
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static string? PickExecutableFile(nint ownerHwnd)
    {
        var ofn = new OPENFILENAMEW();
        ofn.lStructSize = Marshal.SizeOf<OPENFILENAMEW>();
        ofn.hwndOwner = ownerHwnd;
        ofn.lpstrFilter = "Programme (*.exe)\0*.exe\0Alle Dateien (*.*)\0*.*\0\0";
        ofn.lpstrFile = new string('\0', 1024);
        ofn.nMaxFile = ofn.lpstrFile.Length;
        ofn.lpstrTitle = "Programm am PC auswählen";
        ofn.Flags = 0x00080000 | 0x00001000 | 0x00000800; // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST

        if (GetOpenFileNameW(ref ofn))
        {
            int nullIdx = ofn.lpstrFile.IndexOf('\0');
            return nullIdx >= 0 ? ofn.lpstrFile[..nullIdx] : ofn.lpstrFile;
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public string lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW lpofn);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(nint hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIconW(nint hInstance, nint lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);
}
