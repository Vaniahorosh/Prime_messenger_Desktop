using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

            // If existing installation is found in registry, prefill it
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

            if (_isUninstallMode)
            {
                ConfigureUninstallUi();
            }
        }

        private void ConfigureUninstallUi()
        {
            Title = "Удаление Prime Desktop";
            HeaderTitle.Text = "Prime Desktop";
            HeaderSubtitle.Text = "Удаление программы";
            StatusTitle.Text = "Удаление Prime Messenger с компьютера";
            StatusSub.Text = $"Приложение будет удалено из: {TargetDir}";
            DescriptionCard.Visibility = Visibility.Collapsed;
            PathPanel.Visibility = Visibility.Collapsed;
            ChkDesktopShortcut.Content = "Также удалить историю сообщений и профиль";
            ChkDesktopShortcut.IsChecked = true;
            ChkLaunchAfter.Visibility = Visibility.Collapsed;
            InstallBtn.Content = "Удалить с компьютера";
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

        private async void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isCompleted)
            {
                Close();
                return;
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

        private async Task HandleUninstallAsync()
        {
            InstallBtn.Content = "Удаление...";
            StatusTitle.Text = "Удаление файлов и системных ссылок...";
            InstallProgress.Value = 30;

            bool deleteUserData = ChkDesktopShortcut.IsChecked == true;

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
            // 1. Close running processes
            KillPrimeProcesses();

            // 2. Prepare directory
            Directory.CreateDirectory(TargetDir);

            // 3. Extract embedded payload
            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("app_files.zip", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("Файл полезной нагрузки инсталлятора не найден.");
            }

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

            // 4. Copy uninstaller (copying current setup exe to installation dir)
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

            // 5. Create Shortcuts
            string exePath = Path.Combine(TargetDir, "PrimeDesktop.exe");
            bool createDesktop = false;
            Dispatcher.Invoke(() => createDesktop = ChkDesktopShortcut.IsChecked == true);

            if (createDesktop)
            {
                string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(desktopFolder, "Prime Desktop.lnk"), exePath, TargetDir);
            }

            string startMenuPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            if (Directory.Exists(startMenuPrograms))
            {
                CreateShortcut(Path.Combine(startMenuPrograms, "Prime Desktop.lnk"), exePath, TargetDir);
            }

            // 6. Register in Windows Settings / Apps & features (Installed apps)
            RegisterInWindowsSettings(exePath, uninstallerPath);
        }

        private void PerformUninstallation(bool deleteUserData)
        {
            // 1. Kill processes
            KillPrimeProcesses();

            // 2. Remove Shortcuts
            string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Prime Desktop.lnk");
            if (File.Exists(desktopShortcut)) try { File.Delete(desktopShortcut); } catch { }

            string startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Prime Desktop.lnk");
            if (File.Exists(startMenuShortcut)) try { File.Delete(startMenuShortcut); } catch { }

            // 3. Unregister from Windows Settings
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PrimeMessenger", throwOnMissingSubKey: false);
            }
            catch { }

            // 4. Delete user data if requested
            if (deleteUserData)
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop");
                if (Directory.Exists(appData))
                {
                    try { Directory.Delete(appData, recursive: true); } catch { }
                }
            }

            // 5. Self-delete folder on exit
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
