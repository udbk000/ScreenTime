using System.Runtime.InteropServices;

namespace ScreenTime.App.Helpers;

/// <summary>
/// Nativer Win32 System-Tray-Dienst (Shell_NotifyIconW) mit eigenem App-Icon und Widget-Umschalter.
/// </summary>
public sealed class SystemTrayService : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYMESSAGE = WM_USER + 202;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    private readonly nint _hwnd;
    private readonly Action _onRestoreRequested;
    private readonly Action _onToggleWidgetRequested;
    private readonly Action _onExitRequested;
    private readonly SubclassProc _subclassDelegate;
    private NOTIFYICONDATAW _nid;
    private bool _isAdded;
    private bool _disposed;

    public SystemTrayService(
        nint windowHandle,
        Action onRestoreRequested,
        Action onToggleWidgetRequested,
        Action onExitRequested)
    {
        _hwnd = windowHandle;
        _onRestoreRequested = onRestoreRequested;
        _onToggleWidgetRequested = onToggleWidgetRequested;
        _onExitRequested = onExitRequested;
        _subclassDelegate = WindowSubclassProc;
    }

    public void Initialize()
    {
        if (_isAdded || _hwnd == nint.Zero)
        {
            return;
        }

        SetWindowSubclass(_hwnd, _subclassDelegate, 101, nint.Zero);

        nint hIcon = WindowIconHelper.LoadAppIconHandle(32);
        _nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYMESSAGE,
            hIcon = hIcon,
            szTip = "ScreenTime – Aktive Computer-Nutzung"
        };

        _isAdded = Shell_NotifyIconW(NIM_ADD, ref _nid);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_isAdded)
        {
            return;
        }

        _nid.szTip = tooltip.Length > 120 ? tooltip[..120] : tooltip;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    public void UpdateIcon(nint hIcon)
    {
        if (!_isAdded || hIcon == nint.Zero)
        {
            return;
        }

        _nid.hIcon = hIcon;
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);
    }

    private nint WindowSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (uMsg == WM_TRAYMESSAGE)
        {
            uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
            {
                _onRestoreRequested();
                return nint.Zero;
            }
            if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return nint.Zero;
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        nint hMenu = CreatePopupMenu();
        if (hMenu == nint.Zero)
        {
            return;
        }

        try
        {
            AppendMenuW(hMenu, MF_STRING, 1, "ScreenTime öffnen");
            AppendMenuW(hMenu, MF_STRING, 2, "Desktop-Widget ein-/ausblenden");
            AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
            AppendMenuW(hMenu, MF_STRING, 3, "ScreenTime beenden");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, nint.Zero);

            if (cmd == 1)
            {
                _onRestoreRequested();
            }
            else if (cmd == 2)
            {
                _onToggleWidgetRequested();
            }
            else if (cmd == 3)
            {
                _onExitRequested();
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_isAdded)
        {
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
            _isAdded = false;
        }

        if (_hwnd != nint.Zero)
        {
            RemoveWindowSubclass(_hwnd, _subclassDelegate, 101);
        }
    }

    private delegate nint SubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}
