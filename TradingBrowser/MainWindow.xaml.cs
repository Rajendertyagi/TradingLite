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
            ViewModel.ReloadCommand = new RelayCommand(_ => Reload_Click(this, new RoutedEventArgs()));
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser_Fresh");
            Directory.CreateDirectory(folder);
            _environment = await CoreWebView2Environment.CreateAsync(null, folder, new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-renderer-backgrounding" });

            ViewModel.RequestNavigate += ViewModel_RequestNavigate;
            ViewModel.RequestFocusAddressBar += () => { AddressBox.Focus(); AddressBox.SelectAll(); };
            ViewModel.TabClosed += ViewModel_TabClosed;
            ViewModel.TabPopOut += ViewModel_TabPopOut;
            ViewModel.TabScreenshot += ViewModel_TabScreenshot;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.ClearCacheRequested += () => ClearCache();

            // Hook Shield Toggle to AdBlocker
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.IsSiteWhitelisted) && ViewModel.SelectedTab != null)
                    _adBlocker.ToggleWhitelist(ViewModel.SelectedTab.Url);
            };

            _isInitialized = true;
            RestoreSession();
        }

        private void ClearCache()
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser_Fresh");
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                MessageBox.Show("Cache and Cookies cleared!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
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
                    if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String)
                    {
                        string? base64 = dataElement.GetString();
                        if (!string.IsNullOrEmpty(base64))
                        {
                            byte[] imageBytes = Convert.FromBase64String(base64);
                            SaveFileDialog saveDialog = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = $"{tab.Title}_screenshot.png" };
                            if (saveDialog.ShowDialog() == true) File.WriteAllBytes(saveDialog.FileName, imageBytes);
                        }
                    }
                } catch { }
            }
        }

        private void ViewModel_TabClosed(TabViewModel tab)
        {
            if (_webViewPool.TryGetValue(tab.Id, out var webView))
            {
                try { ActiveWebViewHost.Children.Remove(webView); webView.Dispose(); } catch { }
                _webViewPool.Remove(tab.Id);
            }
        }

        protected override void OnClosed(EventArgs e) { try { SaveSession(); } catch { } base.OnClosed(e); }

        private void SaveSession()
        {
            try {
                var sessionData = new List<string>();
                foreach (var tab in ViewModel.Tabs) { try { if (_webViewPool.TryGetValue(tab.Id, out var wv)) sessionData.Add(wv.Source?.ToString() ?? tab.Url); else if (tab.Url != "homemarket://" && tab.Url != "settings://") sessionData.Add(tab.Url); } catch { } }
                File.WriteAllText(_sessionPath, JsonSerializer.Serialize(sessionData));
            } catch { }
        }

        private void RestoreSession()
        {
            if (File.Exists(_sessionPath)) {
                try { var urls = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_sessionPath)); if (urls != null && urls.Count > 0) { foreach (var url in urls) ViewModel.AddTab(url); return; } } catch { }
            }
            ViewModel.AddTab();
        }

        private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedTab) && ViewModel.SelectedTab != null && _isInitialized)
            {
                await ActivateTab(ViewModel.SelectedTab);
                // Update Shield Icon State
                ViewModel.IsSiteWhitelisted = _adBlocker.IsDomainWhitelisted(ViewModel.SelectedTab.Url);
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

                webView.CoreWebView2.DocumentTitleChanged += (_, _) => { Application.Current.Dispatcher.Invoke(() => { try { tab.Title = webView.CoreWebView2.DocumentTitle; } catch { } }); };
                webView.CoreWebView2.FaviconChanged += async (_) => { try { var stream = await webView.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png); var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = stream; bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze(); Application.Current.Dispatcher.Invoke(() => { try { tab.Favicon = bitmap; } catch { } }); } catch { } };
                webView.CoreWebView2.SourceChanged += (_, _) => { Application.Current.Dispatcher.Invoke(() => { if (ViewModel.SelectedTab?.Id == tab.Id) { try { ViewModel.AddressBarText = webView.Source.ToString(); } catch { } } }); };
                webView.CoreWebView2.WebMessageReceived += (s, args) => { var url = args.TryGetWebMessageAsString(); if (!string.IsNullOrEmpty(url)) { if (url == "clearcache") ClearCache(); else ViewModel_RequestNavigate(tab, url); } };
                
                webView.CoreWebView2.NewWindowRequested += (s, args) => { try { args.Handled = true; ViewModel.AddTab(args.Uri); } catch { } };

                // FEATURE: Download Manager
                webView.CoreWebView2.DownloadStarting += (s, args) =>
                {
                    var download = args.DownloadOperation;
                    var item = new DownloadItem { FileName = Path.GetFileName(download.ResultFilePath), FilePath = download.ResultFilePath };
                    download.BytesReceivedChanged += (_, _) => { Application.Current.Dispatcher.Invoke(() => { item.BytesReceived = download.BytesReceived; item.TotalBytes = download.TotalBytesToReceive; }); };
                    download.StateChanged += (_, _) => { if (download.State == CoreWebView2DownloadState.Completed || download.State == CoreWebView2DownloadState.Interrupted) Application.Current.Dispatcher.Invoke(() => item.IsComplete = true); };
                    Application.Current.Dispatcher.Invoke(() => ViewModel.Downloads.Add(item));
                };

                // FEATURE: TradingView Quick Shortcuts (Ctrl+Alt+1..5)
                if (tab.Url.Contains("tradingview.com"))
                {
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                        document.addEventListener('keydown', function(e) {
                            if (e.ctrlKey && e.altKey) {
                                let tf = null;
                                if(e.key==='1') tf='1'; if(e.key==='2') tf='5'; if(e.key==='3') tf='15';
                                if(e.key==='4') tf='30'; if(e.key==='5') tf='60'; if(e.key==='6') tf='120'; if(e.key==='7') tf='D';
                                if(tf) {
                                    e.preventDefault(); e.stopPropagation();
                                    let btns = document.querySelectorAll('button[name="timeInterval"]');
                                    btns.forEach(b => { if(b.getAttribute('data-value') === tf) b.click(); });
                                }
                            }
                        });");
                }

                _webViewPool[tab.Id] = webView;
                await System.Threading.Tasks.Task.Delay(50);

                if (tab.Url == "homemarket://") LoadHomePage(webView);
                else if (tab.Url == "settings://") LoadSettingsPage(webView);
                else webView.CoreWebView2.Navigate(tab.Url);
            }

            foreach (var wv in _webViewPool.Values) { try { wv.Visibility = Visibility.Hidden; MuteTab(wv, true); } catch { } }
            if (_webViewPool.TryGetValue(tab.Id, out var targetWebView)) { try { targetWebView.Visibility = Visibility.Visible; MuteTab(targetWebView, false); } catch { } }
        }

        private void LoadSettingsPage(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            string html = @"<html><head><style>body{background-color:#202124;color:#e8eaed;font-family:'Segoe UI',sans-serif;padding:40px;} h2{margin-top:0;} button{padding:12px 20px;background:#0078d4;color:white;border:none;border-radius:4px;cursor:pointer;font-size:14px;margin-top:20px;} button:hover{background:#1a86d9;} .shortcut{background:#35363a;padding:10px;margin-top:10px;border-radius:4px;}</style></head><body>
            <h2>TradingBrowser Settings</h2>
            <p><strong>Ad-Blocker:</strong> Active. Use the 🛡️ icon in the address bar to whitelist specific sites.</p>
            <p><strong>Quick Menu (TradingView):</strong></p>
            <div class='shortcut'>Ctrl + Alt + 1 &rarr; 1 Minute</div>
            <div class='shortcut'>Ctrl + Alt + 2 &rarr; 5 Minutes</div>
            <div class='shortcut'>Ctrl + Alt + 3 &rarr; 15 Minutes</div>
            <div class='shortcut'>Ctrl + Alt + 4 &rarr; 30 Minutes</div>
            <div class='shortcut'>Ctrl + Alt + 5 &rarr; 1 Hour</div>
            <div class='shortcut'>Ctrl + Alt + 6 &rarr; 2 Hours</div>
            <div class='shortcut'>Ctrl + Alt + 7 &rarr; 1 Day</div>
            <button onclick=""window.chrome.webview.postMessage('clearcache')"">Clear All Cache & Cookies</button>
            </body></html>";
            webView.CoreWebView2.NavigateToString(html);
        }

        private void MuteTab(Microsoft.Web.WebView2.Wpf.WebView2 webView, bool mute) { try { if(webView.CoreWebView2!=null) webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Audio.setMuted", mute ? "{\"muted\":true}" : "{\"muted\":false}").ConfigureAwait(false); } catch { } }
        private void LoadHomePage(Microsoft.Web.WebView2.Wpf.WebView2 webView) { webView.CoreWebView2.NavigateToString(@"<html><head><style>body{background-color:#1e1e1e;color:white;font-family:'Segoe UI',sans-serif;display:flex;flex-direction:column;align-items:center;margin-top:15%}.brokers{display:flex;gap:20px;margin-top:30px}.broker-card{background:#2d2d2d;padding:20px;border-radius:10px;text-align:center;cursor:pointer;transition:0.2s;text-decoration:none;color:white}.broker-card:hover{background:#3d3d3d;transform:scale(1.05)}input[type='text']{margin-top:40px;padding:12px 20px;width:500px;border-radius:25px;border:1px solid #444;background:#333;color:white;font-size:16px;outline:none}input[type='text']:focus{border-color:#0078d4}</style></head><body><h2>Indian Markets Quick Access</h2><div class='brokers'><a class='broker-card' href='https://kite.zerodha.com'><h3 style='color:#387ed1'>Zerodha</h3></a><a class='broker-card' href='https://pro.upstox.com'><h3 style='color:#5e72e4'>Upstox</h3></a><a class='broker-card' href='https://app.groww.in'><h3 style='color:#00d09c'>Groww</h3></a><a class='broker-card' href='https://trade.angelone.in'><h3 style='color:#e8375d'>Angel One</h3></a><a class='broker-card' href='https://www.icicidirect.com'><h3 style='color:#f37021'>ICICI Direct</h3></a></div><input type='text' id='searchbox' placeholder='Search Google or type a URL... (e.g. tv RELIANCE)' autofocus><script>document.getElementById('searchbox').addEventListener('keydown',function(e){if(e.key==='Enter'){let val=this.value;if(!val.includes('.')||val.includes(' '))val='https://www.google.com/search?q='+encodeURIComponent(val);else if(!val.startsWith('http'))val='https://'+val;window.chrome.webview.postMessage(val)}});document.querySelectorAll('.broker-card').forEach(a=>{a.addEventListener('click',function(e){e.preventDefault();window.chrome.webview.postMessage(this.href)})});</script></body></html>"); }
        private void ViewModel_RequestNavigate(TabViewModel tab, string url) { try { if (_webViewPool.TryGetValue(tab.Id, out var webView)) { webView.CoreWebView2.Navigate(url); tab.Url = url; } } catch { } }
        private void AddressBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ViewModel.ExecuteNavigate(); e.Handled = true; } }

        // FEATURE: True Chart Mode Mouse Tracking
        private void MainWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (ViewModel.IsChartMode)
            {
                double y = e.GetPosition(this).Y;
                if (y < 5) // Near top edge
                {
                    TabStripBorder.Visibility = Visibility.Visible;
                    AddressBarBorder.Visibility = Visibility.Visible;
                }
                else if (y > 80) // Moved away from top
                {
                    TabStripBorder.Visibility = Visibility.Collapsed;
                    AddressBarBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                TabStripBorder.Visibility = Visibility.Visible;
                AddressBarBorder.Visibility = Visibility.Visible;
            }
        }

        private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab && !tab.IsPinned) { _startPoint = e.GetPosition(null); _draggedTab = tab; } } catch { } }
        private void Tab_MouseMove(object sender, MouseEventArgs e) { try { if (_draggedTab == null) return; var pos = e.GetPosition(null); if (Math.Abs(pos.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance) { DragDrop.DoDragDrop((DependencyObject)sender, _draggedTab, DragDropEffects.Move); _draggedTab = null; } } catch { _draggedTab = null; } }
        private void Tab_DragOver(object sender, DragEventArgs e) { if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel) e.Effects = DragDropEffects.Move; else e.Effects = DragDropEffects.None; e.Handled = true; }
        private void Tab_Drop(object sender, DragEventArgs e) { try { if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel droppedTab && sender is FrameworkElement target && target.DataContext is TabViewModel targetTab && droppedTab.Id != targetTab.Id) { int oldIndex = ViewModel.Tabs.IndexOf(droppedTab); int newIndex = ViewModel.Tabs.IndexOf(targetTab); ViewModel.Tabs.Move(oldIndex, newIndex); } } catch { } }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T) { ViewModel.AddTab(); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) { ViewModel.CloseTabCommand.Execute(ViewModel.SelectedTab); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { ViewModel.FocusAddressBar(); e.Handled = true; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.T) { ViewModel.UndoCloseTabCommand.Execute(null); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R) { Reload_Click(sender, e); e.Handled = true; }
            if (e.Key == Key.F11) { ViewModel.IsChartMode = !ViewModel.IsChartMode; e.Handled = true; }
        }

        private void TabStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.Source is Border) DragMove(); }
        private void Tab_Click(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab) ViewModel.SelectedTab = tab; }
        private void Back_Click(object sender, RoutedEventArgs e) { if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) wv.CoreWebView2.GoBack(); }
        private void Forward_Click(object sender, RoutedEventArgs e) { if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) wv.CoreWebView2.GoForward(); }
        private void Reload_Click(object sender, RoutedEventArgs e) { if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) wv.CoreWebView2.Reload(); }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
        private const int WM_SYSCOMMAND = 0x0112, SC_MAXIMIZE = 0xF030, SC_MINIMIZE = 0xF020;
        private void Minimize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MINIMIZE, 0);
        private void Maximize_Click(object sender, RoutedEventArgs e) => SendMessage(new System.Windows.Interop.WindowInteropHelper(this).Handle, WM_SYSCOMMAND, SC_MAXIMIZE, 0);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
