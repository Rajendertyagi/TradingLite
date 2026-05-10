using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace TradingBrowser.Services
{
    public class AdBlockService
    {
        private static readonly ConcurrentDictionary<string, bool> BlockedDomains = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        
        // CRITICAL FIX: Whitelist core Google domains to prevent YouTube/Gmail from breaking
        private static readonly HashSet<string> WhitelistedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "google.com", "google.co.in", "youtube.com", "googleapis.com", 
            "googlevideo.com", "ytimg.com", "gstatic.com", "googleusercontent.com",
            "gmail.com"
        };

        private static readonly HttpClient _httpClient = new HttpClient();
        
        public int BlockedCount { get; private set; }

        public AdBlockService()
        {
            Task.Run(() => DownloadAndParseEasyListAsync());
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
                        int endIdx = trimmed.IndexOf('^');
                        if (endIdx < 0) endIdx = trimmed.Length;

                        var domain = trimmed.Substring(2, endIdx - 2);

                        int slashIdx = domain.IndexOf('/');
                        if (slashIdx >= 0) domain = domain.Substring(0, slashIdx);

                        if (!string.IsNullOrWhiteSpace(domain) && domain.Contains('.'))
                        {
                            BlockedDomains.TryAdd(domain, true);
                            count++;
                        }
                    }
                }
                System.Diagnostics.Debug.WriteLine($"AdBlocker loaded {count} rules from EasyList");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to download EasyList: {ex.Message}");
            }
        }

        public void AttachToWebView(CoreWebView2 webView)
        {
            webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            webView.WebResourceRequested += WebView_WebResourceRequested;
        }

        private void WebView_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (BlockedDomains.IsEmpty) return;

            // Never block the main HTML document
            if (e.ResourceContext == CoreWebView2WebResourceContext.Document) return;

            try
            {
                var uri = new Uri(e.Request.Uri);
                string host = uri.Host;

                // SAFE CHECK: If the URL belongs to Google/YouTube/Gmail, immediately allow it
                string tempHost = host;
                while (tempHost.Length > 0)
                {
                    if (WhitelistedDomains.Contains(tempHost)) return;
                    
                    int dotIdx = tempHost.IndexOf('.');
                    if (dotIdx < 0) break;
                    tempHost = tempHost.Substring(dotIdx + 1);
                }

                // If not whitelisted, check against EasyList
                while (host.Length > 0)
                {
                    if (BlockedDomains.ContainsKey(host))
                    {
                        if (sender is CoreWebView2 coreWebView)
                        {
                            e.Response = coreWebView.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
                            BlockedCount++;
                        }
                        return;
                    }

                    int dotIdx = host.IndexOf('.');
                    if (dotIdx < 0) break;
                    host = host.Substring(dotIdx + 1);
                }
            }
            catch { }
        }
    }
}
