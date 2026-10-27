using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PrimeDesktop.Services;
using PrimeDesktop.ViewModels;
using Application = System.Windows.Application;

namespace PrimeDesktop;

public partial class MainWindow : Window
{
    public static SystemTrayService? TrayService { get; private set; }
    private bool _isExplicitExit = false;

    public void ForceClose()
    {
        _isExplicitExit = true;
        Close();
    }

    public MainWindow()
    {
        InitializeComponent();
        App.Log("MainWindow Initialized");
        Closing += (s, e) =>
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                Hide();
                TrayService?.ShowNotification("Прайм Мессенджер", "Приложение свернуто в системный трей.");
            }
            App.Log($"MainWindow Closing (Cancel={e.Cancel})");
        };
        Closed += (s, e) => App.Log("MainWindow Closed");
        StateChanged += MainWindow_StateChanged;

        TrayService = new SystemTrayService(this);
        Loaded += (s, e) =>
        {
            App.Log("MainWindow Loaded");
            ThemeManager.ApplyWindowDwm(this);
            if (DataContext is MainViewModel vm)
            {
                if (vm.Settings.EnableScreenshotProtection)
                {
                    SecurityManager.SetScreenshotProtection(this, true);
                }
                vm.PropertyChanged += (sender, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.IsMediaDrawerOpen))
                    {
                        if (!vm.IsMediaDrawerOpen)
                        {
                            ColDrawer.MinWidth = 0;
                            ColDrawer.Width = new GridLength(0);
                        }
                        else
                        {
                            ColDrawer.MinWidth = 280;
                            if (ColDrawer.Width.Value < 280)
                            {
                                ColDrawer.Width = new GridLength(330);
                            }
                        }
                    }
                    else if (args.PropertyName == nameof(MainViewModel.IsSearchActive))
                    {
                        AnimateSearch(vm.IsSearchActive);
                    }
                };
            }
        };

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (DataContext is MainViewModel vm)
                {
                    if (vm.IsPhotoViewerOpen)
                    {
                        vm.IsPhotoViewerOpen = false;
                        e.Handled = true;
                    }
                    else if (vm.IsSearchActive)
                    {
                        vm.IsSearchActive = false;
                        e.Handled = true;
                    }
                    else if (vm.IsSettingsOpen)
                    {
                        vm.IsSettingsOpen = false;
                        e.Handled = true;
                    }
                    else if (vm.IsMediaDrawerOpen)
                    {
                        vm.IsMediaDrawerOpen = false;
                        e.Handled = true;
                    }
                    else if (vm.HasSelectedChat)
                    {
                        vm.CloseActiveChatCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        };
    }

    public void AnimateSearch(bool isOpening)
    {
        if (LeftSlidingGroup == null || SearchMorphContainer == null || BtnSearchTrigger == null || 
            SearchActiveControls == null || BtnSearchClose == null || LeftGroupTransform == null || CloseBtnScale == null)
            return;

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = isOpening ? System.Windows.Media.Animation.EasingMode.EaseOut : System.Windows.Media.Animation.EasingMode.EaseInOut
        };
        var duration = TimeSpan.FromMilliseconds(250);

        // 1. Left items (Avatar, Name, Device Discovery) slide left and fade out
        var leftTranslate = new System.Windows.Media.Animation.DoubleAnimation(isOpening ? -300 : 0, duration) { EasingFunction = ease };
        var leftOpacity = new System.Windows.Media.Animation.DoubleAnimation(isOpening ? 0.0 : 1.0, duration) { EasingFunction = ease };

        LeftGroupTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, leftTranslate);
        LeftSlidingGroup.BeginAnimation(UIElement.OpacityProperty, leftOpacity);
        LeftSlidingGroup.IsHitTestVisible = !isOpening;

        // 2. SearchMorphContainer width animation:
        // Anchored at Right, expands from 34px to the full width of Row 0
        double fullWidth = 292;
        if (SearchMorphContainer.Parent is FrameworkElement parent && parent.ActualWidth > 50)
        {
            fullWidth = parent.ActualWidth;
        }

        var widthAnim = new System.Windows.Media.Animation.DoubleAnimation(isOpening ? fullWidth : 34, duration) { EasingFunction = ease };
        SearchMorphContainer.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

        // 3. Trigger button fades out when opening, fades in when closing
        var triggerOpacity = new System.Windows.Media.Animation.DoubleAnimation(isOpening ? 0.0 : 1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
        BtnSearchTrigger.BeginAnimation(UIElement.OpacityProperty, triggerOpacity);
        BtnSearchTrigger.IsHitTestVisible = !isOpening;

        // 4. Search input controls fade in when opening, fade out when closing
        var controlsOpacity = new System.Windows.Media.Animation.DoubleAnimation(isOpening ? 1.0 : 0.0, TimeSpan.FromMilliseconds(200))
        {
            BeginTime = isOpening ? TimeSpan.FromMilliseconds(70) : TimeSpan.Zero,
            EasingFunction = ease
        };
        SearchActiveControls.BeginAnimation(UIElement.OpacityProperty, controlsOpacity);
        SearchActiveControls.IsHitTestVisible = isOpening;

        // 5. Close button (✕) fades in on the right with subtle pop scale
        var closeOpacity = new System.Windows.Media.Animation.DoubleAnimation(isOpening ? 1.0 : 0.0, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = isOpening ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero,
            EasingFunction = ease
        };
        var closeScale = new System.Windows.Media.Animation.DoubleAnimation(isOpening ? 1.0 : 0.4, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = isOpening ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero,
            EasingFunction = ease
        };

        BtnSearchClose.BeginAnimation(UIElement.OpacityProperty, closeOpacity);
        CloseBtnScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, closeScale);
        CloseBtnScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, closeScale);

        if (isOpening)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                SearchTextBox?.Focus();
                SearchTextBox?.SelectAll();
            });
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (IconMaximize == null || IconRestore == null || BtnMaximize == null) return;

        if (WindowState == WindowState.Maximized)
        {
            IconMaximize.Visibility = Visibility.Collapsed;
            IconRestore.Visibility = Visibility.Visible;
            BtnMaximize.ToolTip = "Восстановить";
        }
        else
        {
            IconMaximize.Visibility = Visibility.Visible;
            IconRestore.Visibility = Visibility.Collapsed;
            BtnMaximize.ToolTip = "Развернуть";
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyWindowDwm(this);
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int WM_GETMINMAXINFO = 0x0024;

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                var mousePos = PointToScreen(e.GetPosition(this));
                var ratio = e.GetPosition(this).X / Math.Max(ActualWidth, 1);
                WindowState = WindowState.Normal;
                Left = mousePos.X - (ActualWidth * ratio);
                Top = mousePos.Y - 18;
            }

            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        TrayService?.ShowNotification("Прайм Мессенджер", "Приложение свернуто в системный трей.");
    }

    protected override void OnClosed(EventArgs e)
    {
        TrayService?.Dispose();
        base.OnClosed(e);
    }

    private void MsgInputTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                int caret = MsgInputTextBox.CaretIndex;
                MsgInputTextBox.Text = MsgInputTextBox.Text.Insert(caret, Environment.NewLine);
                MsgInputTextBox.CaretIndex = caret + Environment.NewLine.Length;
                e.Handled = true;
            }
            else
            {
                if (DataContext is MainViewModel vm && vm.SendMessageCommand.CanExecute(null))
                {
                    vm.SendMessageCommand.Execute(null);
                }
                e.Handled = true;
            }
        }
    }

    private void MessageBorder_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.ChatMessage msg)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ContextMenuMessage = msg;
            }
        }
    }

    private void ReactionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            Models.ChatMessage? msg = fe.Tag as Models.ChatMessage;
            if (msg == null && fe is System.Windows.Controls.Button btnTag && btnTag.Tag is Models.ChatMessage mTag)
            {
                msg = mTag;
            }
            if (msg == null && DataContext is MainViewModel vmMsg)
            {
                msg = vmMsg.ContextMenuMessage;
            }

            if (msg != null && DataContext is MainViewModel vm)
            {
                vm.ContextMenuMessage = msg;
                if (fe is System.Windows.Controls.Button btn && btn.CommandParameter is string emoji)
                {
                    _ = vm.SendReactionAsync(msg, emoji);
                }
            }

            var cm = FindVisualParent<System.Windows.Controls.ContextMenu>(fe);
            if (cm != null) cm.IsOpen = false;
        }
    }

    private void ProfileDrawer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.NewSize.Width > 0)
        {
            vm.UpdateDrawerGridColumns(e.NewSize.Width);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }
}
