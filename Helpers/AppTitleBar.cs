using FocusDesk.Services;
using Microsoft.UI.Xaml;
using ZeroZero.Controls.WinUI;

namespace FocusDesk.Helpers;

/// <summary>
/// The application mark and the caption colours on a window that shows the system title bar. The
/// painting is the shared title-bar theming; the icon and the palette are this application's.
/// </summary>
internal static class AppTitleBar
{
    /// <summary>The platform's dark caption, with the glyphs of an inactive window kept at full
    /// strength rather than greyed.</summary>
    internal static TitleBarPalette Palette { get; } = TitleBarPalette.Dark with
    {
        InactiveForeground       = TitleBarPalette.Dark.Foreground,
        ButtonInactiveForeground = TitleBarPalette.Dark.ButtonForeground,
    };

    /// <summary>Sets the icon and follows the theme with <see cref="Palette"/>. Never throws: a
    /// title-bar customisation failure must not stop a window from showing.</summary>
    /// <remarks>A window that already follows the theme through the shared theming is repainted
    /// with this palette as well, because handlers added here run after its own.</remarks>
    internal static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Without an icon of its own a WinUI window shows a generic one: the executable's is not used.
        try { window.AppWindow.SetIcon(AppIcons.Application); }
        catch (Exception ex) { AppLog.Error("AppTitleBar.SetIcon", ex); }

        try { TitleBarTheming.Follow(window, Palette); }
        catch (Exception ex) { AppLog.Error("AppTitleBar.Follow", ex); }
    }
}
