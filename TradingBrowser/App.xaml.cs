using System;
using System.Windows;

namespace TradingBrowser
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // CATCH ALL UNHANDLED UI THREAD CRASHES
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show($"UI Crash Prevented:\n{args.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true; // Prevents app from closing!
            };

            // CATCH ALL BACKGROUND THREAD CRASHES (Like Favicon/Title updates)
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
