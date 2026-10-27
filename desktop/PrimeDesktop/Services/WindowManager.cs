using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using PrimeDesktop.ViewModels;
using PrimeDesktop.Views;

namespace PrimeDesktop.Services;

public static class WindowManager
{
    private static SettingsWindow? _settingsWindow;
    private static PhotoViewerWindow? _photoViewerWindow;

    public static void OpenSettings(MainViewModel vm)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                    _settingsWindow.WindowState = WindowState.Normal;
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow
            {
                Owner = Application.Current?.MainWindow,
                DataContext = vm
            };

            _settingsWindow.Closed += (s, e) =>
            {
                _settingsWindow = null;
                vm.IsSettingsOpen = false;
            };

            vm.CaptureSettingsSnapshot();
            vm.IsSettingsOpen = true;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    public static void CloseSettings()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                _settingsWindow.Close();
            }
            _settingsWindow = null;
        });
    }

    public static void OpenPhotoViewer(string photoPath, MainViewModel? vm = null)
    {
        if (string.IsNullOrEmpty(photoPath) || !File.Exists(photoPath)) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            vm ??= Application.Current?.MainWindow?.DataContext as MainViewModel;

            var mediaList = new List<MediaViewerItem>();
            if (vm?.SelectedChat != null)
            {
                foreach (var msg in vm.SelectedChat.Messages)
                {
                    if (msg.IsCollage && msg.CollagePaths != null)
                    {
                        foreach (var cp in msg.CollagePaths)
                        {
                            if (File.Exists(cp))
                            {
                                mediaList.Add(new MediaViewerItem
                                {
                                    FilePath = cp,
                                    Sender = msg.Sender,
                                    FormattedTime = msg.FormattedTime,
                                    IsVideo = false,
                                    FileName = Path.GetFileName(cp)
                                });
                            }
                        }
                    }
                    else if (msg.IsPhoto && !string.IsNullOrEmpty(msg.FilePath) && File.Exists(msg.FilePath))
                    {
                        mediaList.Add(new MediaViewerItem
                        {
                            FilePath = msg.FilePath,
                            Sender = msg.Sender,
                            FormattedTime = msg.FormattedTime,
                            IsVideo = false,
                            FileName = msg.FileName ?? Path.GetFileName(msg.FilePath)
                        });
                    }
                    else if ((msg.IsVideoNote || PhotoViewerWindow.IsVideoPath(msg.FilePath)) && !string.IsNullOrEmpty(msg.FilePath) && File.Exists(msg.FilePath))
                    {
                        mediaList.Add(new MediaViewerItem
                        {
                            FilePath = msg.FilePath,
                            Sender = msg.Sender,
                            FormattedTime = msg.FormattedTime,
                            IsVideo = true,
                            FileName = msg.FileName ?? Path.GetFileName(msg.FilePath)
                        });
                    }
                }
            }

            OpenPhotoViewerWithItems(photoPath, mediaList, vm);
        });
    }

    public static void OpenPhotoViewerWithItems(string photoPath, List<MediaViewerItem> items, MainViewModel? vm = null)
    {
        if (string.IsNullOrEmpty(photoPath) || !File.Exists(photoPath)) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            vm ??= Application.Current?.MainWindow?.DataContext as MainViewModel;

            if (_photoViewerWindow != null && _photoViewerWindow.IsLoaded)
            {
                _photoViewerWindow.SetMediaList(photoPath, items);
                if (_photoViewerWindow.WindowState == WindowState.Minimized)
                    _photoViewerWindow.WindowState = WindowState.Normal;
                _photoViewerWindow.Activate();
                return;
            }

            _photoViewerWindow = new PhotoViewerWindow(photoPath, items)
            {
                Owner = Application.Current?.MainWindow,
                DataContext = vm
            };

            _photoViewerWindow.Closed += (s, e) =>
            {
                _photoViewerWindow = null;
                if (vm != null) vm.IsPhotoViewerOpen = false;
            };

            if (vm != null)
            {
                vm.ViewedPhotoPath = photoPath;
                vm.IsPhotoViewerOpen = true;
            }

            _photoViewerWindow.Show();
            _photoViewerWindow.Activate();
        });
    }

    public static void ClosePhotoViewer()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_photoViewerWindow != null && _photoViewerWindow.IsLoaded)
            {
                _photoViewerWindow.Close();
            }
            _photoViewerWindow = null;
        });
    }
}
