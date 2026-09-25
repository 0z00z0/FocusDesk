using System.Diagnostics;
using FocusDesk.Helpers;
using Microsoft.UI.Xaml;
using ZeroZero.Update;
using ZeroZero.Update.Win32;
using ZeroZero.Update.WinUI;

namespace FocusDesk.Services;

/// <summary>
/// The application's updates: the configured component, the one check every surface joins, the
/// release the last check found, and the install a found release starts.
/// </summary>
/// <remarks>
/// <para>Every check runs silently, so the component puts nothing on screen of its own and each
/// surface decides what to report. The component draws a WinUI window and has no message-box
/// fallback, so <see cref="InstallAsync"/> and anything reached through <see cref="Prompts"/> are
/// called from the thread that owns the application's windows.</para>
/// <para>Inert until <see cref="Start"/> has run. A surface asking before then gets nothing rather
/// than a check against an unconfigured component.</para>
/// </remarks>
internal static class AppUpdates
{
    /// <summary>The release certificate's subject, exactly as signtool writes it.</summary>
    internal const string ExpectedPublisher = "CN=ZeroZero Software";

    private const string Owner = "0z00z0";
    private const string Repository = "FocusDesk";

    /// <summary>The release asset to download. <c>{version}</c> is the component's own placeholder,
    /// expanded to the release's version text; the asset match is exact and case-sensitive, so no
    /// other spelling and no wildcard finds the installer.</summary>
    internal const string InstallerAssetName = "FocusDesk-Setup-{version}.exe";

    /// <summary>Prefix of the per-run download directory. Each download goes to a fresh,
    /// unpredictable name under it, so nothing can plant a file at a guessable path.</summary>
    private const string DownloadDirectoryPrefix = "FocusDesk-Update-";

    /// <summary>How old a leftover download must be before a sweep may take it. A sweep carries on
    /// past an entry it cannot remove, so one running beside a live install could take that install's
    /// own file with it; an hour puts this run's directory out of reach.</summary>
    private static readonly TimeSpan StaleDownloadAge = TimeSpan.FromHours(1);

    private static UpdateService? _service;
    private static Action? _shutdown;

    /// <summary>The one check every surface joins. Null before <see cref="Start"/>.</summary>
    public static UpdateCheckCoordinator? Checks { get; private set; }

    /// <summary>The release the last check found, or null where it found none. What the
    /// notification-area menu's badge is drawn from, so it survives the menu being rebuilt.</summary>
    public static ReleaseInfo? Available { get; private set; }

    /// <summary>The version a check compares a release against.</summary>
    public static Version? RunningVersion => _service?.RunningVersion;

    /// <summary>Configures the component and clears leftovers from an earlier run. Once, at startup,
    /// while no install can be in flight.</summary>
    /// <param name="shutdown">What the flow calls once the installer is running: mark the exit
    /// deliberate and end the process. Called on the thread that owns the windows.</param>
    public static void Start(Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
        if (_service is not null) return;

        try
        {
            _shutdown = shutdown;
            _service  = new UpdateService(Options(), source: null,
                                          launcher: new ShellInstallerLauncher());
            Checks    = new UpdateCheckCoordinator(CheckAsync);

            _service.SweepStaleDownloads(StaleDownloadAge);
        }
        catch (Exception ex) { AppLog.Error("AppUpdates.Start", ex); }
    }

    /// <summary>The component's own window and wording for every outcome a caller chooses to report.
    /// Built per use: the window is a field of the prompts, so one instance would hand a second
    /// caller the first one's window.</summary>
    public static IUpdatePrompts Prompts() => new UpdateWindowPrompts(new UpdateWindowOptions
    {
        ApplicationName = AppInfo.Name,
        // FocusDesk follows the system light/dark setting, and the component's window is its own
        // element tree rather than a child of the application's.
        Theme           = ElementTheme.Default,
        ReleaseNotes    = release => ReleaseNotesText.Strip(release.Body ?? ""),
    });

    /// <summary>
    /// Runs the check, or joins the one running, and reports every outcome in the component's own
    /// window. What the notification-area menu's "Check for updates" and its badge both do: neither
    /// has a control of its own to show an outcome on.
    /// </summary>
    /// <remarks>On the thread that owns the windows, and awaited by nobody: the reporting window is
    /// the caller's answer.</remarks>
    public static async Task CheckAndReportAsync()
    {
        if (Checks is not { } checks)
        {
            AppLog.Info("Update: a check was asked for before the update service was started.");
            return;
        }

        try
        {
            var run = await checks.Run();

            if (run.Result == UpdateFlowResult.UpdateAvailable && run.Release is { } release)
            {
                await InstallAsync(release);
                return;
            }

            await SayAsync(run);
        }
        catch (Exception ex) { AppLog.Error("AppUpdates.CheckAndReportAsync", ex); }
    }

    /// <summary>Offers and installs a release already found, with no second check. The question, the
    /// download and every refusal are the component's own window, which it opens and closes
    /// itself.</summary>
    public static async Task InstallAsync(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (_service is not { } service || _shutdown is not { } shutdown) return;

        try { await new UpdateFlow(service, Prompts(), FlowOptions(shutdown)).InstallAsync(release); }
        catch (Exception ex) { AppLog.Error("AppUpdates.InstallAsync", ex); }
    }

    /// <summary>One check, reported by nobody: the caller reads the run and decides what to show.
    /// Silent, so the component puts nothing on screen for any outcome.</summary>
    private static async Task<UpdateFlowRun> CheckAsync()
    {
        var service  = _service!;
        var shutdown = _shutdown!;

        var run = await new UpdateFlow(service, Prompts(), FlowOptions(shutdown))
                            .RunAsync(UpdateTrigger.Silent);

        // Held so the menu's badge survives being rebuilt on every right click. Cleared on any other
        // outcome, so a release installed or withdrawn does not leave the badge behind.
        Available = run.Result == UpdateFlowResult.UpdateAvailable ? run.Release : null;
        return run;
    }

    /// <summary>The component's wording for an outcome that is not an available release. Each one is
    /// named: a result added later must not inherit a sibling's wording.</summary>
    /// <remarks>On the thread that owns the windows: the component draws one and has no
    /// fallback.</remarks>
    public static async Task SayAsync(UpdateFlowRun run)
    {
        var prompts = Prompts();
        switch (run.Result)
        {
            case UpdateFlowResult.UpToDate when RunningVersion is { } running:
                await prompts.SayUpToDateAsync(running);
                break;

            case UpdateFlowResult.NothingReleased:
                await prompts.SayNothingReleasedAsync();
                break;

            case UpdateFlowResult.CheckFailed when run.Check is { } check:
                await prompts.SayCheckFailedAsync(check);
                break;

            default:
                AppLog.Info($"Update check ended {run.Result}; nothing is shown for it.");
                break;
        }
    }

    private static UpdateOptions Options() => new()
    {
        RepositoryOwner = Owner,
        RepositoryName  = Repository,
        ProductName     = AppInfo.Name,
        // Named rather than left to the entry assembly, so the comparison uses the same number the
        // About window and the release tag do.
        RunningVersion  = Version.TryParse(AppInfo.Version, out var running) ? running : new Version(1, 0, 0),
        // Publisher name alone, no pinned thumbprint: the release certificate is self-signed, and a
        // pin would turn its next rotation into a silent update outage.
        ExpectedSigner  = new ExpectedSigner(ExpectedPublisher, certificateThumbprints: null,
                                             acceptSelfSignedSubject: true),
        DirectoryPrefix    = DownloadDirectoryPrefix,
        InstallerFileName  = InstallerAssetName,
        InstallerArguments = UnattendedUpdate.Arguments(UnattendedUpdate.InstallerLogPath),
        Log                = new AppLogSink(),
    };

    // No Progress: the component's window reports into itself through the download surface it hands
    // the flow, and a second reporter here would have nothing to draw with.
    private static UpdateFlowOptions FlowOptions(Action shutdown) => new()
    {
        Shutdown        = shutdown,
        OpenReleasePage = OpenInBrowser,
        Log             = new AppLogSink(),
    };

    /// <summary>Opens an address in the default browser. Never throws.</summary>
    internal static void OpenInBrowser(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Error("AppUpdates.OpenInBrowser", ex); }
    }
}
