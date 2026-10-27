using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace PrimeDesktop.Services;

public class SystemTrayService : IDisposable
{
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;
    private const int WM_USER = 0x0400;
    private const int WM_TRAYCALLBACK = WM_USER + 1024;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    private readonly Window _window;
    private IntPtr _hwnd;
    private bool _isAdded;

    public SystemTrayService(Window window)
    {
        _window = window;
        _window.SourceInitialized += (s, e) => InitializeTray();
        if (_window.IsLoaded) InitializeTray();
    }

    private void InitializeTray()
    {
        if (_isAdded) return;
        _hwnd = new WindowInteropHelper(_window).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        var nid = CreateNotifyData();
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        _isAdded = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private static IntPtr GetAppIconHandle()
    {
        try
        {
            string? exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe))
            {
                IntPtr h = ExtractIcon(IntPtr.Zero, exe, 0);
                if (h != IntPtr.Zero && h.ToInt64() > 1) return h;
            }
        }
        catch { }
        return LoadIcon(IntPtr.Zero, (IntPtr)32512);
    }

    private NOTIFYICONDATA CreateNotifyData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _hwnd,
            uID = 1,
            uCallbackMessage = WM_TRAYCALLBACK,
            hIcon = GetAppIconHandle(),
            szTip = "Prime Desktop"
        };
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isAdded) return;
        var nid = CreateNotifyData();
        nid.uFlags = NIF_INFO;
        nid.szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title;
        nid.szInfo = message.Length > 255 ? message.Substring(0, 255) : message;
        nid.dwInfoFlags = 0x01; // NIIF_INFO
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYCALLBACK)
        {
            int action = lParam.ToInt32();
            if (action == WM_LBUTTONDBLCLK || action == 0x0202 /* WM_LBUTTONUP */)
            {
                RestoreWindow();
                handled = true;
            }
            else if (action == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void RestoreWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();
        if (Application.Current.TryFindResource("ModernContextMenu") is Style cmStyle)
        {
            menu.Style = cmStyle;
        }

        var openItem = new MenuItem { Header = "Открыть Прайм" };
        if (Application.Current.TryFindResource("ModernMenuItem") is Style miStyle)
        {
            openItem.Style = miStyle;
        }
        openItem.Click += (s, e) => RestoreWindow();

        var exitItem = new MenuItem { Header = "Выход" };
        if (Application.Current.TryFindResource("DangerMenuItem") is Style dangerStyle)
        {
            exitItem.Style = dangerStyle;
        }
        exitItem.Click += (s, e) =>
        {
            if (_window is MainWindow mw)
            {
                mw.ForceClose();
            }
            Application.Current.Shutdown();
        };

        menu.Items.Add(openItem);
        menu.Items.Add(new Separator { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x31, 0x45)), Margin = new Thickness(4, 3, 4, 3) });
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_isAdded)
        {
            var nid = CreateNotifyData();
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isAdded = false;
        }
    }
}
