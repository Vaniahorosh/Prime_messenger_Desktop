using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrimeDesktop.Services;
using PrimeDesktop.ViewModels;

namespace PrimeDesktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        StateChanged += Window_StateChanged;
        Loaded += Window_Loaded;
        Closing += Window_Closing;
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyGlassToWindow(this);
        WindowUtils.EnableWindowMinMaxHook(this);
        if (DataContext is MainViewModel vm)
        {
            vm.CaptureSettingsSnapshot();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.HasPendingSettingsChanges)
        {
            vm.RevertSettingsToSnapshot();
        }
    }

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
        CancelAndClose();
    }

    public void CancelAndClose()
    {
        if (DataContext is MainViewModel vm && vm.HasPendingSettingsChanges)
        {
            vm.RevertSettingsToSnapshot();
        }
        Close();
    }

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var btn = sender as Button;
            string originalText = btn?.Content?.ToString() ?? "Применить";
            if (btn != null)
            {
                btn.Content = "✓ Применено!";
                btn.IsEnabled = false;
            }

            await vm.ApplyAndSaveSettingsAsync(updateSnapshot: false);
            await Task.Delay(650);

            vm.CaptureSettingsSnapshot();

            if (btn != null)
            {
                btn.Content = originalText;
                btn.IsEnabled = true;
            }
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelAndClose();
            e.Handled = true;
        }
    }
}
