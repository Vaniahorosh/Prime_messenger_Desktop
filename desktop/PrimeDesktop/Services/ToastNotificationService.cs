using System;
using System.Collections.Generic;
using System.Windows;
using PrimeDesktop.Models;
using PrimeDesktop.Views;

namespace PrimeDesktop.Services
{
    public static class ToastNotificationService
    {
        private static ToastNotificationWindow? _activeToast;

        public static void Show(ChatItem chat, string messageText, Action<ChatItem> onClick)
        {
            if (chat == null || Application.Current == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // Close previous toast if any to avoid cluttering screen
                    _activeToast?.Dismiss();

                    var toast = new ToastNotificationWindow(chat, messageText, onClick);
                    _activeToast = toast;
                    toast.Closed += (s, e) =>
                    {
                        if (_activeToast == toast) _activeToast = null;
                    };
                    toast.Show();
                }
                catch { }
            });
        }
    }
}
