using FocusDesk.Services;
using Microsoft.Web.WebView2.Core;

namespace FocusDesk.Helpers;

/// <summary>The one embedded-browser environment the process shares, whatever page it shows. Two
/// environments over one store are only allowed where every option matches, which is a constraint
/// worth not having.</summary>
internal static class EmbeddedBrowser
{
    /// <summary>The browser's own store, beside the settings rather than beside the executable: the
    /// program folder is not writable for the user the session runs as.</summary>
    private const string DataFolder = "WebView2";

    private static Task<CoreWebView2Environment>? _environment;

    /// <summary>The shared environment, created on first use. From the UI thread.</summary>
    internal static Task<CoreWebView2Environment> Environment =>
        _environment ??= CoreWebView2Environment.CreateWithOptionsAsync(
            "", AppPaths.DataFile(DataFolder), new CoreWebView2EnvironmentOptions()).AsTask();
}
