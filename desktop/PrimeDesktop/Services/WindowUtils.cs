using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PrimeDesktop.Services;

public static class WindowUtils
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int WM_GETMINMAXINFO = 0x0024;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor = new RECT();
        public RECT rcWork = new RECT();
        public int dwFlags = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    public static void EnableWindowMinMaxHook(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO();
            GetMonitorInfo(monitor, monitorInfo);

            mmi.ptMaxPosition.x = monitorInfo.rcWork.left - monitorInfo.rcMonitor.left;
            mmi.ptMaxPosition.y = monitorInfo.rcWork.top - monitorInfo.rcMonitor.top;
            mmi.ptMaxSize.x = monitorInfo.rcWork.right - monitorInfo.rcWork.left;
            mmi.ptMaxSize.y = monitorInfo.rcWork.bottom - monitorInfo.rcWork.top;
            mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
            mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
        }
        Marshal.StructureToPtr(mmi, lParam, true);
    }
}
