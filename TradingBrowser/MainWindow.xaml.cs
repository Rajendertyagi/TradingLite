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
        private bool _isInitialized = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser_Fresh");
                Directory.CreateDirectory(folder);
                
                _environment = await CoreWebView2Environment.CreateAsync(null, folder, new CoreWebView2EnvironmentOptions 
                { 
                    AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-renderer-backgrounding" 
                });

                ViewModel.RequestNavigate += ViewModel_RequestNavigate;
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;

                if (ViewModel.SelectedTab != null) await ActivateTab(ViewModel.SelectedTab);
                
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                ActiveWebViewHost.Children.Add(new TextBlock 
                { 
                    Text = $"WebView2 Init Failed: {ex.Message}", 
                    Foreground = System.Windows.Media.Brushes.Red,
                    Margin = new Thickness(20)
                });
            }
        }

        private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedTab) && ViewModel.SelectedTab != null && _isInitialized)
            {
                await ActivateTab(ViewModel.SelectedTab);
            }
        }

        private async System.Threading.Tasks.Task ActivateTab(TabViewModel tab)
        {
            if (!_webViewPool.ContainsKey(tab.Id))
            {
                var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                ActiveWebViewHost.Children.Add(webView);

                await webView.EnsureCoreWebView2Async(_environment);

                _adBlocker.AttachToWebView(webView.CoreWebView2);

                webView.CoreWebView2.DocumentTitleChanged += (_, _) => tab.Title = webView.CoreWebView2.DocumentTitle;
                webView.CoreWebView2.SourceChanged += (_, _) => 
                {
                    if (ViewModel.SelectedTab?.Id == tab.Id)
                        ViewModel.AddressBarText = webView.Source.ToString();
                };

                webView.CoreWebView2.Navigate("https://www.tradingview.com/chart/");
                _webViewPool[tab.Id] = webView;
            }

            foreach (var wv in _webViewPool.Values)
            {
                wv.Visibility = Visibility.Hidden;
            }

            if (_webViewPool.TryGetValue(tab.Id, out var targetWebView))
            {
                targetWebView.Visibility = Visibility.Visible;
            }
        }

        // Drag window by grabbing empty tab strip space
        private void TabStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.Source is Border)
            {
                DragMove();
            }
        }

        private void Tab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab)
                ViewModel.SelectedTab = tab;
        }

        private void ViewModel_RequestNavigate(TabViewModel tab, string url)
        {
            if (_webViewPool.TryGetValue(tab.Id, out var webView))
            {
                webView.CoreWebView2.Navigate(url);
                tab.Url = url;
            }
        }

        private void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ViewModel.ExecuteNavigate();
                ((TextBox)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); 
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) 
                wv.CoreWebView2.GoBack();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) 
                wv.CoreWebView2.GoForward();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) 
                wv.CoreWebView2.Reload();
        }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
        private const int WM_SYSCOMMAND = 0x0112, SC_MAXIMIZE = 0xF030, SC_MINIMIZE = 0xF020;
        private void Minimize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MINIMIZE, 0);
        private void Maximize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MAXIMIZE, 0);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
