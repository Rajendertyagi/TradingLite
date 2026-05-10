using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TradingBrowser.Services
{
    public class AdBlockService
    {
        private static readonly ConcurrentDictionary<string, bool> BlockedDomains = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> WhitelistedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "google.com", "google.co.in", "youtube.com", "googleapis.com", 
            "googlevideo.com", "ytimg.com", "gstatic.com", "googleusercontent.com", "gmail.com"
        };
        
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _whitelistPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TradingBrowser_Fresh", "whitelist.json");
        
        public int TotalBlocked { get; private set; }
        public int SessionBlocked { get; private set; }

        public AdBlockService()
        {
            LoadWhitelist();
            Task.Run(() => DownloadAndParseEasyListAsync());
        }

        private void LoadWhitelist()
        {
            try
            {
                if (File.Exists(_whitelistPath))
                {
                    var domains = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_whitelistPath));
                    if (domains != null) foreach (var d in domains) WhitelistedDomains.Add(d);
                }
            }
            catch { }
        }

        public void SaveWhitelist()
        {
            try { File.WriteAllText(_whitelistPath, JsonSerializer.Serialize(WhitelistedDomains.ToList())); } catch { }
        }

        public bool IsDomainWhitelisted(string host)
        {
            string temp = host;
            while (temp.Length > 0)
            {
                if (WhitelistedDomains.Contains(temp)) return true;
                int dot = temp.IndexOf('.');
                if (dot < 0) break;
                temp = temp.Substring(dot + 1);
            }
            return false;
        }

        public void ToggleWhitelist(string url)
        {
            try
            {
                var uri = new Uri(url);
                if (IsDomainWhitelisted(uri.Host))
                    WhitelistedDomains.Remove(uri.Host);
                else
                    WhitelistedDomains.Add(uri.Host);
                SaveWhitelist();
            }
            catch { }
        }

        private async Task DownloadAndParseEasyListAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://easylist.to/easylist/easylist.txt");
                var lines = response.Split('\n');
                int count = 0;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("!") || trimmed.StartsWith("[")) continue;
                    if (trimmed.Contains("##") && !trimmed.Contains("||")) continue;
                    if (trimmed.StartsWith("||"))
                    {
                        int endIdx = trimmed.IndexOf('^'); if (endIdx < 0) endIdx = trimmed.Length;
                        var domain = trimmed.Substring(2, endIdx - 2);
                        int slashIdx = domain.IndexOf('/'); if (slashIdx >= 0) domain = domain.Substring(0, slashIdx);
                        if (!string.IsNullOrWhiteSpace(domain) && domain.Contains('.')) { BlockedDomains.TryAdd(domain, true); count++; }
                    }
                }
                TotalBlocked = count;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"EasyList failed: {ex.Message}"); }
        }

        public void AttachToWebView(CoreWebView2 webView)
        {
            webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            webView.WebResourceRequested += WebView_WebResourceRequested;
        }

        private void WebView_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (BlockedDomains.IsEmpty || e.ResourceContext == CoreWebView2WebResourceContext.Document) return;
            try
            {
                var uri = new Uri(e.Request.Uri);
                string host = uri.Host;
                while (host.Length > 0)
                {
                    if (BlockedDomains.ContainsKey(host))
                    {
                        if (sender is CoreWebView2 coreWebView)
                        {
                            e.Response = coreWebView.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
                            SessionBlocked++;
                        }
                        return;
                    }
                    int dotIdx = host.IndexOf('.'); if (dotIdx < 0) break;
                    host = host.Substring(dotIdx + 1);
                }
            }
            catch { }
        }
    }
}
