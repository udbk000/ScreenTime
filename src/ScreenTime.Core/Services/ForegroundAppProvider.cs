using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ScreenTime.Core.Abstractions;
using ScreenTime.Core.Models;

namespace ScreenTime.Core.Services;

/// <summary>
/// Ermittelt den aktiven Vordergrundprozess über Win32-APIs.
/// Löst auch UWP-/MSIX-Apps unter ApplicationFrameHost.exe zuverlässig auf und
/// stellt einen flüchtigen Fenstertitel im RAM für den Abgleich benutzerdefinierter Webseiten bereit.
/// </summary>
public sealed class ForegroundAppProvider : IForegroundAppProvider
{
    private readonly IAppLogger _logger;

    private static readonly HashSet<string> IgnoredSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle",
        "LockApp",
        "LogonUI",
        "ScreenClippingHost",
        "SearchHost",
        "SearchApp",
        "StartMenuExperienceHost",
        "ShellExperienceHost",
        "TextInputHost",
        "dwm",
        "csrss"
    };

    public ForegroundAppProvider(IAppLogger logger)
    {
        _logger = logger;
    }

    public ForegroundProcessInfo? GetCurrentForegroundProcess()
    {
        try
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == nint.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                return null;
            }

            var info = TryBuildProcessInfo((int)pid, hwnd);
            if (info is null)
            {
                return null;
            }

            // Wenn ApplicationFrameHost.exe aktiv ist, das eigentliche UWP-Kindfenster ermitteln
            if (info.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                uint childPid = FindRealUwpProcessId(hwnd, pid);
                if (childPid != 0 && childPid != pid)
                {
                    var childInfo = TryBuildProcessInfo((int)childPid, hwnd);
                    if (childInfo is not null)
                    {
                        info = childInfo;
                    }
                }
            }

            if (IgnoredSystemProcesses.Contains(info.ProcessName))
            {
                return null;
            }

            return info;
        }
        catch (Exception ex)
        {
            _logger.Error("Fehler beim Auslesen des Vordergrundfensters", ex);
            return null;
        }
    }

    private ForegroundProcessInfo? TryBuildProcessInfo(int pid, nint hwnd)
    {
        string? exePath = TryGetExecutablePath(pid, out string? packageFullName);
        string? processName = null;

        if (!string.IsNullOrWhiteSpace(exePath))
        {
            processName = Path.GetFileNameWithoutExtension(exePath);
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                processName = proc.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        string? windowTitle = TryGetTransientWindowTitle(hwnd);
        bool isFullscreen = CheckIsWindowFullscreen(hwnd);

        return new ForegroundProcessInfo
        {
            ProcessId = pid,
            ProcessName = processName,
            ExecutablePath = exePath,
            PackageFullName = packageFullName,
            WindowHandle = hwnd,
            TransientWindowTitle = windowTitle,
            IsFullscreen = isFullscreen,
            TimestampUtc = DateTimeOffset.UtcNow
        };
    }

    private static string? TryGetTransientWindowTitle(nint hwnd)
    {
        var sb = new StringBuilder(512);
        int len = GetWindowTextW(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : null;
    }

    private static bool CheckIsWindowFullscreen(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out RECT winRect))
        {
            return false;
        }

        nint hMonitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
        if (hMonitor == nint.Zero)
        {
            return false;
        }

        var monInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(hMonitor, ref monInfo))
        {
            return false;
        }

        int winWidth = winRect.Right - winRect.Left;
        int winHeight = winRect.Bottom - winRect.Top;
        int monWidth = monInfo.rcMonitor.Right - monInfo.rcMonitor.Left;
        int monHeight = monInfo.rcMonitor.Bottom - monInfo.rcMonitor.Top;

        return monWidth > 0 && monHeight > 0 &&
               winWidth >= monWidth && winHeight >= monHeight &&
               winRect.Left <= monInfo.rcMonitor.Left && winRect.Top <= monInfo.rcMonitor.Top;
    }

    private static uint FindRealUwpProcessId(nint parentHwnd, uint hostPid)
    {
        uint foundPid = 0;
        EnumChildWindows(parentHwnd, (childHwnd, lParam) =>
        {
            GetWindowThreadProcessId(childHwnd, out uint childPid);
            if (childPid != 0 && childPid != hostPid)
            {
                foundPid = childPid;
                return false;
            }
            return true;
        }, nint.Zero);
        return foundPid;
    }

    private static string? TryGetExecutablePath(int pid, out string? packageFullName)
    {
        packageFullName = null;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        nint hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (hProcess == nint.Zero)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            string? path = null;
            if (QueryFullProcessImageNameW(hProcess, 0, sb, ref size))
            {
                path = sb.ToString();
            }

            uint pkgLen = 256;
            var pkgSb = new StringBuilder((int)pkgLen);
            int pkgRc = GetPackageFullName(hProcess, ref pkgLen, pkgSb);
            if (pkgRc == 0 && pkgSb.Length > 0)
            {
                packageFullName = pkgSb.ToString();
            }

            return path;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint hwndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(nint hProcess, ref uint packageFullNameLength, StringBuilder packageFullName);
}
