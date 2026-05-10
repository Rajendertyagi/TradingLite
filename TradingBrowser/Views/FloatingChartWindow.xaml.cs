using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace TradingBrowser.Views
{
    public partial class FloatingChartWindow : Window
    {
        private CoreWebView2Environment? _environment;
        private string _targetUrl;

        public FloatingChartWindow(CoreWebView2Environment env, string url)
        {
            InitializeComponent();
            _environment = env;
            _targetUrl = url;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
            FloatingHost.Children.Add(webView);
            await webView.EnsureCoreWebView2Async(_environment);
            webView.CoreWebView2.Navigate(_targetUrl);
            
            Left = SystemParameters.PrimaryScreenWidth - Width - 50;
            Top = SystemParameters.PrimaryScreenHeight - Height - 50;
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
