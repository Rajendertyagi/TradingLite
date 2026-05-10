using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace TradingBrowser.Services
{
    public class AdBlockService
    {
        // Thread-safe dictionary for real-time updating while blocking
        private static readonly ConcurrentDictionary<string, bool> BlockedDomains = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public int BlockedCount { get; private set; }

        public AdBlockService()
        {
            // Start downloading EasyList in the background without freezing the UI
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
                    var trimmed = line.AsSpan().Trim();

                    // Skip comments, empty lines, and cosmetic-only filters (##)
                    if (trimmed.IsEmpty || trimmed.StartsWith("!") || trimmed.StartsWith("[")) continue;
                    if (trimmed.IndexOf("##") >= 0 && trimmed.IndexOf("||") < 0) continue;

                    // We only care about network blocking rules starting with ||
                    if (trimmed.StartsWith("||"))
                    {
                        // Strip || and split by options $ or end of line ^
                        int endIdx = trimmed.IndexOf('^');
                        if (endIdx < 0) endIdx = trimmed.Length;

                        var domainSpan = trimmed.Slice(2, endIdx - 2);

                        // Strip paths (e.g., domain.com/path -> domain.com)
                        int slashIdx = domainSpan.IndexOf('/');
                        if (slashIdx >= 0) domainSpan = domainSpan.Slice(0, slashIdx);

                        string domain = domainSpan.ToString();
                        if (!string.IsNullOrWhiteSpace(domain) && domain.Contains('.')) // Ensure it's a domain
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

            try
            {
                var uri = new Uri(e.Request.Uri);
                string host = uri.Host;

                // Check if the host or any parent domain is blocked
                // e.g., for ads.doubleclick.net, it checks "ads.doubleclick.net", then "doubleclick.net"
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
            catch { /* Ignore malformed URIs */ }
        }
    }
}
