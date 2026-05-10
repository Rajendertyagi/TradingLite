using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace TradingBrowser
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Spec: Isolated storage & TradingView Chromium flags
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser");
            Directory.CreateDirectory(folder);

            string args = "--disable-background-timer-throttling --disable-renderer-backgrounding --enable-gpu-rasterization";
            var env = await CoreWebView2Environment.CreateAsync(null, folder, new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = args });

            // Create actual visible WebView2
            var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
            WebViewContainer.Child = webView;
            await webView.EnsureCoreWebView2Async(env);

            // Navigate to TradingView to test pre-warming and flags
            webView.CoreWebView2.Navigate("https://www.tradingview.com/chart/");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_MINIMIZE = 0xF020;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MINIMIZE, 0);
        private void Maximize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MAXIMIZE, 0);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
