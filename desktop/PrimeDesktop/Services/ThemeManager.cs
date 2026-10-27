using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PrimeDesktop.Services;

public static class ThemeManager
{
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;
    public static bool IsWindows10OrGreater => Environment.OSVersion.Version.Major >= 10;

    private static bool _listenerInitialized = false;
    private static string _currentMode = "system";
    private static string _currentAccentHex = "#4D9FFF";

    public static bool GetSystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int i) return i == 0;
        }
        catch { }
        return true; // Default to dark on error
    }

    public static bool ResolveEffectiveDark(string themeMode)
    {
        return themeMode switch
        {
            "dark" => true,
            "light" => false,
            _ => GetSystemIsDark()
        };
    }

    public static void InitializeTheme(string themeMode, string? accentHex = null)
    {
        _currentMode = string.IsNullOrWhiteSpace(themeMode) ? "system" : themeMode;
        if (!string.IsNullOrWhiteSpace(accentHex)) _currentAccentHex = accentHex;

        if (!_listenerInitialized)
        {
            _listenerInitialized = true;
            try
            {
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            if (_currentMode == "system")
                            {
                                ApplyTheme("system", _currentAccentHex);
                            }
                        });
                    }
                };
            }
            catch { }
        }

        ApplyTheme(_currentMode, _currentAccentHex);
    }

    public static void ApplyTheme(string themeMode, string? accentHex = null)
    {
        _currentMode = string.IsNullOrWhiteSpace(themeMode) ? "system" : themeMode;
        if (!string.IsNullOrWhiteSpace(accentHex)) _currentAccentHex = accentHex;

        bool isDark = ResolveEffectiveDark(_currentMode);
        var res = Application.Current?.Resources;
        if (res == null) return;

        if (isDark)
        {
            res["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x17));
            res["SidebarBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x15, 0x1F));
            res["HeaderBrush"] = new SolidColorBrush(Color.FromRgb(0x12, 0x19, 0x24));
            res["CardBrush"] = new SolidColorBrush(Color.FromRgb(0x18, 0x22, 0x32));
            res["CardHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x22, 0x2F, 0x44));
            res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x22, 0x30, 0x45));
            res["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x15, 0x1E, 0x2B));
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF6, 0xFC));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81));
            res["SentBubbleBrush"] = new SolidColorBrush(Color.FromRgb(0x15, 0x4B, 0x87));
            res["ReceivedBubbleBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x25, 0x36));
        }
        else
        {
            res["BackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            res["SidebarBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["HeaderBrush"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB));
            res["CardBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["CardHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5));
            res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0xDC, 0xE0, 0xE5));
            res["SurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB));
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            res["SentBubbleBrush"] = new SolidColorBrush(Color.FromRgb(0x15, 0x4B, 0x87));
            res["ReceivedBubbleBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        }

        if (!string.IsNullOrWhiteSpace(_currentAccentHex))
        {
            ApplyAccentColor(_currentAccentHex);
        }

        // Apply DWM styles to all open windows
        if (Application.Current?.Windows != null)
        {
            foreach (Window win in Application.Current.Windows)
            {
                ApplyWindowDwm(win, isDark);
            }
        }
    }

    public static void ApplyAccentColor(string hexColor)
    {
        try
        {
            _currentAccentHex = hexColor;
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            var res = Application.Current?.Resources;
            if (res == null) return;

            res["AccentColor"] = color;
            res["AccentBrush"] = new SolidColorBrush(color);

            // Light hover / translucent shade (25% opacity)
            var hoverColor = Color.FromArgb(64, color.R, color.G, color.B);
            res["AccentHoverBrush"] = new SolidColorBrush(hoverColor);

            // Soft chip shade (15% opacity)
            var softColor = Color.FromArgb(38, color.R, color.G, color.B);
            res["AccentSoftBrush"] = new SolidColorBrush(softColor);

            // Border shade (40% opacity)
            var borderColor = Color.FromArgb(102, color.R, color.G, color.B);
            res["AccentBorderBrush"] = new SolidColorBrush(borderColor);
        }
        catch { }
    }

    public static void ApplyWindowDwm(Window win, bool? forceDark = null)
    {
        try
        {
            var helper = new WindowInteropHelper(win);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            bool isDark = forceDark ?? ResolveEffectiveDark(_currentMode);

            // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Windows 10 20H1+ and Windows 11)
            int darkMode = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));

            // Windows 11 specific features: Rounded corners & Mica backdrop
            if (IsWindows11)
            {
                // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2
                int cornerPref = 2;
                DwmSetWindowAttribute(hwnd, 33, ref cornerPref, sizeof(int));

                // DWMWA_SYSTEMBACKDROP_TYPE = 38 (DWMSBT_MAINWINDOW / Mica = 2)
                int backdrop = 2;
                DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            }
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
