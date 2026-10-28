using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PrimeDesktop;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrimeDesktop", "app.log");

    public const string AppMutexName = "Global\\PrimeDesktop_SingleInstance_Mutex_fa87c0d0";
    public const string RestoreMessageName = "PrimeDesktop_Restore_Message_fa87c0d0";

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
    public static readonly int WM_SHOWME = RegisterWindowMessage(RestoreMessageName);

    private static Mutex? _instanceMutex;

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"[FATAL UnhandledException] {args.ExceptionObject}");
        };
        Log("App constructor entered.");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, AppMutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            Log("Another instance of PrimeDesktop is already running. Signaling existing instance and exiting.");
            try
            {
                if (WM_SHOWME != 0)
                {
                    PostMessage(HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Log($"Error signaling existing instance: {ex.Message}");
            }

            // Immediately exit the duplicate process
            Shutdown();
            return;
        }

        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"[FATAL UnhandledException] {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"[FATAL DispatcherException] {args.Exception}");
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log($"[WARN UnobservedTaskException] {args.Exception}");
            args.SetObserved();
        };

        Log("PrimeDesktop application started (primary single instance).");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"PrimeDesktop application exited with code {e.ApplicationExitCode}.");
        if (_instanceMutex != null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch { }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        base.OnExit(e);
    }
}
