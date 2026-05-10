using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using TradingBrowser.Services;
using TradingBrowser.ViewModels;
using TradingBrowser.Views;
using Microsoft.Win32;

namespace TradingBrowser
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        private readonly Dictionary<Guid, Microsoft.Web.WebView2.Wpf.WebView2> _webViewPool = new();
        private readonly AdBlockService _adBlocker = new AdBlockService();
        private CoreWebView2Environment? _environment;
        private bool _isInitialized = false;
        
        private readonly string _sessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser_Fresh", "session.json");

        private TabViewModel? _draggedTab;
        private Point _startPoint;

        public MainWindow()
        {
            InitializeComponent();
            // FIX: CS8604 - Provide valid dummy sender
            ViewModel.ReloadCommand = new RelayCommand(_ => Reload_Click(this, new RoutedEventArgs()));
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser_Fresh");
            Directory.CreateDirectory(folder);
            
            _environment = await CoreWebView2Environment.CreateAsync(null, folder, new CoreWebView2EnvironmentOptions 
            { AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-renderer-backgrounding" });

            ViewModel.RequestNavigate += ViewModel_RequestNavigate;
            ViewModel.RequestFocusAddressBar += () => { AddressBox.Focus(); AddressBox.SelectAll(); };
            ViewModel.TabClosed += ViewModel_TabClosed;
            ViewModel.TabPopOut += ViewModel_TabPopOut;
            ViewModel.TabScreenshot += ViewModel_TabScreenshot;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            _isInitialized = true;
            RestoreSession();
        }

        private void ViewModel_TabPopOut(TabViewModel tab)
        {
            if (_webViewPool.TryGetValue(tab.Id, out var wv) && _environment != null)
            {
                var popOut = new FloatingChartWindow(_environment, wv.Source.ToString());
                popOut.Show();
            }
        }

        private async void ViewModel_TabScreenshot(TabViewModel tab)
        {
            if (_webViewPool.TryGetValue(tab.Id, out var wv))
            {
                try
                {
                    string jsonResult = await wv.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\"}");
                    using JsonDocument doc = JsonDocument.Parse(jsonResult);
                    
                    // FIX: CS8600 & CS8604 - Safely handle potential nulls from JSON
                    if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String)
                    {
                        string? base64 = dataElement.GetString();
                        if (!string.IsNullOrEmpty(base64))
                        {
                            byte[] imageBytes = Convert.FromBase64String(base64);
                            SaveFileDialog saveDialog = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = $"{tab.Title}_screenshot.png" };
                            if (saveDialog.ShowDialog() == true)
                            {
                                File.WriteAllBytes(saveDialog.FileName, imageBytes);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void ViewModel_TabClosed(TabViewModel tab)
        {
            if (_webViewPool.TryGetValue(tab.Id, out var webView))
            {
                ActiveWebViewHost.Children.Remove(webView);
                webView.Dispose(); 
                _webViewPool.Remove(tab.Id);
            }
        }

        protected override void OnClosed(EventArgs e) { SaveSession(); base.OnClosed(e); }

        private void SaveSession()
        {
            try {
                var sessionData = new List<string>();
                foreach (var tab in ViewModel.Tabs) {
                    if (_webViewPool.TryGetValue(tab.Id, out var wv)) sessionData.Add(wv.Source.ToString());
                    else if (tab.Url != "homemarket://") sessionData.Add(tab.Url);
                }
                File.WriteAllText(_sessionPath, JsonSerializer.Serialize(sessionData));
            } catch { }
        }

        private void RestoreSession()
        {
            if (File.Exists(_sessionPath)) {
                try {
                    var urls = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_sessionPath));
                    if (urls != null && urls.Count > 0) { foreach (var url in urls) ViewModel.AddTab(url); return; }
                } catch { }
            }
            ViewModel.AddTab();
        }

        private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedTab) && ViewModel.SelectedTab != null && _isInitialized)
                await ActivateTab(ViewModel.SelectedTab);
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
                webView.CoreWebView2.FaviconChanged += async (_, _) => {
                    try { var stream = await webView.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png); var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = stream; bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze(); tab.Favicon = bitmap; } catch { }
                };
                webView.CoreWebView2.SourceChanged += (_, _) => { if (ViewModel.SelectedTab?.Id == tab.Id) ViewModel.AddressBarText = webView.Source.ToString(); };
                webView.CoreWebView2.WebMessageReceived += (s, args) => { var url = args.TryGetWebMessageAsString(); if (!string.IsNullOrEmpty(url)) ViewModel_RequestNavigate(tab, url
