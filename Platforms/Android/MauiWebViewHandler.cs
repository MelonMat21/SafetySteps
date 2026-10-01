// Place this file at:  Platforms/Android/MauiWebViewHandler.cs
//
// Registers a custom handler that gives the Android WebView the
// permissions it needs to load a local HTML file (file:// URL) that
// then fetches external resources such as the Leaflet CDN and
// OpenStreetMap tiles.
//
// Wire it up in MauiProgram.cs:
//
//   #if ANDROID
//   builder.ConfigureMauiHandlers(h =>
//       h.AddHandler<Microsoft.Maui.Controls.WebView,
//                    SAFETY_STEPS.Platforms.Android.MauiWebViewHandler>());
//   #endif

#if ANDROID
// FIX: use global:: so the compiler resolves Android.Webkit against the
// Android SDK root rather than against the current namespace
// (SAFETY_STEPS.Platforms.Android), which has no "Webkit" child.
using global::Android.Webkit;
using Microsoft.Maui.Handlers;

namespace SAFETY_STEPS.Platforms.Android;

public class MauiWebViewHandler : WebViewHandler
{
    protected override global::Android.Webkit.WebView CreatePlatformView()
    {
        var webView = base.CreatePlatformView();

        webView.Settings.JavaScriptEnabled = true;
        webView.Settings.DomStorageEnabled = true;

        // Allow the file:// page to load https:// CDN resources
        webView.Settings.AllowFileAccessFromFileURLs = true;
        webView.Settings.AllowUniversalAccessFromFileURLs = true;

        // Allow mixed content (file:// loading https://)
        webView.Settings.MixedContentMode =
            MixedContentHandling.AlwaysAllow;

        // Better rendering
        webView.Settings.UseWideViewPort = true;
        webView.Settings.LoadWithOverviewMode = true;
        webView.Settings.SetSupportZoom(true);
        webView.Settings.BuiltInZoomControls = true;
        webView.Settings.DisplayZoomControls = false;

        return webView;
    }
}
#endif