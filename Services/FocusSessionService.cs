using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>
/// The focus session as one mechanism: the levers, the record on disk and the clock that ends it.
/// The Settings page and an MQTT command both resolve through here.
/// </summary>
/// <remarks>After the grace period nothing on the machine can end a session, by design. The only ways out are Home
/// Assistant's staged cancel and the session's own duration; the firewall console is the published
/// way out of the network block alone.</remarks>
internal static class FocusSessionService
{
    /// <summary>Often enough that the ten-second confirm window is reported while it stands, and
    /// cheap enough to leave running: each tick is a comparison against the clock.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private static readonly InputBlock _inputBlock =
        new(NativeMethods.SetInputBlocked, what => AppLog.Info(what));

    private static readonly FirewallBlockPark _firewall =
        new(new WindowsFirewallPolicy(), new SettingsFirewallBlockRecord(),
            (what, cause) => AppLog.Info($"{what}{cause.Clause}"));

    private static readonly ProgramGate _programGate =
        new(new WindowInspector(), new WindowEvents((where, ex) => AppLog.Error(where, ex)), new WindowActions(),
            () => DateTimeOffset.Now, what => AppLog.Info(what));

    private static readonly FocusSessionEngine _engine = new(
        new FocusScreenLever(() => ScreenBrightnessService.IsSupported,
                             ScreenBrightnessService.Set,
                             ScreenBrightnessService.Restore),
        new FocusCoverLever(() => ScreenCoverService.HasDisplay,
                            ScreenCoverService.Show,
                            ScreenCoverService.Hide),
        new FocusInputLever(_inputBlock, FocusInputLever.ElevationRefusal),
        new FocusNetworkLever(_firewall, new LiveFocusNetworkTargets(Broker),
                              FocusNetworkLever.ElevationRefusal,
                              (what, cause) => AppLog.Info($"{what}{cause.Clause}"),
                              () => SettingsService.Read(s => FocusAllowedPrograms.Usable(
                                  s.ScreenBreakNetworkExceptions.Where(e => e.Kind == FocusProgramKind.ProgramFile)
                                                                .Select(e => e.Id)))),
        new SettingsFocusSessionRecord(),
        () => DateTimeOffset.Now,
        (what, cause) => AppLog.Info($"{what}{cause.Clause}"),
        FocusHistoryService.Record,
        new FocusProgramLever(_programGate, ProgramGateContext));

    private static Timer? _timer;

    // The named session being armed, so the levers' refusal reads it before the record names it.
    private static readonly Lock _armGate = new();
    private static string? _arming;

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
    /// is the level the session is owed to put back. Runs after <see cref="MqttService.Start"/> too,
    /// so a resuming network block is written against the broker the connection uses.</remarks>
    public static void Start()
    {
        // settings.json roams, so it can arrive from another machine carrying no record while this
        // machine is still blocked and dimmed.
        SettingsService.Reloaded += KeepRecords;

        _engine.Start();
        if (Current is { IsRunning: true } resumed) OpenMarked(resumed.Kind, resumed.Mode, ProgramStartTrigger.Resume, resumed.NamedSession);
        _timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
    }

    /// <summary>Starts a session from the defaults the settings hold. The switches are whatever was
    /// last left in them; the duration and the kind are the ones a surface asked for, and the stored
    /// defaults otherwise.</summary>
    /// <param name="minutes">The duration chosen in the start box. Null from Home Assistant, which
    /// sets the duration through its own number instead.</param>
    /// <param name="kind">The kind chosen in the start box. Null from Home Assistant, which sets the
    /// kind through its own select instead. A kind chosen here is never stored.</param>
    /// <param name="goal">The goal typed in the start box. Null from Home Assistant, which has none.</param>
    /// <param name="namedSession">The named session chosen in the start box. Null from MQTT, which
    /// starts the session in use. A key naming no session in the list refuses the start.</param>
    /// <param name="mode">The mode chosen for this start only; the session's saved mode otherwise.</param>
    /// <param name="openedAhead">The focus point window already opened the marked programs behind it,
    /// so they are not started again.</param>
    public static FocusArmOutcome Arm(ActionCause cause, int? minutes = null, FocusSessionKind? kind = null,
                                      string? goal = null, string? namedSession = null, ProgramFocusMode? mode = null,
                                      bool openedAhead = false)
    {
        var (stored, storedKind, screen, input, network, session, grace) = SettingsService.Read(
            s => (s.FocusSessionMinutes, s.FocusSessionKind, s.FocusDimsScreen, s.FocusBlocksInput,
                  s.FocusBlocksNetwork, NamedSessions.ForStart(s, namedSession), s.FocusGraceSeconds));
        if (session is null)
        {
            AppLog.Info($"Focus: the chosen named session is no longer in the list, so nothing started{cause.Clause}.");
            return FocusArmOutcome.NamedSessionMissing;
        }
        var plan = FocusSessionPlan.For(kind ?? storedKind, network, screen, input, session.Key,
                                        mode ?? session.Mode, session.Programs.Any(p => p.StartWithSession));

        FocusArmOutcome outcome;
        lock (_armGate)
        {
            _arming = plan.NamedSession;
            try { outcome = _engine.Arm(FocusStartRequest.Minutes(minutes, stored), plan, cause, goal, TimeSpan.FromSeconds(grace)); }
            finally { _arming = null; }
        }

        if (outcome == FocusArmOutcome.Armed && !openedAhead)
            OpenMarked(plan.Kind, plan.Mode, ProgramStartTrigger.Start, plan.NamedSession);
        return outcome;
    }

    /// <summary>Opens the marked programs of a start not armed yet, behind the focus point window; the
    /// same rule and the same session as the arm that follows. Null where this start opens nothing.</summary>
    public static ProgramStartRun? OpenAhead(FocusSessionKind kind, string? namedSession, ProgramFocusMode? mode)
    {
        var session = SettingsService.Read(s => NamedSessions.ForStart(s, namedSession));
        return session is null ? null : RunMarked(kind, mode ?? session.Mode, ProgramStartTrigger.Start, session.Key);
    }

    /// <summary>Opens the named session's marked programs where the rules say this moment does.</summary>
    private static void OpenMarked(FocusSessionKind kind, ProgramFocusMode mode, ProgramStartTrigger how,
                                   string? namedSession)
    {
        if (RunMarked(kind, mode, how, namedSession) is { } run) UI.StartingWindow.Show(run);
    }

    private static ProgramStartRun? RunMarked(FocusSessionKind kind, ProgramFocusMode mode, ProgramStartTrigger how,
                                              string? namedSession) =>
        ProgramStartRules.Opens(kind, mode, how)
            ? ProgramStarter.Run(SettingsService.Read(s =>
                  NamedSessions.Find(s, namedSession)?.Programs.Where(p => p.StartWithSession).ToList() ?? []))
            : null;

    /// <summary>What the focus-app lever decides against, read when a session arms or resumes: the
    /// list, the lever-wide action, and the exemptions on this machine. Null where the list cannot be
    /// read, which refuses the lever.</summary>
    private static GateContext? ProgramGateContext()
    {
        try
        {
            var (entries, defaultAction) = SettingsService.Read(
                s => (NamedSessions.ForLevers(s, _arming).Programs.ToList(), s.FocusProgramsDefaultAction));
            string windows = WindowsPrograms.Folder;
            var skip = GateContext.MachineSkipFolders();
            var signed = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            return new GateContext(
                entries, defaultAction, windows, Environment.ProcessPath ?? "",
                SecurityProducts.Folders([.. skip, windows]), skip,
                family => signed.GetOrAdd(family, StartMenuApps.IsFamilySignedByWindows));
        }
        catch (Exception ex)
        {
            AppLog.Error("FocusSessionService.ProgramGateContext", ex);
            return null;
        }
    }

    public static void RequestCancel(ActionCause cause) => _engine.RequestCancel(cause);

    public static bool CancelInGrace(ActionCause cause) => _engine.CancelInGrace(cause);

    /// <summary>Programs open only on a change from a mode that opened nothing.</summary>
    public static FocusGraceChange ChangeMode(ProgramFocusMode mode, ActionCause cause)
    {
        var before = Current;
        bool marks = SettingsService.Read(s => NamedSessions.Find(s, before.NamedSession)?.Programs.Any(p => p.StartWithSession) ?? false);
        var outcome = _engine.ChangeMode(mode, marks, cause);
        if (outcome == FocusGraceChange.Changed && !ProgramStartRules.OpensMarked(before.Mode))
            OpenMarked(before.Kind, mode, ProgramStartTrigger.GraceChange, before.NamedSession);
        return outcome;
    }

    public static bool RowsOpenInGrace(string? key) =>
        Current is { Kind: FocusSessionKind.ProgramFocus } running && running.GraceLeft(DateTimeOffset.Now) is not null
        && running.NamedSession == key;

    /// <summary>A list keeping nothing usable in a limiting mode is refused.</summary>
    public static FocusGraceChange ChangeRunningPrograms(string key, List<FocusProgramEntry> list, ActionCause cause) =>
        _engine.ProgramsChanged(key, limits =>
        {
            if (limits && !FocusProgramLever.KeepsAProgramUsable(list, WindowsPrograms.Folder)) return false;
            SettingsService.Update(s => { if (NamedSessions.Find(s, key) is { } session) session.Programs = list; });
            return true;
        }, cause);

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

        // The firewall outlives the process, so a block left in place by an exit nothing restarts
        // would have no owner at all. The session record keeps the lever, and the next start puts the
        // block back.
        _firewall.Lift(ActionCause.ApplicationClosing());

        // The window watch dies with the process anyway; stopping it here keeps the shutdown ordered.
        // The session record keeps the lever, and the next start takes it up again with a sweep.
        _programGate.Lift();
    }

    /// <summary>The broker the connection is configured with, or null where publishing is off or no
    /// host is set: a block with no reachable broker would leave nothing to end the session from.</summary>
    private static (string? Host, int? Port)? Broker() =>
        MqttService.Current?.Settings.Read() is { Enabled: true, Host.Length: > 0 } broker
            ? (broker.Host, broker.Port)
            : null;

    private static void Tick()
    {
        try { _engine.Tick(); }
        catch (Exception ex) { AppLog.Error("FocusSessionService.Tick", ex); }
    }

    private static void KeepRecords()
    {
        _engine.KeepRecord();
        ScreenBrightnessService.KeepRecord();
        _firewall.KeepRecord();
    }
}

/// <summary>The record in settings.json, in the Focus section.</summary>
internal sealed class SettingsFocusSessionRecord : IFocusSessionRecord
{
    public FocusSessionRecord? Read()
    {
        var (startedAt, endsAt, screen, cover, input, network, programs, kind, goal, named, mode) = SettingsService.Read(
            s => (s.FocusSessionStartedAt, s.FocusSessionEndsAt, s.FocusSessionDimmedScreen,
                  s.FocusSessionCoveredScreen, s.FocusSessionBlockedInput,
                  s.FocusSessionBlockedNetwork, s.FocusSessionLimitedPrograms,
                  s.FocusSessionRunningKind, s.FocusSessionGoal, s.FocusSessionNamedSession, s.FocusSessionMode));
        // A document written before the start time was recorded falls back to the end time, which
        // reads as a session with no length. Only the cover's ring uses it, and such a document
        // carries no cover lever, so nothing draws from the fallback.
        return endsAt is { } ends
            ? new FocusSessionRecord(startedAt ?? ends, ends, screen, cover, input, network, programs, kind,
                                     FocusSessionGoal.Clean(goal), named, mode)
            : null;
    }

    public bool Save(FocusSessionRecord session) => SettingsService.Update(s =>
    {
        s.FocusSessionStartedAt = session.StartedAt;
        s.FocusSessionEndsAt = session.EndsAt;
        s.FocusSessionDimmedScreen = session.DimsScreen;
        s.FocusSessionCoveredScreen = session.CoversScreen;
        s.FocusSessionBlockedInput = session.BlocksInput;
        s.FocusSessionBlockedNetwork = session.BlocksNetwork;
        s.FocusSessionLimitedPrograms = session.LimitsPrograms;
        s.FocusSessionRunningKind = session.Kind;
        s.FocusSessionGoal = session.Goal.Length > 0 ? session.Goal : null;
        s.FocusSessionNamedSession = session.NamedSession;
        s.FocusSessionMode = session.Mode;
    });

    public void Clear() => SettingsService.Update(s =>
    {
        s.FocusSessionStartedAt = null;
        s.FocusSessionEndsAt = null;
        s.FocusSessionRunningKind = FocusSessionKind.ScreenBreak;
        s.FocusSessionGoal = null;
        s.FocusSessionNamedSession = null;
        s.FocusSessionMode = ProgramFocusMode.AllowListed;
        s.FocusSessionDimmedScreen = false;
        s.FocusSessionCoveredScreen = false;
        s.FocusSessionBlockedInput = false;
        s.FocusSessionBlockedNetwork = false;
        s.FocusSessionLimitedPrograms = false;
    });
}
