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
            webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            webView.WebResourceRequested += WebView_WebResourceRequested;
        }

        private void WebView_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var uri = new Uri(e.Request.Uri);
            string host = uri.Host;

            foreach (var blocked in BlockedDomains)
            {
                if (host.Contains(blocked))
                {
                    // To block in C# WebView2, we create a fake empty 204 response
                    var coreWebView = (CoreWebView2)sender;
                    e.Response = coreWebView.Environment.CreateWebResourceResponse(
                        null,       // No content body
                        204,        // HTTP 204 No Content
                        "No Content", 
                        ""          // No headers
                    );
                    
                    BlockedCount++;
                    return;
                }
            }
        }
    }
}
