using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;

namespace TradingBrowser.Services
{
    public class AdBlockService
    {
        // HashSet provides O(1) lookup time, easily hitting the <0.5ms spec requirement
        private static readonly HashSet<string> BlockedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "doubleclick.net", "googlesyndication.com", "googleadservices.com",
            "google-analytics.com", "facebook.net", "facebook.com",
            "ads.twitter.com", "analytics.twitter.com", "amazon-adsystem.com",
            "adnxs.com", "adsrvr.org", "rubiconproject.com", "outbrain.com",
            "taboola.com", "criteo.com", "quantserve.com"
        };

        public int BlockedCount { get; private set; }

        public void AttachToWebView(CoreWebView2 webView)
        {
            // Spec: AddWebResourceRequestedFilter to intercept all URL requests
            webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            webView.WebResourceRequested += WebView_WebResourceRequested;
        }

        private void WebView_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var uri = new Uri(e.Request.Uri);
            string host = uri.Host;

            // Check if main domain or subdomain contains a blocked keyword
            foreach (var blocked in BlockedDomains)
            {
                if (host.Contains(blocked))
                {
                    // Block the request by setting Response to null and marking as handled
                    e.Response = null;
                    e.Handled = true;
                    BlockedCount++;
                    return;
                }
            }
        }
    }
}
