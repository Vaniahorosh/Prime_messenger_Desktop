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

    public static event Action<string>? SystemAccentColorChanged;

    /// <summary>
    /// Detects the native Windows System Accent Color from Registry / DWM / SystemParameters.
    /// Returns a hex color string like "#461135".
    /// </summary>
    public static string GetWindowsSystemAccentHex()
    {
        try
        {
            // 1. DWM AccentColor (DWORD ABGR: 0xAABBGGRR)
            using var dwmKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            var val = dwmKey?.GetValue("AccentColor");
            if (val is int dwmInt)
            {
                byte r = (byte)(dwmInt & 0xFF);
                byte g = (byte)((dwmInt >> 8) & 0xFF);
                byte b = (byte)((dwmInt >> 16) & 0xFF);
                return $"#{r:X2}{g:X2}{b:X2}";
            }

            // 2. Explorer AccentColorMenu
            using var expKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            var expVal = expKey?.GetValue("AccentColorMenu");
            if (expVal is int expInt)
            {
                byte r = (byte)(expInt & 0xFF);
                byte g = (byte)((expInt >> 8) & 0xFF);
                byte b = (byte)((expInt >> 16) & 0xFF);
                return $"#{r:X2}{g:X2}{b:X2}";
            }

            // 3. Fallback to WPF SystemParameters.WindowGlassColor
            var glass = SystemParameters.WindowGlassColor;
            return $"#{glass.R:X2}{glass.G:X2}{glass.B:X2}";
        }
        catch
        {
            return "#0078D4"; // Default Windows Fluent Blue fallback
        }
    }

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
                            var newSysAccent = GetWindowsSystemAccentHex();
                            SystemAccentColorChanged?.Invoke(newSysAccent);

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
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF6, 0xF8));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81));
            res["ReceivedBubbleBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x25, 0x36));

            res["GlassSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(0xD0, 0x12, 0x1C, 0x2B));
            res["GlassBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x80, 0x5A, 0x82, 0xB5));
            res["GlassVignetteTopBrush"] = new LinearGradientBrush(
                new GradientStopCollection {
                    new GradientStop(Color.FromArgb(128, 0x08, 0x0D, 0x14), 0.0),
                    new GradientStop(Color.FromArgb(64, 0x08, 0x0D, 0x14), 0.45),
                    new GradientStop(Color.FromArgb(0, 0x08, 0x0D, 0x14), 1.0)
                }, new Point(0, 0), new Point(0, 1));
            res["GlassVignetteBottomBrush"] = new LinearGradientBrush(
                new GradientStopCollection {
                    new GradientStop(Color.FromArgb(0, 0x08, 0x0D, 0x14), 0.0),
                    new GradientStop(Color.FromArgb(64, 0x08, 0x0D, 0x14), 0.55),
                    new GradientStop(Color.FromArgb(128, 0x08, 0x0D, 0x14), 1.0)
                }, new Point(0, 0), new Point(0, 1));
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
            res["ReceivedBubbleBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

            res["GlassSurfaceBrush"] = new SolidColorBrush(Color.FromArgb(0xE8, 0xF7, 0xF9, 0xFC));
            res["GlassBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x90, 0x98, 0xAB, 0xC4));
            res["GlassVignetteTopBrush"] = new LinearGradientBrush(
                new GradientStopCollection {
                    new GradientStop(Color.FromArgb(128, 0xC8, 0xD0, 0xD9), 0.0),
                    new GradientStop(Color.FromArgb(64, 0xC8, 0xD0, 0xD9), 0.45),
                    new GradientStop(Color.FromArgb(0, 0xC8, 0xD0, 0xD9), 1.0)
                }, new Point(0, 0), new Point(0, 1));
            res["GlassVignetteBottomBrush"] = new LinearGradientBrush(
                new GradientStopCollection {
                    new GradientStop(Color.FromArgb(0, 0xC8, 0xD0, 0xD9), 0.0),
                    new GradientStop(Color.FromArgb(64, 0xC8, 0xD0, 0xD9), 0.55),
                    new GradientStop(Color.FromArgb(128, 0xC8, 0xD0, 0xD9), 1.0)
                }, new Point(0, 0), new Point(0, 1));
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
                if (win.IsLoaded && PresentationSource.FromVisual(win) != null)
                {
                    ApplyWindowDwm(win, isDark);
                }
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

            var accentBrush = new SolidColorBrush(color);
            var badgeBrush = new SolidColorBrush(color);

            // Compute Sent Message Bubble Brush:
            // In dark mode: saturated, deeper shade of the accent color for great contrast with white text
            // In light mode: vibrant accent color
            bool isDark = ResolveEffectiveDark(_currentMode);
            Color sentBubbleColor;
            if (isDark)
            {
                byte r = (byte)(color.R * 0.45 + 10);
                byte g = (byte)(color.G * 0.45 + 15);
                byte b = (byte)(color.B * 0.45 + 25);
                sentBubbleColor = Color.FromRgb(r, g, b);
            }
            else
            {
                byte r = (byte)Math.Min(255, color.R * 0.85 + 25);
                byte g = (byte)Math.Min(255, color.G * 0.85 + 25);
                byte b = (byte)Math.Min(255, color.B * 0.85 + 25);
                sentBubbleColor = Color.FromRgb(r, g, b);
            }
            var sentBubbleBrush = new SolidColorBrush(sentBubbleColor);

            // Light hover / translucent shade (25% opacity)
            var hoverColor = Color.FromArgb(64, color.R, color.G, color.B);
            var hoverBrush = new SolidColorBrush(hoverColor);

            // Soft chip shade (18% opacity)
            var softColor = Color.FromArgb(46, color.R, color.G, color.B);
            var softBrush = new SolidColorBrush(softColor);

            // Border shade (45% opacity)
            var borderColor = Color.FromArgb(115, color.R, color.G, color.B);
            var borderBrush = new SolidColorBrush(borderColor);

            // 1. Update Application Resources
            res["AccentColor"] = color;
            res["AccentBrush"] = accentBrush;
            res["BadgeBrush"] = badgeBrush;
            res["SentBubbleBrush"] = sentBubbleBrush;
            res["AccentHoverBrush"] = hoverBrush;
            res["AccentSoftBrush"] = softBrush;
            res["AccentBorderBrush"] = borderBrush;

            // 2. Also inject into each open window's resources for instant DynamicResource re-evaluation
            if (Application.Current?.Windows != null)
            {
                foreach (Window win in Application.Current.Windows)
                {
                    if (win.IsLoaded && PresentationSource.FromVisual(win) != null)
                    {
                        win.Resources["AccentColor"] = color;
                        win.Resources["AccentBrush"] = new SolidColorBrush(color);
                        win.Resources["BadgeBrush"] = new SolidColorBrush(color);
                        win.Resources["SentBubbleBrush"] = new SolidColorBrush(sentBubbleColor);
                        win.Resources["AccentHoverBrush"] = new SolidColorBrush(hoverColor);
                        win.Resources["AccentSoftBrush"] = new SolidColorBrush(softColor);
                        win.Resources["AccentBorderBrush"] = new SolidColorBrush(borderColor);
                    }
                }
            }
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

    /// <summary>
    /// Applies high-performance, GPU-accelerated frosted glass (Acrylic / Blurview) to a window
    /// using Windows 11 DWMSBT_TRANSIENTWINDOW (3) or Windows 10 SetWindowCompositionAttribute.
    /// Zero CPU lag, full 60/120fps hardware acceleration.
    /// </summary>
    public static void ApplyGlassToWindow(Window win, bool isTransient = true)
    {
        try
        {
            var helper = new WindowInteropHelper(win);
            var hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyGlassToHwnd(hwnd, isTransient);
            }
            else
            {
                win.SourceInitialized += (s, e) =>
                {
                    var h = new WindowInteropHelper(win).Handle;
                    if (h != IntPtr.Zero) ApplyGlassToHwnd(h, isTransient);
                };
            }
        }
        catch { }
    }

    /// <summary>
    /// Applies hardware-accelerated frosted glass (Acrylic) directly to an HWND (Window, Popup, ContextMenu).
    /// </summary>
    public static void ApplyGlassToHwnd(IntPtr hwnd, bool isTransient = true)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            // Dark mode (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));

            // Windows 11: Rounded corners + System Backdrop
            if (IsWindows11)
            {
                int cornerPref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref cornerPref, sizeof(int));

                // DWMWA_SYSTEMBACKDROP_TYPE = 38 (3 = DWMSBT_TRANSIENTWINDOW / Acrylic, 2 = Mica)
                int backdrop = isTransient ? 3 : 2;
                DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            }

            // Windows 10 Acrylic & Win11 fallback via user32 AccentPolicy
            EnableAcrylicComposition(hwnd, 0xCC111822);
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    private static void EnableAcrylicComposition(IntPtr hwnd, uint gradientColor)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND (or 3 for ACCENT_ENABLE_BLURBEHIND)
                AccentFlags = 2,
                GradientColor = (int)gradientColor
            };

            int size = Marshal.SizeOf(accent);
            IntPtr pMem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pMem, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = pMem,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(pMem);
            }
        }
        catch { }
    }
}
