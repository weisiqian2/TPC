using System.IO;
using System.Runtime.InteropServices;

namespace TPC.WinUI;

internal sealed class Win32TrayIcon : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAY = WM_USER + 91;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;
    private const int MF_STRING = 0x0000;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    private const int LR_DEFAULTSIZE = 0x00000040;
    private const int ID_OPEN = 1001;
    private const int ID_PAUSE = 1002;
    private const int ID_RESUME = 1003;
    private const int ID_EXIT = 1004;

    private static readonly IntPtr HwndMessage = new(-3);
    private readonly Action _open;
    private readonly Action _pause;
    private readonly Action _resume;
    private readonly Action _exit;
    private readonly WndProc _wndProc;
    private readonly string _className;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private IntPtr _hwnd;
    private bool _disposed;

    public Win32TrayIcon(Action open, Action pause, Action resume, Action exit)
    {
        _open = open;
        _pause = pause;
        _resume = resume;
        _exit = exit;
        _wndProc = WindowProc;
        _className = "TPCTrayWindow_" + Guid.NewGuid().ToString("N");
        (_icon, _ownsIcon) = LoadTrayIcon();
        CreateMessageWindow();
        AddIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            var data = CreateNotifyData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }
    }

    private void CreateMessageWindow()
    {
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = _className
        };
        RegisterClassEx(ref wc);
        _hwnd = CreateWindowEx(
            0,
            _className,
            "",
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            wc.hInstance,
            IntPtr.Zero);
    }

    private void AddIcon()
    {
        var data = CreateNotifyData();
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NotifyIconData CreateNotifyData()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _icon,
            szTip = "TPC 正在后台运行"
        };
    }

    private static (IntPtr Icon, bool OwnsIcon) LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
        if (File.Exists(iconPath))
        {
            var icon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            if (icon != IntPtr.Zero)
            {
                return (icon, true);
            }
        }

        return (LoadIcon(IntPtr.Zero, new IntPtr(32512)), false);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY)
        {
            var mouseMessage = lParam.ToInt32();
            if (mouseMessage == WM_LBUTTONDBLCLK)
            {
                _open();
                return IntPtr.Zero;
            }
            if (mouseMessage == WM_RBUTTONUP)
            {
                ShowMenu(hwnd);
                return IntPtr.Zero;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu(IntPtr hwnd)
    {
        GetCursorPos(out var point);
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, ID_OPEN, "打开主界面");
        AppendMenu(menu, MF_STRING, ID_PAUSE, "暂停全部");
        AppendMenu(menu, MF_STRING, ID_RESUME, "恢复全部");
        AppendMenu(menu, MF_STRING, ID_EXIT, "退出");
        SetForegroundWindow(hwnd);
        var command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        switch (command)
        {
            case ID_OPEN:
                _open();
                break;
            case ID_PAUSE:
                _pause();
                break;
            case ID_RESUME:
                _resume();
                break;
            case ID_EXIT:
                _exit();
                break;
        }
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
