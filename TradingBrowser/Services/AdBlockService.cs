using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;

namespace TradingBrowser.Services
{
    public class AdBlockService
    {
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
                    // Safely cast and check for null (fixes warnings)
                    if (sender is CoreWebView2 coreWebView)
                    {
                        e.Response = coreWebView.Environment.CreateWebResourceResponse(
                            null,       
                            204,        
                            "No Content", 
                            ""          
                        );
                        BlockedCount++;
                    }
                    return;
                }
            }
        }
    }
}
