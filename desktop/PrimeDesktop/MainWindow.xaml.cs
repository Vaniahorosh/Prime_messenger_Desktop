using System;
using System.Windows;
using System.Windows.Input;
using PrimeDesktop.Services;
using PrimeDesktop.ViewModels;
using Application = System.Windows.Application;

namespace PrimeDesktop;

public partial class MainWindow : Window
{
    public static SystemTrayService? TrayService { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        App.Log("MainWindow Initialized");
        Closing += (s, e) => App.Log($"MainWindow Closing (Cancel={e.Cancel})");
        Closed += (s, e) => App.Log("MainWindow Closed");

        TrayService = new SystemTrayService(this);
        Loaded += (s, e) =>
        {
            App.Log("MainWindow Loaded");
            if (DataContext is MainViewModel vm && vm.Settings.EnableScreenshotProtection)
            {
                SecurityManager.SetScreenshotProtection(this, true);
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

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
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
}
