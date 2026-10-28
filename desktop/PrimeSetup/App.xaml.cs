using System;
using System.IO;
using System.Windows;

namespace PrimeSetup
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogCrash(args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash(args.Exception);
                args.Handled = false;
            };

            base.OnStartup(e);
        }

        private static void LogCrash(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                string log = Path.Combine(Path.GetTempPath(), "PrimeSetup_crash.log");
                File.AppendAllText(log, $"[{DateTime.Now}] {ex}\n\n");
            }
            catch { }
        }
    }
}
