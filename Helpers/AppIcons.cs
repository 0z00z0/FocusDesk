using ZeroZero.Tray;

namespace FocusDesk.Helpers;

/// <summary>
/// The product mark's icon files, which sit beside the executable. The mark is an open amber ring
/// with a purple dot held clear of it; <c>scripts\build-icons.ps1</c> draws all three files and they
/// are committed, so no build draws anything.
/// </summary>
/// <remarks>Files rather than a bitmap built in memory: the notification area reloads its icon from
/// disk after the shell is restarted, and a handle built per render leaks where a file does not.
/// The same file is what a window's title bar is given.</remarks>
internal static class AppIcons
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>The application mark, at 16, 32, 48 and 256 pixels. The executable carries the same
    /// picture as its own icon; this copy is what a window's title bar is set from.</summary>
    internal static string Application { get; } = Path.Combine(Folder, "FocusDesk.ico");

    /// <summary>The notification-area icon for the taskbar <paramref name="theme"/> names.</summary>
    /// <remarks>Two files rather than one: a thin amber ring reads at about 2.2:1 against a white
    /// taskbar, which disappears at tray size, so the light-taskbar file carries the same two hues
    /// deepened.</remarks>
    internal static string Tray(TaskbarTheme theme) => Path.Combine(
        Folder, theme == TaskbarTheme.Light ? "FocusDeskTrayLight.ico" : "FocusDeskTray.ico");
}
