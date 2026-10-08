namespace FocusDesk.Helpers;

/// <summary>
/// Where the bundled setup guide for an automatic focus session is served from. The page is a plain
/// file beside the executable, served to the embedded browser under a host name in the reserved
/// <c>.invalid</c> namespace, so it can never resolve to anything on the network.
/// </summary>
internal static class SetupGuidePage
{
    internal const string Host = "guide.focusdesk.invalid";

    /// <summary>The page, relative to the executable's folder. The folder itself is what the host
    /// maps, because the page loads the brand font from the copy the brand package ships there.</summary>
    internal const string RelativePath = "Assets/Guide/automatic-focus-session.html";

    /// <summary>The page's address, opening in <paramref name="language"/>: <c>en</c> or <c>nb</c>.</summary>
    internal static Uri Address(string language) => new($"https://{Host}/{RelativePath}#{language}");

    /// <summary>Whether an address is the guide itself, in either language. Nothing else is shown.</summary>
    internal static bool IsGuide(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath, "/" + RelativePath, StringComparison.OrdinalIgnoreCase);
}
