using System;
using System.Diagnostics;
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

            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show($"UI Crash Prevented:\n{args.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    MessageBox.Show($"Background Crash Prevented:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            base.OnStartup(e);
        }
    }
}
