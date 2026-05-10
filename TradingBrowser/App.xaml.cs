using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace TradingBrowser
{
    public partial class App : Application
    {
        private Mutex? _mutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, "Global\\TradingBrowser_SingleInstance_Mutex_987654321", out bool isNewInstance);
            
            if (!isNewInstance)
            {
                var currentProcess = Process.GetCurrentProcess();
                var runningProcesses = Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var process in runningProcesses)
                {
                    if (process.Id != currentProcess.Id)
                    {
                        IntPtr mainWindowHandle = process.MainWindowHandle;
                        if (mainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(mainWindowHandle, SW_RESTORE);
                            SetForegroundWindow(mainWindowHandle);
                        }
                        break;
                    }
                }
                Shutdown();
                return;
            }

            // FIX: Write all errors to a file on the Desktop for easy sharing
            DispatcherUnhandledException += (sender, args) =>
            {
                LogError($"UI Thread Error: {args.Exception.ToString()}");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                string error = args.ExceptionObject?.ToString() ?? "Unknown fatal error";
                LogError($"Background Thread Error: {error}");
            };

            base.OnStartup(e);
        }

        private void LogError(string message)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string logPath = Path.Combine(desktopPath, "TradingBrowser_Errors.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{message}\n\n");
            }
            catch { }
        }
    }
}
