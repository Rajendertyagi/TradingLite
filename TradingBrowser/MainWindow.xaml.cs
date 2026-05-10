using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.WebView2.Core;
using TradingBrowser.Services;
using TradingBrowser.ViewModels;
using TradingBrowser.Views;
using Microsoft.Win32;

namespace TradingBrowser
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;
        private readonly Dictionary<Guid, Microsoft.WebView2.Wpf.WebView2> _webViewPool = new();
        private readonly AdBlockService _adBlocker = new AdBlockService();
        private CoreWebView2Environment? _environment;
        private bool _isInitialized = false;
        
        private readonly string _sessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser_Fresh", "session.json");
        private string _homePath = ""; 
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
            _homePath = Path.Combine(folder, "homepage.json");
            
            _environment = await CoreWebView2Environment.CreateAsync(null, folder, new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-renderer-backgrounding" });

            ViewModel.RequestNavigate += ViewModel_RequestNavigate;
            ViewModel.RequestFocusAddressBar += () => { AddressBox.Focus(); AddressBox.SelectAll(); };
            ViewModel.TabClosed += ViewModel_TabClosed;
            ViewModel.TabPopOut += ViewModel_TabPopOut;
            ViewModel.TabScreenshot += ViewModel_TabScreenshot;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.ClearCacheRequested += () => ClearCache();

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
                MessageBox.Show("Cache cleared! Please restart.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information); 
            }
            catch { }
        }

        private void ViewModel_TabPopOut(TabViewModel tab) { if (_webViewPool.TryGetValue(tab.Id, out var wv) && _environment != null) { var popOut = new FloatingChartWindow(_environment, wv.Source.ToString()); popOut.Show(); } }

        private async void ViewModel_TabScreenshot(TabViewModel tab)
        {
            if (_webViewPool.TryGetValue(tab.Id, out var wv))
            {
                try { string jsonResult = await wv.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\"}"); using JsonDocument doc = JsonDocument.Parse(jsonResult); if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String) { string? base64 = dataElement.GetString(); if (!string.IsNullOrEmpty(base64)) { byte[] imageBytes = Convert.FromBase64String(base64); SaveFileDialog saveDialog = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = $"{tab.Title}_screenshot.png" }; if (saveDialog.ShowDialog() == true) File.WriteAllBytes(saveDialog.FileName, imageBytes); } } } catch { }
            }
        }

        private void ViewModel_TabClosed(TabViewModel tab) { if (_webViewPool.TryGetValue(tab.Id, out var webView)) { try { ActiveWebViewHost.Children.Remove(webView); webView.Dispose(); } catch { } _webViewPool.Remove(tab.Id); } }

        protected override void OnClosed(EventArgs e) { } 

        private void SaveSession() { } 

        private void RestoreSession()
        {
            try 
            { 
                if (File.Exists(_sessionPath)) 
                    File.Delete(_sessionPath); 
            } 
            catch { }
            
            ViewModel.AddTab("homemarket://");
        }

        private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ViewModel.SelectedTab) && ViewModel.SelectedTab != null && _isInitialized) { await ActivateTab(ViewModel.SelectedTab); ViewModel.IsSiteWhitelisted = _adBlocker.IsDomainWhitelisted(ViewModel.SelectedTab.Url); } }

        private async System.Threading.Tasks.Task ActivateTab(TabViewModel tab)
        {
            if (!_webViewPool.ContainsKey(tab.Id))
            {
                var webView = new Microsoft.WebView2.Wpf.WebView2();
                ActiveWebViewHost.Children.Add(webView);
                await webView.EnsureCoreWebView2Async(_environment);

                _adBlocker.AttachToWebView(webView.CoreWebView2);

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    (function() {
                        let link = document.createElement('link');
                        link.rel = 'stylesheet';
                        link.href = 'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap';
                        document.head.appendChild(link);
                        let style = document.createElement('style');
                        style.type = 'text/css';
                        style.innerHTML = 'body, input, button, textarea, select, div, span, p, h1, h2, h3, h4, h5, h6, a { font-family: Inter, sans-serif !important; }';
                        document.head.appendChild(style);
                    })();
                ");

                webView.CoreWebView2.DocumentTitleChanged += (_, _) => { Application.Current.Dispatcher.Invoke(() => { try { tab.Title = webView.CoreWebView2.DocumentTitle; } catch { } }); };
                webView.CoreWebView2.FaviconChanged += async (_, _) => { try { var stream = await webView.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png); var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = stream; bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze(); Application.Current.Dispatcher.Invoke(() => { try { tab.Favicon = bitmap; } catch { } }); } catch { } };
                
                webView.CoreWebView2.SourceChanged += (_, _) => 
                { 
                    Application.Current.Dispatcher.Invoke(() => 
                    { 
                        try 
                        { 
                            var currentUrl = webView.Source?.ToString();
                            if (!string.IsNullOrEmpty(currentUrl))
                            {
                                tab.Url = currentUrl; 
                                if (ViewModel.SelectedTab?.Id == tab.Id && !AddressBox.IsKeyboardFocused)
                                    ViewModel.AddressBarText = currentUrl;
                            }
                        } 
                        catch { } 
                    }); 
                };
                
                webView.CoreWebView2.WebMessageReceived += (s, args) => {
                    var url = args.TryGetWebMessageAsString();
                    if (!string.IsNullOrEmpty(url))
                    {
                        if (url == "clearcache") ClearCache();
                        else if (url == "edit_homepage") EditHomepage(); 
                        else ViewModel_RequestNavigate(tab, url);
                    }
                };
                
                webView.CoreWebView2.NewWindowRequested += (s, args) => { try { args.Handled = true; ViewModel.AddTab(args.Uri); } catch { } };

                webView.CoreWebView2.DownloadStarting += (s, args) =>
                {
                    try
                    {
                        var download = args.DownloadOperation;
                        
                        if (download != null && !string.IsNullOrEmpty(download.ResultFilePath))
                        {
                            string filePath = download.ResultFilePath;
                            var item = new DownloadItem { FileName = Path.GetFileName(filePath) ?? "Downloading...", FilePath = filePath };
                            
                            download.BytesReceivedChanged += (_, _) => 
                            { 
                                try { Application.Current?.Dispatcher?.Invoke(() => { item.BytesReceived = (long)download.BytesReceived; item.TotalBytes = (long)(download.TotalBytesToReceive ?? 0); }); } 
                                catch { } 
                            };
                            
                            download.StateChanged += (_, _) => 
                            { 
                                try { if (download.State == CoreWebView2DownloadState.Completed || download.State == CoreWebView2DownloadState.Interrupted) Application.Current?.Dispatcher?.Invoke(() => item.IsComplete = true); } 
                                catch { } 
                            };
                            
                            try { Application.Current?.Dispatcher?.Invoke(() => ViewModel.Downloads.Add(item)); } catch { }
                        }
                        else
                        {
                            // If ResultFilePath is null (Blob/Script downloads), completely ignore tracking. Windows native Save dialog handles it.
                        }
                    }
                    catch
                    {
                        // If tracking the download crashes, ignore it so the browser doesn't die.
                    }
                };

                if (tab.Url.Contains("tradingview.com"))
                {
                    await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"document.addEventListener('keydown', function(e) { if (e.ctrlKey && e.altKey) { let tf = null; if(e.key==='1') tf='1'; if(e.key==='2') tf='5'; if(e.key==='3') tf='15'; if(e.key==='4') tf='30'; if(e.key==='5') tf='60'; if(e.key==='6') tf='120'; if(e.key==='7') tf='D'; if(tf) { e.preventDefault(); e.stopPropagation(); let btns = document.querySelectorAll('button[name=""timeInterval""]'); btns.forEach(b => { if(b.getAttribute('data-value') === tf) b.click(); }); } } });");
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

        private void EditHomepage()
        {
            try
            {
                if (!File.Exists(_homePath)) { var def = GetDefaultLinks(); File.WriteAllText(_homePath, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true })); }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", _homePath) { UseShellExecute = true };
            }
            catch (Exception ex) { MessageBox.Show($"Failed to open editor: {ex.Message}"); }
        }

        private class HomePageLink { public string Title { get; set; } = ""; public string Url { get; set; } = ""; public string Color { get; set; } = "#0078d9d4"; }
        private List<HomePageLink> GetDefaultLinks() => new List<HomePageLink> { new() { Title = "Zerodha", Url = "https://kite.zerodha.com", Color = "#387ed1" }, new() { Title = "Upstox", Url = "https://pro.upstox.com", Color = "#5e72e4" }, new() { Title = "Groww", Url = "https://app.groww.in", Color = "#00d09c" }, new() { Title = "Angel One", Url = "https://trade.angelone.in", Color = "#e8375d" }, new() { Title = "ICICI Direct", Url = "https://www.icicidirect.com", Color = "#f37021" } };

        private void LoadHomePage(Microsoft.WebView2.Wpf.WebView2 webView)
        {
            List<HomePageLink> links = new List<HomePageLink>();
            if (File.Exists(_homePath))
            {
                try { links = JsonSerializer.Deserialize<List<HomePageLink>>(File.ReadAllText(_homePath)) ?? new List<HomePageLink>(); }
                catch { links = GetDefaultLinks(); }
            }
            else { links = GetDefaultLinks(); File.WriteAllText(_homePath, JsonSerializer.Serialize(links, new JsonSerializerOptions { WriteIndented = true })); }

            string linksJson = JsonSerializer.Serialize(links);
            string html = @"<html><head><style>body{background-color:#1e1e1e;color:white;font-family:'Inter',sans-serif;display:flex;flex-direction:column;align-items:center;margin-top:10%}.brokers{display:flex;gap:20px;margin-top:30px;flex-wrap:wrap;justify-content:center}.broker-card{background:#2d2d2d;padding:20px;border-radius:10px;text-align:center;cursor:pointer;transition:0.2s;text-decoration:none;color:white;width:120px}.broker-card:hover{background:#3d3d3d;transform:scale(1.05)}.edit-btn{position:absolute;top:20px;right:20px;background:#35363a;border:1px solid #444;color:white;padding:8px 12px;border-radius:6px;cursor:pointer;font-size:13px}.edit-btn:hover{background:#4a4b4f}input[type='text']{margin-top:40px;padding:12px 20px;width:500px;border-radius:25px;border:1px solid #444;background:#333;color:white;font-size:16px;outline:none}input[type='text']:focus{border-color:#0078d4}</style></head><body><button class='edit-btn' onclick=""window.chrome.webview.postMessage('edit_homepage')"">✏️ Edit Homepage</button><h2>My Quick Access</h2><div class='brokers' id='brokerList'></div><input type='text' id='searchbox' placeholder='Search Google or type a URL... (e.g. tv RELIANCE)' autofocus><script>const links=" + linksJson + @";const list=document.getElementById('brokerList');links.forEach(link=>{const a=document.createElement('a');a.className='broker-card';a.href=link.url;a.innerHTML=`<h3 style='color:${link.color}'>${link.title}</h3>`;list.appendChild(a)});document.getElementById('searchbox').addEventListener('keydown',function(e){if(e.key==='Enter'){let val=this.value;if(!val.includes('.')||val.includes(' '))val='https://www.google.com/search?q='+encodeURIComponent(val);else if(!val.startsWith('http'))val='https://'+val;window.chrome.webview.postMessage(val)}});document.querySelectorAll('.broker-card').forEach(a=>{a.addEventListener('click',function(e){e.preventDefault();window.chrome.webview.postMessage(this.href)})});</script></body></html>";
            webView.CoreWebView2.NavigateToString(html);
        }

        private void LoadSettingsPage(Microsoft.WebView2.Wpf.WebView2 webView) { webView.CoreWebView2.NavigateToString(@"<html><head><style>body{background-color:#202124;color:#e8eaed;font-family:'Inter',sans-serif;padding:40px}h2{margin-top:0}button{padding:12px 20px;background:#0078d4;color:white;border:none;border-radius:4px;cursor:pointer;font-size:14px;margin-top:20px}button:hover{background:#1a86d9}.shortcut{background:#35363a;padding:10px;margin-top:10px;border-radius:4px}</style></head><body><h2>Settings</h2><p><strong>Ad-Blocker:</strong> Active. Use shield icon in address bar.</p><p><strong>Quick Menu (TradingView):</strong></p><div class='shortcut'>Ctrl + Alt + 1 &rarr; 1 Min</div><div class='shortcut'>Ctrl + Alt + 5 &rarr; 1 Hour</div><button onclick=""window.chrome.webview.postMessage('clearcache')"">Clear All Cache</button></body></html>"); }
        private void MuteTab(Microsoft.WebView2.Wpf.WebView2 webView, bool mute) { try { if(webView.CoreWebView2!=null) webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Audio.setMuteTab", mute ? "{\"muted\":true}" : "{\"muted\":false}").ConfigureAwait(false); } catch { } }
        private void ViewModel_RequestNavigate(TabViewModel tab, string url) { try { if (_webViewPool.TryGetValue(tab.Id, out var webView)) { webView.CoreWebView2.Navigate(url); tab.Url = url; } } catch { } }
        private void AddressBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ViewModel.ExecuteNavigate(); e.Handled = true; } }

        private void MainWindow_MouseMove(object sender, MouseEventArgs e) { if (ViewModel.IsChartMode) { double y = e.GetPosition(this).Y; if (y < 5) { TabStripBorder.Visibility = Visibility.Visible; AddressBarBorder.Visibility = Visibility.Visible; } else if (y > 80) { TabStripBorder.Visibility = Visibility.Collapsed; AddressBarBorder.Visibility = Visibility.Collapsed; } } else { TabStripBorder.Visibility = Visibility.Visible; AddressBarBorder.Visibility = Visibility.Visible; } }

        private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { try { if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab && !tab.IsPinned) { _startPoint = e.GetPosition(null); _draggedTab = tab; } } catch { } }
        private void Tab_MouseMove(object sender, MouseEventArgs e) { try { if (_draggedTab == null) return; var pos = e.GetPosition(null); if (Math.Abs(pos.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance) { DragDrop.DoDragDrop((DependencyObject)sender, _draggedTab, DragDropEffects.Move); _draggedTab = null; } catch { _draggedTab = null; } }
        private void Tab_DragOver(object sender, DragEventArgs e) { if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel) e.Effects = DragDropEffects.Move; else e.Effects = DragDropEffects.None; e.Handled = true; }
        private void Tab_Drop(object sender, DragEventArgs e) { try { if (e.Data.GetData(typeof(TabViewModel)) is TabViewModel droppedTab && sender is FrameworkElement target && target.DataContext is TabViewModel targetTab && droppedTab.Id != targetTab.Id) { int oldIndex = ViewModel.Tabs.IndexOf(droppedTab); int newIndex = ViewModel.Tabs.IndexOf(targetTab); ViewModel.Tabs.Move(oldIndex, newIndex); } } catch { } }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T) { ViewModel.AddTab(); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) { ViewModel.CloseTabCommand.Execute(ViewModel.SelectedTab); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) { ViewModel.FocusAddressBar(); e.Handled = true; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.T) { ViewModel.UndoCloseTabCommand.Execute(null); e.Handled = true; }
            if (Key.R == Key.F11) { ViewModel.IsChartMode = !ViewModel.IsChartMode; e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R) { Reload_Click(sender, e); e.Handled = true; }
        }

        private void TabStrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.Source is Border) DragMove(); }
        private void Tab_Click(object sender, MouseButtonEventArgs e) { if (sender is FrameworkElement fe && fe.DataContext is TabViewModel tab) ViewModel.SelectedTab = tab; }
        private void Back_Click(object sender, RoutedEventArgs e) { if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) wv.CoreWebView2.GoBack(); }
        private void Forward_Click(object sender, RoutedEventArgs e) { if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) wv.CoreWebView2.GoForward(); }
        private void Reload_Click(object sender, RoutedEventArgs e) { if (_webViewPool.TryGetValue(ViewModel.SelectedTab?.Id ?? Guid.Empty, out var wv)) wv.CoreWebView2.Reload(); }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);
        private const int WM_SYSCOMMAND = 0x0112, SC_MAXIMIZE = 0xF030, SC_MINIMIZE = 0xF020;
        private void Minimize_Click(object sender, RoutedEventArgs e) => SendMessage(System.Windows.Interop.WindowInteropHelper.Handle, WM_SYSCOMMAND, SC_MINIMIZE, 0);
        private void Maximize_Click(object sender, RoutedEventArgs e) => SendMessage(System.Windows.Interop.WindowInteropHelper.Handle, WM_SYSCOMMAND, SC_MAXIMIZE, 0);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
