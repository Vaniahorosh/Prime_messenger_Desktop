using System;
using System.Windows;
using System.Windows.Input;
using PrimeDesktop.Services;

namespace PrimeDesktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        StateChanged += Window_StateChanged;
        Loaded += Window_Loaded;
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyWindowDwm(this);
        WindowUtils.EnableWindowMinMaxHook(this);
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
        Close();
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
            Close();
            e.Handled = true;
        }
    }
}
