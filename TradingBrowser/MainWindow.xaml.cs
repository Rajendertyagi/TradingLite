using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using TradingBrowser.Services;
using TradingBrowser.ViewModels;

namespace TradingBrowser
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        private readonly Dictionary<Guid, Microsoft.Web.WebView2.Wpf.WebView2> _webViewPool = new();
        private readonly AdBlockService _adBlocker = new AdBlockService();
        private CoreWebView2Environment? _environment;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Initialize Core Engine
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser");
            Directory.CreateDirectory(folder);
            string args = "--disable-background-timer-throttling --disable-renderer-backgrounding --enable-gpu-rasterization";
            _environment = await CoreWebView2Environment.CreateAsync(null, folder, new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = args });

            // 2. Listen for Tab changes
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // 3. Show the initial tab
            if (ViewModel.SelectedTab != null) await ActivateTab(ViewModel.SelectedTab);
        }

        private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedTab) && ViewModel.SelectedTab != null)
            {
                await ActivateTab(ViewModel.SelectedTab);
            }
        }

        private async System.Threading.Tasks.Task ActivateTab(TabViewModel tab)
        {
            // If we don't have a WebView for this tab yet, create one
            if (!_webViewPool.ContainsKey(tab.Id))
            {
                var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                await webView.EnsureCoreWebView2Async(_environment);

                // Attach Ad-Blocker
                _adBlocker.AttachToWebView(webView.CoreWebView2);

                // Update MVVM when title changes
                webView.CoreWebView2.DocumentTitleChanged += (_, _) => tab.Title = webView.CoreWebView2.DocumentTitle;

                // Default navigation
                webView.CoreWebView2.Navigate("https://www.tradingview.com/chart/");

                // Put in hidden pool to keep renderer alive
                HiddenWebViewPool.Children.Add(webView);
                _webViewPool[tab.Id] = webView;
            }

            // Move the selected tab's WebView to the active visual area
            var targetWebView = _webViewPool[tab.Id];
            
            if (HiddenWebViewPool.Children.Contains(targetWebView))
                HiddenWebViewPool.Children.Remove(targetWebView);

            ActiveWebViewHost.Content = targetWebView;
        }

        // Tab Click UI logic
        private void Tab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab)
                ViewModel.SelectedTab = tab;
        }

        // Window Chrome Interop
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
        private const int WM_SYSCOMMAND = 0x0112, SC_MAXIMIZE = 0xF030, SC_MINIMIZE = 0xF020;
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MINIMIZE, 0);
        private void Maximize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MAXIMIZE, 0);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
