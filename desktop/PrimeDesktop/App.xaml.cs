using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PrimeDesktop;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrimeDesktop", "app.log");

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

        Log("PrimeDesktop application started.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"PrimeDesktop application exited with code {e.ApplicationExitCode}. Environment.StackTrace: {Environment.StackTrace}");
        base.OnExit(e);
    }
}
