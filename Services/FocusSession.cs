using System.Globalization;

namespace FocusDesk.Services;

/// <summary>What a running session is: when it started, when it ends, and which levers it owns.
/// Held on disk, so a crash, a restart or an update that replaces the executable cannot lose it.</summary>
/// <param name="StartedAt">When the session was armed. Only the cover's countdown ring reads it, to
/// know what a full ring means; nothing about ending a session depends on it.</param>
/// <param name="EndsAt">The instant the session ends, never a countdown — the system clock keeps
/// time whether or not the machine is awake to watch it.</param>
/// <param name="BlocksInput">Whether the session holds the mouse and keyboard. Cleared while the
/// session runs if the block is lost and cannot be taken again, so it says what is held.</param>
/// <param name="BlocksNetwork">Whether the session holds the network block. Cleared where the block
/// could not be written or put back, for the same reason.</param>
/// <param name="LimitsPrograms">Whether the session limits which programs can be used. Cleared where
/// the window watch could not be started or started again, for the same reason.</param>
/// <param name="Kind">The kind the session was started as. Never changes while it runs; the lever
/// fields beside it say what is actually held. A record written before kinds existed reads as a
/// screen break.</param>
internal readonly record struct FocusSessionRecord(
    DateTimeOffset StartedAt, DateTimeOffset EndsAt, bool DimsScreen, bool CoversScreen,
    bool BlocksInput = false, bool BlocksNetwork = false, bool LimitsPrograms = false,
    FocusSessionKind Kind = FocusSessionKind.ScreenBreak);

/// <summary>Where a running session is kept so nothing but the clock can end it.</summary>
internal interface IFocusSessionRecord
{
    FocusSessionRecord? Read();

    /// <summary>False when the record did not reach disk.</summary>
    bool Save(FocusSessionRecord session);

    void Clear();
}

/// <summary>One of the things a session can do to the machine. Each restores only what it itself
/// displaced.</summary>
internal interface IFocusLever
{
    /// <summary>Why this lever cannot be engaged now, or null when it can. Read before anything is
    /// armed, so a session with a lever that would refuse arms nothing at all.</summary>
    string? Refusal();

    bool Engage(ActionCause cause);

    /// <summary>Puts the lever back on for a session that outlived the application. Separate from
    /// <see cref="Engage"/> because a lever can answer differently: a brightness does not survive a
    /// restart and the startup restore has just put it back, while something the platform carries
    /// across on its own would need no second engagement.</summary>
    /// <returns>Whether the lever holds again.</returns>
    bool Resume(ActionCause cause);

    /// <summary>Called on every tick while the session owns the lever. False where the lever has
    /// been lost and could not be taken again. A lever the platform holds by itself has nothing to
    /// renew.</summary>
    bool Hold(DateTimeOffset until, ActionCause cause) => true;

    /// <summary>Puts back what this lever displaced. False leaves the record for the next start.</summary>
    bool Lift(ActionCause cause);
}

/// <summary>
/// A focus session: one duration, the levers it owns, and no way out from the machine itself.
/// </summary>
/// <remarks>
/// <para>Pure but for the seams handed in, so every rule here — the refusals, the staged cancel, the
/// expiry lifted at the next start — is exercised against fakes rather than against a display.</para>
/// <para>The session is defined by an end time on disk. Nothing relies on a timer that only runs
/// while the application does: the running timer ends a session that expires while the application
/// is up, and <see cref="Start"/> is what ends one that expired while it was not.</para>
/// <para>A session arms from a <see cref="FocusSessionPlan"/>, and the lever its kind always holds —
/// the cover for a screen break, the program limit for program focus — must arm: where it refuses,
/// the session is refused with that lever's own reason, and where it fails, whatever did engage is
/// rolled back. The screen dim arms fully or not at all in the same way. The network block and the
/// input block fail safe instead: where one is refused, fails or is lost, the session runs on without
/// it and reports it as not held. A held lever lost part-way through is dropped the same way, the
/// program limit included.</para>
/// </remarks>
/// <param name="history">Where a finished session is written down. Behind a seam, so the three
/// endings are exercised without a file.</param>
/// <param name="programs">The focus-app lever, which limits the session to the chosen programs. Null
/// is a lever that is never chosen.</param>
internal sealed class FocusSessionEngine(
    IFocusLever screen, IFocusLever cover, IFocusLever input, IFocusLever network,
    IFocusSessionRecord record,
    Func<DateTimeOffset> now, Action<string, ActionCause> log,
    Action<FocusHistoryEntry>? history = null, IFocusLever? programs = null)
{
    private readonly IFocusLever _programs = programs ?? new AbsentLever();

    /// <summary>A lever that is never there to engage and has nothing to lift.</summary>
    private sealed class AbsentLever : IFocusLever
    {
        public string? Refusal() => "this build has no program limit";
        public bool Engage(ActionCause cause) => false;
        public bool Resume(ActionCause cause) => false;
        public bool Lift(ActionCause cause) => true;
    }

    public const int MinMinutes = 1;

    /// <summary>Long enough that a failure elsewhere costs an afternoon rather than a weekend.</summary>
    public const int MaxMinutes = 240;

    public const int DefaultMinutes = 60;

    private readonly Lock _gate = new();

    private FocusSessionRecord? _session;
    private DateTimeOffset? _cancelRequestedAt;
    private FocusSessionStage _published = FocusSessionStage.Off;

    // Set when a lever is dropped from a running session, so the change is raised although the
    // stage did not move.
    private bool _leversMoved;

    /// <summary>Raised after the session or its stage moves, so every surface reflects it without
    /// waiting for its own refresh.</summary>
    public event Action? Changed;

    public FocusSnapshot Snapshot()
    {
        lock (_gate) return Compose();
    }

    /// <summary>
    /// Puts back whatever a previous run left displaced, and resumes or ends the session it left.
    /// </summary>
    /// <remarks>Called once at startup, and the whole of what makes a session survive being switched
    /// off: a recorded end time already in the past is lifted here, because nothing was running to
    /// notice it pass.</remarks>
    public void Start()
    {
        bool changed;
        lock (_gate)
        {
            if (record.Read() is { } saved)
            {
                _session = saved;
                // The stage is never trusted across a restart: the confirm window is a live
                // interaction, and a restart must neither end a session nor grant an open window.
                _cancelRequestedAt = null;

                if (saved.EndsAt <= now())
                    Finish("a session that ran its length while the application was not running",
                           FocusSessionOutcome.FoundStale);
                else
                    Resume();
            }
            else
            {
                // A lever record with nothing owning it: the run died before the session record was
                // written, or after it was cleared. Nothing was owed, so everything goes back.
                var leftBehind = ActionCause.StartupRestore("a focus lever");
                screen.Lift(leftBehind);
                cover.Lift(leftBehind);
                input.Lift(leftBehind);
                network.Lift(leftBehind);
                _programs.Lift(leftBehind);
            }

            changed = Sync();
        }
        if (changed) Raise();
    }

    /// <summary>Starts a session for <paramref name="minutes"/> holding the levers the plan names.
    /// Nothing is armed unless the lever the plan's kind always holds can be.</summary>
    public FocusArmOutcome Arm(int minutes, FocusSessionPlan plan, ActionCause cause)
    {
        ArgumentNullException.ThrowIfNull(plan);
        FocusArmOutcome outcome;
        bool changed;

        lock (_gate)
        {
            outcome = ArmLocked(minutes, plan, cause);
            changed = Sync();
        }

        if (changed) Raise();
        return outcome;
    }

    /// <summary>A cancel request. The first opens the wait, a repeat during it changes nothing, and
    /// one inside the confirm window ends the session.</summary>
    public void RequestCancel(ActionCause cause)
    {
        bool changed;
        lock (_gate)
        {
            if (_session is null) return;

            switch (Compose().Stage)
            {
                case FocusSessionStage.Active:
                    _cancelRequestedAt = now();
                    log($"Focus session cancel asked for: it can be confirmed in "
                      + $"{FocusSessionStages.CancelWait.TotalMinutes:0} minutes, for "
                      + $"{FocusSessionStages.ConfirmWindow.TotalSeconds:0} seconds", cause);
                    break;

                // A fixed clock from the first request: a repeat neither shortens nor restarts it, so
                // pressing again has no effect worth relying on.
                case FocusSessionStage.Ending:
                    log("Focus session cancel repeated while the wait runs, which changes nothing", cause);
                    break;

                case FocusSessionStage.Confirm:
                    Finish(cause, FocusSessionOutcome.EndedEarly);
                    break;
            }

            changed = Sync();
        }
        if (changed) Raise();
    }

    /// <summary>Ends a session that has run its length, and drops a cancel attempt whose window
    /// passed. Called on a timer while the application runs.</summary>
    public void Tick()
    {
        bool changed;
        lock (_gate)
        {
            if (_session is { } session)
            {
                if (now() >= session.EndsAt)
                    Finish("the session ran its length", FocusSessionOutcome.RanToTime);
                else
                {
                    if (_cancelRequestedAt is not null && Compose().Stage == FocusSessionStage.Active)
                    {
                        _cancelRequestedAt = null;
                        log("The focus session's confirm window passed unused, so the session continues "
                          + "to its original end time", "the confirm window");
                    }

                    // The input block lapses unless this renews it, so a hung application lifts it
                    // within seconds; one that lapsed while the session still owns it is taken again.
                    ActionCause clock = "the session's own clock";
                    if (session.BlocksInput && !input.Hold(session.EndsAt, clock))
                        DropInput("the mouse and keyboard block was lost and could not be taken again",
                                  clock);
                    if (session.BlocksNetwork && !network.Hold(session.EndsAt, clock))
                        DropNetwork("the network block was lost and could not be put back", clock);
                    if (session.LimitsPrograms && !_programs.Hold(session.EndsAt, clock))
                        DropPrograms("the window watch stopped and could not be started again", clock);
                }
            }

            changed = Sync();
        }
        if (changed) Raise();
    }

    /// <summary>Re-saves the record when a reloaded settings document has lost it — settings.json
    /// roams, so it can arrive from another machine while this one is still dimmed.</summary>
    public void KeepRecord()
    {
        lock (_gate)
        {
            if (_session is { } session && record.Read() is null && record.Save(session))
                AppLog.Info("Focus: reloaded settings carried no running session while one is still "
                          + "holding its levers — restoring the record from this session.");
        }
    }

    private FocusArmOutcome ArmLocked(int minutes, FocusSessionPlan plan, ActionCause cause)
    {
        if (_session is not null) return FocusArmOutcome.AlreadyRunning;

        // The lever the kind always holds is the session: without it, it refuses in its own words.
        if (LeverFor(plan.Required).Refusal() is { } requiredRefusal)
        {
            log($"Focus session refused: {requiredRefusal}", cause);
            return FocusArmOutcome.LeverRefused;
        }

        bool dimsScreen = plan.DimsScreen, blocksInput = plan.BlocksInput,
             blocksNetwork = plan.BlocksNetwork;

        if (dimsScreen && screen.Refusal() is { } screenRefusal)
        {
            log($"Focus session refused: {screenRefusal}", cause);
            return FocusArmOutcome.LeverRefused;
        }

        // Fails safe: a refused network or input block is left off and the session runs on the
        // other levers.
        if (blocksNetwork && network.Refusal() is { } networkRefusal)
        {
            log($"Focus session leaves the network open: {networkRefusal}", cause);
            blocksNetwork = false;
        }

        if (blocksInput && input.Refusal() is { } inputRefusal)
        {
            log($"Focus session leaves the mouse and keyboard free: {inputRefusal}", cause);
            blocksInput = false;
        }

        var started = now();
        var session = new FocusSessionRecord(
            started, started.AddMinutes(Math.Clamp(minutes, MinMinutes, MaxMinutes)),
            dimsScreen, plan.CoversScreen, blocksInput, blocksNetwork, plan.LimitsPrograms, plan.Kind);

        // The record reaches disk before a lever moves: a crash between the two has to leave a
        // session the next start can end, never a lever nothing owns.
        if (!record.Save(session))
        {
            log("Focus session refused: it could not be written down first, so a crash would have "
              + "left the machine dimmed with nothing to end it", cause);
            return FocusArmOutcome.LeverFailed;
        }

        _session = session;
        _cancelRequestedAt = null;

        if (dimsScreen && !screen.Engage(cause)) return Rollback(session, cause);
        if (session.CoversScreen && !cover.Engage(cause)) return Rollback(session, cause);
        // The program limit only ever comes as the lever program focus always holds, so a watch that
        // will not start refuses the session rather than leaving it running without it.
        if (session.LimitsPrograms && !_programs.Engage(cause)) return Rollback(session, cause);

        if (blocksNetwork && !network.Engage(cause))
            DropNetwork("the firewall would not take the block", cause);

        // Last, so nothing above can fail while the machine already answers nothing.
        if (blocksInput && !input.Engage(cause))
            DropInput("Windows refused to block the mouse and keyboard", cause);

        log($"Focus session started as {FocusSessionKinds.Word(plan.Kind)}, running until "
          + $"{session.EndsAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}", cause);
        return FocusArmOutcome.Armed;
    }

    private IFocusLever LeverFor(FocusLever lever) => lever switch
    {
        FocusLever.Screen   => screen,
        FocusLever.Cover    => cover,
        FocusLever.Input    => input,
        FocusLever.Network  => network,
        _                   => _programs,
    };

    /// <summary>Undoes a half-armed session, so a lever that failed leaves nothing engaged.</summary>
    private FocusArmOutcome Rollback(FocusSessionRecord session, ActionCause cause)
    {
        if (session.DimsScreen) screen.Lift(cause);
        if (session.CoversScreen) cover.Lift(cause);
        if (session.BlocksInput) input.Lift(cause);
        if (session.BlocksNetwork) network.Lift(cause);
        if (session.LimitsPrograms) _programs.Lift(cause);
        record.Clear();
        _session = null;
        _cancelRequestedAt = null;
        log("Focus session not started: a lever could not be engaged, and what did engage is back as "
          + "it was", cause);
        return FocusArmOutcome.LeverFailed;
    }

    /// <summary>Picks up a session that outlived the application, letting each lever decide for
    /// itself what putting it back means.</summary>
    private void Resume()
    {
        if (_session is not { } session) return;
        ActionCause cause = "resuming a session the machine was switched off during";
        if (session.DimsScreen) screen.Resume(cause);
        if (session.CoversScreen) cover.Resume(cause);
        if (session.BlocksNetwork && !network.Resume(cause))
            DropNetwork("the network block could not be put back", cause);
        if (session.BlocksInput && !input.Resume(cause))
            DropInput("the mouse and keyboard block could not be taken again", cause);
        if (session.LimitsPrograms && !_programs.Resume(cause))
            DropPrograms("the window watch could not be started again", cause);
        log($"Focus session resumed, running until "
          + $"{session.EndsAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}",
            ActionCause.Startup());
    }

    /// <summary>Lifts every lever the session owns and clears the record. A lever that will not lift
    /// keeps its own record, which the next start puts back; the session still ends, because nothing
    /// is holding it any more.</summary>
    private void Finish(ActionCause cause, FocusSessionOutcome outcome)
    {
        if (_session is not { } session) return;

        if (session.DimsScreen && !screen.Lift(cause))
            log("The screen brightness could not be put back — it is retried at the next start", cause);
        if (session.CoversScreen && !cover.Lift(cause))
            log("The screen cover could not be taken down — it goes with the next restart", cause);
        if (session.BlocksInput && !input.Lift(cause))
            log("The mouse and keyboard block did not release at once — it lapses by itself within "
              + "seconds", cause);
        if (session.BlocksNetwork && !network.Lift(cause))
            log("The firewall could not be put back — it is retried at the next start", cause);
        // Nothing to put back: minimised programs stay minimised until somebody restores them.
        if (session.LimitsPrograms) _programs.Lift(cause);

        record.Clear();
        _session = null;
        _cancelRequestedAt = null;

        // After the levers and the record, so a history write that throws cannot leave a session
        // still holding them.
        history?.Invoke(new FocusHistoryEntry(
            session.StartedAt, session.EndsAt, now(),
            session.DimsScreen, session.CoversScreen, outcome, session.BlocksInput,
            session.BlocksNetwork, session.LimitsPrograms, session.Kind));

        log("Focus session ended", cause);
    }

    /// <summary>Carries on without the input block: the session keeps running on its other levers
    /// and reports the block as not held. Called with the lock held.</summary>
    private void DropInput(string why, ActionCause cause)
    {
        if (_session is { BlocksInput: true } session)
            Drop(session with { BlocksInput = false }, "the mouse and keyboard free", why, cause);
    }

    /// <summary>Carries on without the network block, as <see cref="DropInput"/> does. Called with
    /// the lock held.</summary>
    private void DropNetwork(string why, ActionCause cause)
    {
        if (_session is { BlocksNetwork: true } session)
            Drop(session with { BlocksNetwork = false }, "the network open", why, cause);
    }

    /// <summary>Carries on without the program limit, as <see cref="DropInput"/> does. Called with the
    /// lock held.</summary>
    private void DropPrograms(string why, ActionCause cause)
    {
        if (_session is { LimitsPrograms: true } session)
        {
            _programs.Lift(cause);
            Drop(session with { LimitsPrograms = false }, "every program usable", why, cause);
        }
    }

    private void Drop(FocusSessionRecord without, string freed, string why, ActionCause cause)
    {
        _session = without;
        _leversMoved = true;
        if (!record.Save(without))
            log($"The session record could not be updated to say it runs with {freed}", cause);
        log($"Focus session continues with {freed}: {why}", cause);
    }

    /// <summary>The session as it stands. Called with the lock held.</summary>
    private FocusSnapshot Compose()
    {
        if (_session is not { } session) return FocusSnapshot.None;

        var stage = FocusSessionStage.Active;
        if (_cancelRequestedAt is { } requested)
        {
            var elapsed = now() - requested;
            if (elapsed < FocusSessionStages.CancelWait) stage = FocusSessionStage.Ending;
            else if (elapsed < FocusSessionStages.CancelWait + FocusSessionStages.ConfirmWindow)
                stage = FocusSessionStage.Confirm;
        }

        return new FocusSnapshot(stage, session.StartedAt, session.EndsAt,
                                 session.DimsScreen, session.CoversScreen, session.BlocksInput,
                                 session.BlocksNetwork, session.LimitsPrograms, session.Kind);
    }

    /// <summary>Brings the stage last reported into line with the stage now, and says whether it
    /// moved. Called with the lock held, so one comparison covers every path.</summary>
    private bool Sync()
    {
        bool leversMoved = _leversMoved;
        _leversMoved = false;

        var stage = Compose().Stage;
        if (stage == _published) return leversMoved;
        _published = stage;
        return true;
    }

    private void Raise() => Changed?.Invoke();
}
