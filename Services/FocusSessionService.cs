using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>
/// The focus session as one mechanism: the levers, the record on disk and the clock that ends it.
/// The Settings page and an MQTT command both resolve through here.
/// </summary>
/// <remarks>Nothing on the machine can end a session, by design. The only ways out are Home
/// Assistant's staged cancel and the session's own duration.</remarks>
internal static class FocusSessionService
{
    /// <summary>Often enough that the ten-second confirm window is reported while it stands, and
    /// cheap enough to leave running: each tick is a comparison against the clock.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private static readonly InputBlock _inputBlock =
        new(NativeMethods.SetInputBlocked, what => AppLog.Info(what));

    private static readonly FocusSessionEngine _engine = new(
        new FocusScreenLever(() => ScreenBrightnessService.IsSupported,
                             ScreenBrightnessService.Set,
                             ScreenBrightnessService.Restore),
        new FocusCoverLever(() => ScreenCoverService.HasDisplay,
                            ScreenCoverService.Show,
                            ScreenCoverService.Hide),
        new FocusInputLever(_inputBlock, FocusInputLever.ElevationRefusal),
        new SettingsFocusSessionRecord(),
        () => DateTimeOffset.Now,
        (what, cause) => AppLog.Info($"{what}{cause.Clause}"),
        FocusHistoryService.Record);

    private static Timer? _timer;

    /// <summary>Raised after the session or its stage moves.</summary>
    public static event Action? Changed
    {
        add    => _engine.Changed += value;
        remove => _engine.Changed -= value;
    }

    /// <summary>The session as every surface reads it.</summary>
    public static FocusSnapshot Current => _engine.Snapshot();

    public static bool IsRunning => _engine.Snapshot().IsRunning;

    /// <summary>
    /// Puts back whatever a previous run left displaced, resumes or ends the session it left, and
    /// starts the clock.
    /// </summary>
    /// <remarks>Runs after <see cref="ScreenBrightnessService.Start"/>, which puts a dimmed display
    /// back first: a session that is resuming then dims it again and parks the level it found, which
    /// is the level the session is owed to put back.</remarks>
    public static void Start()
    {
        // settings.json roams, so it can arrive from another machine carrying no record while this
        // machine is still dimmed.
        SettingsService.Reloaded += KeepRecords;

        _engine.Start();
        _timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
    }

    /// <summary>Starts a session from the defaults the settings hold. The lever choices are whatever
    /// was last left in them; the duration is <paramref name="minutes"/> where a surface asked for
    /// one, and the stored default otherwise.</summary>
    /// <param name="minutes">The duration chosen in the start box. Null from Home Assistant, which
    /// sets the duration through its own number instead.</param>
    public static FocusArmOutcome Arm(ActionCause cause, int? minutes = null)
    {
        var (stored, screen, cover, input) = SettingsService.Read(
            s => (s.FocusSessionMinutes, s.FocusDimsScreen, s.FocusCoversScreen, s.FocusBlocksInput));
        return _engine.Arm(FocusStartRequest.Minutes(minutes, stored), screen, cover, input, cause);
    }

    public static void RequestCancel(ActionCause cause) => _engine.RequestCancel(cause);

    /// <summary>Whether a lever switch may be changed. Refused while a session runs: a lever turned
    /// off part-way through would leave its record parked with nothing owning it.</summary>
    public static bool LeversAreLocked => IsRunning;

    public static void Stop()
    {
        SettingsService.Reloaded -= KeepRecords;
        _timer?.Dispose();
        _timer = null;
        // The cover is a window and dies with the process anyway; taking it down here keeps the
        // shutdown ordered rather than relying on that. The session itself is untouched — its record
        // stays on disk and the next start resumes or ends it.
        ScreenCoverService.Hide(ActionCause.ApplicationClosing());

        // Released here rather than left to the process ending, so a machine that answers does not
        // wait on what a kill does to a block nobody can measure from inside it.
        _inputBlock.Release(ActionCause.ApplicationClosing());
    }

    private static void Tick()
    {
        try { _engine.Tick(); }
        catch (Exception ex) { AppLog.Error("FocusSessionService.Tick", ex); }
    }

    private static void KeepRecords()
    {
        _engine.KeepRecord();
        ScreenBrightnessService.KeepRecord();
    }
}

/// <summary>The record in settings.json, in the Focus section.</summary>
internal sealed class SettingsFocusSessionRecord : IFocusSessionRecord
{
    public FocusSessionRecord? Read()
    {
        var (startedAt, endsAt, screen, cover, input) = SettingsService.Read(
            s => (s.FocusSessionStartedAt, s.FocusSessionEndsAt, s.FocusSessionDimmedScreen,
                  s.FocusSessionCoveredScreen, s.FocusSessionBlockedInput));
        // A document written before the start time was recorded falls back to the end time, which
        // reads as a session with no length. Only the cover's ring uses it, and such a document
        // carries no cover lever, so nothing draws from the fallback.
        return endsAt is { } ends
            ? new FocusSessionRecord(startedAt ?? ends, ends, screen, cover, input)
            : null;
    }

    public bool Save(FocusSessionRecord session) => SettingsService.Update(s =>
    {
        s.FocusSessionStartedAt = session.StartedAt;
        s.FocusSessionEndsAt = session.EndsAt;
        s.FocusSessionDimmedScreen = session.DimsScreen;
        s.FocusSessionCoveredScreen = session.CoversScreen;
        s.FocusSessionBlockedInput = session.BlocksInput;
    });

    public void Clear() => SettingsService.Update(s =>
    {
        s.FocusSessionStartedAt = null;
        s.FocusSessionEndsAt = null;
        s.FocusSessionDimmedScreen = false;
        s.FocusSessionCoveredScreen = false;
        s.FocusSessionBlockedInput = false;
    });
}
