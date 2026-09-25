using FocusDesk.Services;
using ZeroZero.Brand.Core;

namespace FocusDesk.Helpers;

/// <summary>
/// The About payload, shared by the notification-area menu's About window and the About section in
/// the Settings window, so neither can drift on wording, version or credits.
/// </summary>
internal static class AboutContent
{
    /// <summary>Width in device-independent units at which the shared About control lays out
    /// correctly: narrow enough that prose wraps at a readable measure, wide enough that a credit
    /// row does not wrap mid-row. A property of the control, so both hosts use this one number.</summary>
    internal const int ContentWidthDip = 460;

    private const string RepoUrl = "https://github.com/0z00z0/FocusDesk";

    internal const string WhatsNewLabel = "What's new";

    /// <summary>The GitHub release page of the running version. Releases are tagged <c>v</c> and the
    /// version.</summary>
    internal static Uri ReleaseNotesPage { get; } = new($"{RepoUrl}/releases/tag/v{AppInfo.Version}");

    /// <summary>The About payload. Pure data — no input and no output, so it cannot fail.</summary>
    /// <remarks>"What's new" is a button of this application's that opens the release page. The
    /// control's own button stays hidden: it wants plain-text notes, which GitHub does not serve.</remarks>
    internal static AboutInfo Build() => new()
    {
        AppName     = AppInfo.Name,
        Version     = AppInfo.Version,
        Description = "Holds the machine in a chosen state for a stretch of time. A session runs for the length it was given; nothing on this computer ends one early.",
        RepoUrl     = RepoUrl,
        Buttons     = [new AboutButton(WhatsNewLabel, () => AppUpdates.OpenInBrowser(ReleaseNotesPage))],
        ExternalLibraries =
        [
            new ExternalLibrary("H.NotifyIcon.WinUI", "HavenDV", "The notification-area icon and its native menu", "MIT", "https://github.com/HavenDV/H.NotifyIcon"),
            new ExternalLibrary("TaskScheduler", "David Hall", "Reads and writes the logon and watchdog scheduled tasks", "MIT", "https://github.com/dahall/TaskScheduler"),
            new ExternalLibrary("CommunityToolkit.WinUI.Controls.SettingsControls", ".NET Foundation", "The settings rows the pages are built from", "MIT", "https://github.com/CommunityToolkit/Windows"),
            new ExternalLibrary("MQTTnet", "The MQTTnet Project", "The client behind the Home Assistant integration", "MIT", "https://github.com/dotnet/MQTTnet"),
            new ExternalLibrary("NLog", "Jarek Kowalski, Kim Christensen, Julian Verdurmen", "The event log, rolled daily with a size cap", "BSD-3-Clause", "https://github.com/NLog/NLog"),
            new ExternalLibrary("System.Management", "Microsoft", "Reads and writes the built-in screen's brightness", "MIT", "https://github.com/dotnet/runtime"),
        ],
    };
}
