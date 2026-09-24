using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ZeroZero.Tray.WinUI;

namespace FocusDesk.UI;

/// <summary>
/// The notification-area icon: the one thing on screen the whole time FocusDesk runs, and the way to
/// everything else. A click opens the status window; the menu reaches the status window, the
/// Settings window and the way out of the application.
/// </summary>
/// <remarks>
/// <para>The shared host owns the icon's lifecycle, the taskbar theme and display listeners, the
/// shell-restart repair, the tooltip's length and the menu's rebuild. What is FocusDesk's is which
/// file is shown, what the tooltip says, what the menu holds and what a click does.</para>
/// <para>Leaving from the menu is not a way out of a session: the levers a session holds are put
/// back by the process ending, but the session's record stays on disk and the next start resumes
/// it. That is the point — nothing on this machine ends a session.</para>
/// </remarks>
internal static class TrayIconHost
{
    /// <summary>How often the tooltip is recomposed while a session runs. The line carries whole
    /// minutes, so twice a minute is never more than a few seconds behind what a hover shows.</summary>
    private static readonly TimeSpan TooltipInterval = TimeSpan.FromSeconds(30);

    private static TrayHost? _host;
    private static DispatcherQueue? _dispatcher;
    private static DispatcherTimer? _tooltipTimer;
    private static Action? _exit;

    /// <summary>
    /// Puts the icon in the notification area. On the UI thread, once, after the XAML runtime is up.
    /// </summary>
    /// <param name="exit">What leaving from the menu does. Called on the UI thread.</param>
    public static void Start(Action exit)
    {
        ArgumentNullException.ThrowIfNull(exit);

        _exit = exit;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        var host = new TrayHost(new TrayHostOptions
        {
            // The name is the icon's identity in the shell's own icon settings, so it stays exactly
            // this across every version: a changed name is a new icon, and whether the user chose to
            // show it is forgotten with the old one.
            Name = AppInfo.Name,
            Icon = request => TrayIconImage.FromFile(AppIcons.Tray(request.Theme)),
            Tooltip = Tooltip,
            Menu = Menu,
            LeftClick = StatusWindow.Open,
            CacheDirectory = AppPaths.DataDir,
        });

        host.Failed += (_, ex) => AppLog.Error("TrayIconHost", ex);
        host.Start();
        _host = host;

        // The session moves on its own clock and from Home Assistant. The menu is rebuilt by the
        // host on every right click, so only the tooltip needs following.
        FocusSessionService.Changed += OnSessionChanged;

        _tooltipTimer = new DispatcherTimer { Interval = TooltipInterval };
        _tooltipTimer.Tick += (_, _) => RefreshTooltip();
        _tooltipTimer.Start();
    }

    /// <summary>Takes the icon out of the notification area. Nothing else here holds a shell
    /// resource, so this is the whole teardown.</summary>
    public static void Stop()
    {
        FocusSessionService.Changed -= OnSessionChanged;
        _tooltipTimer?.Stop();
        _tooltipTimer = null;
        _host?.Dispose();
        _host = null;
    }

    private static void OnSessionChanged() => _dispatcher?.TryEnqueue(RefreshTooltip);

    private static void RefreshTooltip()
    {
        try { _host?.RefreshTooltip(); }
        catch (Exception ex) { AppLog.Error("TrayIconHost.RefreshTooltip", ex); }
    }

    /// <summary>What a hover says: the product, and the session on a line of its own while one
    /// runs.</summary>
    private static IEnumerable<TrayTooltipLine> Tooltip()
    {
        yield return new TrayTooltipLine(AppInfo.Name);

        var session = FocusSessionService.Current;
        if (session.IsRunning)
            yield return new TrayTooltipLine(FocusSessionStages.Describe(session, DateTimeOffset.Now));
    }

    /// <summary>What the menu holds. Rebuilt by the host on the right click that opens it, so the
    /// session's line is current without anything here keeping it so.</summary>
    private static IEnumerable<TrayMenuItem> Menu()
    {
        var session = FocusSessionService.Current;

        // A line of text and never a control. A person at the keyboard cannot end a session, and
        // nothing may put a way to here because the line looked incomplete without one.
        if (session.IsRunning)
        {
            yield return TrayMenuItem.Command(
                FocusSessionStages.Describe(session, DateTimeOffset.Now), null, isEnabled: false);
            yield return TrayMenuItem.Separator();
        }

        yield return TrayMenuItem.Command("Status…", StatusWindow.Open);
        yield return TrayMenuItem.Command("Settings…", () => SettingsShellHost.Open());
        yield return TrayMenuItem.Separator();
        yield return TrayMenuItem.Command("Exit", () => _exit?.Invoke());
    }
}
