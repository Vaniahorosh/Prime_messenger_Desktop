using System;
using System.IO;
using System.Windows;
using PrimeDesktop.ViewModels;
using PrimeDesktop.Views;

namespace PrimeDesktop.Services;

public static class WindowManager
{
    private static PhotoViewerWindow? _photoViewerWindow;

    public static void OpenPhotoViewer(string photoPath, MainViewModel? vm = null)
    {
        if (string.IsNullOrEmpty(photoPath) || !File.Exists(photoPath)) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            vm ??= Application.Current?.MainWindow?.DataContext as MainViewModel;

            if (_photoViewerWindow != null && _photoViewerWindow.IsLoaded)
            {
                _photoViewerWindow.LoadPhoto(photoPath);
                if (_photoViewerWindow.WindowState == WindowState.Minimized)
                    _photoViewerWindow.WindowState = WindowState.Normal;
                _photoViewerWindow.Activate();
                return;
            }

            _photoViewerWindow = new PhotoViewerWindow(photoPath)
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
