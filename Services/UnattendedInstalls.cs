using ZeroZero.Primitives;
using ZeroZero.Update;
using ZeroZero.Update.Win32;

namespace FocusDesk.Services;

/// <summary>
/// Installing an update with nobody asked, driven by the shared component's policy. Off unless the
/// About page's switch is on, and nothing is even checked while it is off. With it on, the component
/// checks once a day and starts an installer only where the screen is locked or nothing has touched
/// the keyboard or the mouse for ten minutes; a running focus session refuses every such moment on
/// top of that.
/// </summary>
/// <remarks>
/// <para>A session's own levers make the machine look free: blocked input and a covered screen both
/// leave the keyboard and mouse untouched, so ten minutes into a session the component's rule alone
/// would pass. The session refusal is what keeps an installer from closing FocusDesk mid-session.</para>
/// <para>Nothing on this path reaches a screen. The menu's "Check for updates" and the About page's
/// button keep their own flow and window beside it.</para>
/// </remarks>
internal static class UnattendedInstalls
{
    /// <summary>Why a moment is refused while a session runs. One fixed wording: the component logs a
    /// refusal once per distinct reason, so a reason carrying the minutes left would write a line on
    /// every tick.</summary>
    internal const string SessionRunningReason = "a focus session is running";

    private static readonly Lock _lock = new();
    private static Func<UnattendedUpdatePolicy>? _build;
    private static UnattendedUpdatePolicy? _policy;
    private static bool? _enabled;

    /// <summary>Starts the policy where the setting is on, and follows the setting from then on.
    /// Once, at startup, after the update service exists.</summary>
    /// <param name="service">The one update service the manual flow uses too.</param>
    /// <param name="installing">Told the release's version immediately before its installer starts,
    /// so the handover the next start reports on is recorded.</param>
    /// <param name="shutdown">Mark the exit deliberate and end the process. Called from the policy's
    /// own thread once the installer is running.</param>
    public static void Start(IUpdateService service, Action<string> installing, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(installing);
        ArgumentNullException.ThrowIfNull(shutdown);

        lock (_lock)
        {
            if (_build is not null) return;
            _build = () => new UnattendedUpdatePolicy(service, Options(
                enabled: true, () => FocusSessionService.Current, installing, shutdown, new AppLogSink()));
        }

        SettingsService.Changed  += Follow;
        SettingsService.Reloaded += Follow;
        Follow();
    }

    /// <summary>Whether a moment suits an installer, as far as FocusDesk is concerned. Any session
    /// stage but off refuses, a cancel in progress included: the session still holds its levers.</summary>
    internal static InstallMoment MomentFor(FocusSnapshot session) =>
        session.IsRunning ? InstallMoment.NotNow(SessionRunningReason) : InstallMoment.Now;

    /// <summary>The options the policy runs on. Separated so a test drives the component's real
    /// policy through exactly what FocusDesk hands it. The cadence is the component's own: a check a
    /// day, a retry tick every ten minutes.</summary>
    internal static UnattendedUpdateOptions Options(bool enabled, Func<FocusSnapshot> session,
                                                    Action<string> installing, Action shutdown,
                                                    ILogSink log) => new()
    {
        Enabled       = enabled,
        MayInstallNow = release =>
        {
            var moment = MomentFor(session());
            if (moment.Accepted) installing(release.VersionText);
            return moment;
        },
        Shutdown      = shutdown,
        Log           = log,
    };

    /// <summary>Brings the running policy into line with the setting. The policy reads the switch
    /// once, when it is built, so a change replaces it rather than telling it.</summary>
    private static void Follow()
    {
        try
        {
            bool wanted = SettingsService.Read(s => s.InstallUpdatesUnattended);
            lock (_lock)
            {
                if (_build is null || _enabled == wanted) return;
                _enabled = wanted;

                _policy?.Dispose();
                _policy = null;

                if (wanted)
                {
                    _policy = _build();
                    _policy.Start();
                }
            }

            AppLog.Info(wanted
                ? "Update: installing without being asked is on; checked once a day, never during a focus session."
                : "Update: installing without being asked is off; nothing is checked in the background.");
        }
        catch (Exception ex) { AppLog.Error("UnattendedInstalls.Follow", ex); }
    }
}
