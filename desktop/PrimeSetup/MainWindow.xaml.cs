using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PrimeSetup
{
    public partial class MainWindow : Window
    {
        private bool _isCompleted = false;
        private readonly bool _isUninstallMode = false;
        private string _targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "PrimeMessenger");

        private string? _chosenAvatarPath = null;
        private DispatcherTimer? _sloganTimer;
        private int _currentSloganIndex = 0;

        private record SloganItem(string Prefix, string Button, string Subtitle, string Desc, string HexColor);

        private readonly List<SloganItem> _slogans = new()
        {
            new("Всегда будь в", "Прайме!", "Мессенджер нового поколения", "Приватный и свободный мессенджер для общения по Bluetooth P2P и онлайн.", "#38BDF8"),
            new("Всегда сообщение", "Быстрее!", "Мгновенная доставка & Скорость", "Мгновенная отправка сообщений и медиафайлов с плавным откликом интерфейса.", "#22C55E"),
            new("Всегда будь на", "Связи!", "Связь без интернета & Контакты", "Общайся без интернета по Bluetooth P2P или онлайн, используй глобальный поиск.", "#EAB308"),
            new("Всегда будь в", "Приватности!", "Анонимность & Шифрование", "Защищенная локальная база данных, анонимизация устройств и защита от посторонних.", "#EC4899"),
            new("Всегда мы", "Кастомнее!", "Лавовый фон & Персонализация", "Живой лавовый фон, Material 3 акценты, медиаплеер и редактор изображений.", "#A855F7")
        };

        public string TargetDir
        {
            get => _targetDir;
            set => _targetDir = value;
        }

        public MainWindow()
        {
            InitializeComponent();

            string[] args = Environment.GetCommandLineArgs();
            _isUninstallMode = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                             a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrimeMessenger");
                string? loc = key?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(loc))
                {
                    TargetDir = loc;
                }
            }
            catch { }

            TxtInstallPath.Text = TargetDir;
            TxtDisplayName.Text = Environment.UserName;
            TxtUsername.Text = Environment.UserName.ToLowerInvariant().Replace(" ", "_");

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(infoVer))
                {
                    TxtAppVersion.Text = $"v{infoVer}";
                }
            }
            catch { }

            InitSloganCarousel();

            if (_isUninstallMode)
            {
                ConfigureUninstallUi();
            }
        }

        private void InitSloganCarousel()
        {
            _sloganTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.5)
            };
            _sloganTimer.Tick += (s, e) => AnimateToNextSlogan();
            _sloganTimer.Start();
        }

        private void AnimateToNextSlogan()
        {
            // Slide down and fade out
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(220));
            var slideOut = new DoubleAnimation(0.0, 16.0, TimeSpan.FromMilliseconds(220));

            slideOut.Completed += (s, e) =>
            {
                _currentSloganIndex = (_currentSloganIndex + 1) % _slogans.Count;
                var next = _slogans[_currentSloganIndex];

                TxtSloganPrefix.Text = next.Prefix;
                TxtSloganButton.Text = next.Button;
                TxtSloganButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(next.HexColor));
                TxtSloganSubtitle.Text = next.Subtitle;
                TxtSloganDesc.Text = next.Desc;

                // Prepare for entry from top
                SloganTranslate.Y = -16.0;
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(260));
                var slideIn = new DoubleAnimation(-16.0, 0.0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                SloganContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                SloganTranslate.BeginAnimation(TranslateTransform.YProperty, slideIn);
            };

            SloganContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            SloganTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
        }

        private void ConfigureUninstallUi()
        {
            Title = "Удаление Prime Desktop";
            Width = 560;
            Height = 440;

            if (_sloganTimer != null)
            {
                _sloganTimer.Stop();
            }

            SetupModeGrid.Visibility = Visibility.Collapsed;
            UninstallModeCard.Visibility = Visibility.Visible;

            StatusTitle.Text = "Готово к удалению";
            StatusSub.Text = $"Программа будет полностью удалена из: {TargetDir}";

            InstallBtn.Content = "Удалить";
            InstallBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Выберите папку для установки Prime Desktop",
                InitialDirectory = Directory.Exists(TxtInstallPath.Text)
                    ? TxtInstallPath.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };

            if (dlg.ShowDialog() == true)
            {
                string selected = dlg.FolderName;
                if (!selected.EndsWith("PrimeMessenger", StringComparison.OrdinalIgnoreCase) &&
                    !selected.EndsWith("Prime Desktop", StringComparison.OrdinalIgnoreCase))
                {
                    selected = Path.Combine(selected, "PrimeMessenger");
                }
                TxtInstallPath.Text = selected;
                TargetDir = selected;
            }
        }

        private void AvatarBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            BtnPickAvatar_Click(sender, e);
        }

        private void BtnPickAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите фотографию для профиля Prime",
                Filter = "Изображения (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Все файлы (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                var cropper = new AvatarCropperDialog(dlg.FileName)
                {
                    Owner = this
                };

                if (cropper.ShowDialog() == true && !string.IsNullOrEmpty(cropper.CroppedResultPath))
                {
                    _chosenAvatarPath = cropper.CroppedResultPath;
                    DisplayAvatarPreview(_chosenAvatarPath);
                }
            }
        }

        private void DisplayAvatarPreview(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();

                AvatarPreviewImage.Source = bmp;
                AvatarPreviewImage.Visibility = Visibility.Visible;
                AvatarPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private void InputFields_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ValidateInputs(showErrors: false);
        }

        private bool ValidateInputs(bool showErrors = true)
        {
            string name = TxtDisplayName?.Text?.Trim() ?? "";
            string username = TxtUsername?.Text?.Trim() ?? "";

            string? nameError = PrimeCommon.ValidationUtils.ValidateName(name);
            string? usernameError = PrimeCommon.ValidationUtils.ValidateLogin(username);

            string? firstError = nameError ?? usernameError;

            if (firstError != null)
            {
                if (showErrors && TxtValidationError != null)
                {
                    TxtValidationError.Text = firstError;
                    TxtValidationError.Visibility = Visibility.Visible;
                }

                if (BorderDisplayName != null)
                {
                    BorderDisplayName.BorderBrush = nameError != null 
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));
                }

                if (BorderUsername != null)
                {
                    BorderUsername.BorderBrush = usernameError != null 
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));
                }

                return false;
            }

            if (TxtValidationError != null)
            {
                TxtValidationError.Text = "";
                TxtValidationError.Visibility = Visibility.Collapsed;
            }

            if (BorderDisplayName != null)
                BorderDisplayName.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));
            if (BorderUsername != null)
                BorderUsername.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));

            return true;
        }

        private async void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isCompleted)
            {
                Close();
                return;
            }

            if (!_isUninstallMode)
            {
                if (!ValidateInputs(showErrors: true))
                {
                    return;
                }
            }

            InstallBtn.IsEnabled = false;

            if (_isUninstallMode)
            {
                await HandleUninstallAsync();
            }
            else
            {
                string chosen = TxtInstallPath.Text.Trim();
                if (!string.IsNullOrEmpty(chosen))
                {
                    TargetDir = chosen;
                }
                BtnBrowse.IsEnabled = false;
                TxtInstallPath.IsEnabled = false;
                await HandleInstallAsync();
            }
        }

        private async Task HandleInstallAsync()
        {
            AnimateExpansionToFull();

            InstallBtn.Content = "Установка...";
            StatusTitle.Text = "Распаковка файлов Prime Messenger...";
            InstallProgress.Value = 20;

            try
            {
                await Task.Run(() => PerformInstallation());

                InstallProgress.Value = 100;
                StatusTitle.Text = "Установка успешно завершена!";
                StatusSub.Text = $"Prime Messenger установлен в:\n{TargetDir}";
                InstallBtn.IsEnabled = true;
                InstallBtn.Content = "Завершить";
                _isCompleted = true;

                if (ChkLaunchAfter.IsChecked == true)
                {
                    string exePath = Path.Combine(TargetDir, "PrimeDesktop.exe");
                    if (File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTitle.Text = "Ошибка установки";
                StatusSub.Text = ex.Message;
                InstallBtn.IsEnabled = true;
                InstallBtn.Content = "Повторить";
                BtnBrowse.IsEnabled = true;
                TxtInstallPath.IsEnabled = true;
            }
        }

        private void AnimateExpansionToFull()
        {
            try
            {
                var fadeOutRight = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };

                fadeOutRight.Completed += (s, e) =>
                {
                    RightPanelContainer.Visibility = Visibility.Collapsed;
                    ColGap.Width = new GridLength(0);
                    ColRight.Width = new GridLength(0);
                    ColLeft.Width = new GridLength(1, GridUnitType.Star);
                };

                RightPanelContainer.BeginAnimation(UIElement.OpacityProperty, fadeOutRight);
            }
            catch
            {
                RightPanelContainer.Visibility = Visibility.Collapsed;
                ColGap.Width = new GridLength(0);
                ColRight.Width = new GridLength(0);
                ColLeft.Width = new GridLength(1, GridUnitType.Star);
            }
        }

        private async Task HandleUninstallAsync()
        {
            InstallBtn.Content = "Удаление...";
            StatusTitle.Text = "Удаление файлов и системных ссылок...";
            InstallProgress.Value = 30;

            bool deleteUserData = ChkDeleteUserData.IsChecked == true;

            try
            {
                await Task.Run(() => PerformUninstallation(deleteUserData));

                InstallProgress.Value = 100;
                StatusTitle.Text = "Prime Desktop успешно удален";
                StatusSub.Text = "Все компоненты приложения были удалены из системы.";
                InstallBtn.IsEnabled = true;
                InstallBtn.Content = "Закрыть";
                _isCompleted = true;
            }
            catch (Exception ex)
            {
                StatusTitle.Text = "Ошибка при удалении";
                StatusSub.Text = ex.Message;
                InstallBtn.IsEnabled = true;
                InstallBtn.Content = "Закрыть";
                _isCompleted = true;
            }
        }

        private void PerformInstallation()
        {
            KillPrimeProcesses();
            Directory.CreateDirectory(TargetDir);

            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("app_files.zip", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(resourceName))
            {
                string tempZip = Path.Combine(Path.GetTempPath(), $"prime_inst_{Guid.NewGuid():N}.zip");
                try
                {
                    using (var stream = assembly.GetManifestResourceStream(resourceName)!)
                    using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fs);
                    }

                    Dispatcher.Invoke(() => { InstallProgress.Value = 60; StatusTitle.Text = "Развертывание программы..."; });
                    ZipFile.ExtractToDirectory(tempZip, TargetDir, overwriteFiles: true);
                }
                finally
                {
                    if (File.Exists(tempZip))
                    {
                        try { File.Delete(tempZip); } catch { }
                    }
                }
            }

            string uninstallerPath = Path.Combine(TargetDir, "PrimeSetup.exe");
            try
            {
                string? currentModule = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(currentModule) && File.Exists(currentModule))
                {
                    if (!currentModule.Equals(uninstallerPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(currentModule, uninstallerPath, overwrite: true);
                    }
                }
            }
            catch { }

            Dispatcher.Invoke(() => { InstallProgress.Value = 85; StatusTitle.Text = "Регистрация в Windows и создание ярлыков..."; });

            string exePath = Path.Combine(TargetDir, "PrimeDesktop.exe");
            bool createDesktop = false;
            bool createStartMenu = false;
            bool enableAutostart = false;

            Dispatcher.Invoke(() =>
            {
                createDesktop = ChkDesktopShortcut.IsChecked == true;
                createStartMenu = ChkStartMenuShortcut.IsChecked == true;
                enableAutostart = ChkAutostart.IsChecked == true;
            });

            if (createDesktop)
            {
                string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desktopFolder, "Prime Desktop.lnk"), exePath, TargetDir);
            }

            if (createStartMenu)
            {
                string startMenuPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                if (Directory.Exists(startMenuPrograms))
                {
                    CreateShortcut(Path.Combine(startMenuPrograms, "Prime Desktop.lnk"), exePath, TargetDir);
                }
            }

            // Register/remove autostart in Windows Run registry key
            ConfigureAutostart(exePath, enableAutostart);

            RegisterInWindowsSettings(exePath, uninstallerPath);

            // 7. Save initial profile and settings
            SaveInitialUserProfile();
        }

        private static void ConfigureAutostart(string exePath, bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key != null)
                {
                    if (enable)
                    {
                        key.SetValue("PrimeDesktop", $"\"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue("PrimeDesktop", throwOnMissingValue: false);
                    }
                }
            }
            catch { }
        }

        private void PerformUninstallation(bool deleteUserData)
        {
            KillPrimeProcesses();

            string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Prime Desktop.lnk");
            if (File.Exists(desktopShortcut)) try { File.Delete(desktopShortcut); } catch { }

            string startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Prime Desktop.lnk");
            if (File.Exists(startMenuShortcut)) try { File.Delete(startMenuShortcut); } catch { }

            // Remove Autostart
            ConfigureAutostart("", false);

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrimeMessenger", throwOnMissingSubKey: false);
            }
            catch { }

            if (deleteUserData)
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop");
                if (Directory.Exists(appData))
                {
                    try { Directory.Delete(appData, recursive: true); } catch { }
                }
            }

            try
            {
                string cmd = $"/c timeout /t 1 /nobreak > nul & rmdir /s /q \"{TargetDir}\"";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmd,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch { }
        }

        private static void KillPrimeProcesses()
        {
            foreach (var p in Process.GetProcessesByName("PrimeDesktop"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        private void RegisterInWindowsSettings(string exePath, string uninstallerPath)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrimeMessenger");
                if (key != null)
                {
                    key.SetValue("DisplayName", "Prime Desktop");
                    key.SetValue("DisplayVersion", "0.1 Desk");
                    key.SetValue("Publisher", "Prime Team");
                    key.SetValue("InstallLocation", TargetDir);
                    key.SetValue("DisplayIcon", $"{exePath},0");
                    key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                    key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall");
                    key.SetValue("EstimatedSize", 240000, RegistryValueKind.DWord);
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("URLInfoAbout", "https://github.com/Vaniahorosh/Prime_messenger");
                }
            }
            catch { }
        }

        private void SaveInitialUserProfile()
        {
            try
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop");
                Directory.CreateDirectory(appDataDir);
                string avatarsDir = Path.Combine(appDataDir, "Avatars");
                Directory.CreateDirectory(avatarsDir);

                string myDisplayName = Environment.UserName;
                string myUsername = Environment.UserName.ToLowerInvariant().Replace(" ", "_");

                Dispatcher.Invoke(() =>
                {
                    string name = TxtDisplayName.Text.Trim();
                    string user = TxtUsername.Text.Trim();
                    if (!string.IsNullOrEmpty(name)) myDisplayName = name;
                    if (!string.IsNullOrEmpty(user)) myUsername = user;
                });

                string? finalAvatarPath = null;
                if (!string.IsNullOrEmpty(_chosenAvatarPath) && File.Exists(_chosenAvatarPath))
                {
                    finalAvatarPath = Path.Combine(avatarsDir, "avatar_my_profile.png");
                    File.Copy(_chosenAvatarPath, finalAvatarPath, overwrite: true);
                }

                string settingsPath = Path.Combine(appDataDir, "settings.json");
                string json = "{}";

                if (File.Exists(settingsPath))
                {
                    try { json = File.ReadAllText(settingsPath); } catch { }
                }

                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();

                dict["MyDisplayName"] = myDisplayName;
                dict["MyUsername"] = myUsername;
                if (!string.IsNullOrEmpty(finalAvatarPath))
                {
                    dict["MyAvatarPath"] = finalAvatarPath;
                }

                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(dict, options));
            }
            catch { }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        shortcut.TargetPath = targetPath;
                        shortcut.WorkingDirectory = workingDir;
                        shortcut.IconLocation = $"{targetPath},0";
                        shortcut.Description = "Prime Messenger - Защищенный мессенджер";
                        shortcut.Save();
                    }
                }
            }
            catch { }
        }
    }
}
