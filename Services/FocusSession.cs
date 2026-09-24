using System.Globalization;

namespace FocusDesk.Services;

/// <summary>What a running session is: when it started, when it ends, and which levers it owns.
/// Held on disk, so a crash, a restart or an update that replaces the executable cannot lose it.</summary>
/// <param name="StartedAt">When the session was armed. Only the cover's countdown ring reads it, to
/// know what a full ring means; nothing about ending a session depends on it.</param>
/// <param name="EndsAt">The instant the session ends, never a countdown — the system clock keeps
/// time whether or not the machine is awake to watch it.</param>
internal readonly record struct FocusSessionRecord(
    DateTimeOffset StartedAt, DateTimeOffset EndsAt, bool DimsScreen, bool CoversScreen);

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
    void Resume(ActionCause cause);

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
/// </remarks>
/// <param name="history">Where a finished session is written down. Behind a seam, so the three
/// endings are exercised without a file.</param>
internal sealed class FocusSessionEngine(
    IFocusLever screen, IFocusLever cover, IFocusSessionRecord record,
    Func<DateTimeOffset> now, Action<string, ActionCause> log,
    Action<FocusHistoryEntry>? history = null)
{
    public const int MinMinutes = 1;

    /// <summary>Long enough that a failure elsewhere costs an afternoon rather than a weekend.</summary>
    public const int MaxMinutes = 240;

    public const int DefaultMinutes = 60;

    private readonly Lock _gate = new();

    private FocusSessionRecord? _session;
    private DateTimeOffset? _cancelRequestedAt;
    private FocusSessionStage _published = FocusSessionStage.Off;

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
            }

            changed = Sync();
        }
        if (changed) Raise();
    }

    /// <summary>Starts a session for <paramref name="minutes"/> using whichever levers are chosen.
    /// Nothing is armed unless every chosen lever can be.</summary>
    public FocusArmOutcome Arm(int minutes, bool dimsScreen, bool coversScreen, ActionCause cause)
    {
        FocusArmOutcome outcome;
        bool changed;

        lock (_gate)
        {
            outcome = ArmLocked(minutes, dimsScreen, coversScreen, cause);
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
                else if (_cancelRequestedAt is not null && Compose().Stage == FocusSessionStage.Active)
                {
                    _cancelRequestedAt = null;
                    log("The focus session's confirm window passed unused, so the session continues to "
                      + "its original end time", "the confirm window");
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

    private FocusArmOutcome ArmLocked(int minutes, bool dimsScreen, bool coversScreen,
                                      ActionCause cause)
    {
        if (_session is not null) return FocusArmOutcome.AlreadyRunning;

        // A switch that turns on and does nothing but count down looks identical to a broken one.
        if (!dimsScreen && !coversScreen)
        {
            log("Focus session refused: no lever at all was chosen, so the session would do nothing "
              + "but count down", cause);
            return FocusArmOutcome.NoLeverChosen;
        }

        if (dimsScreen && screen.Refusal() is { } screenRefusal)
        {
            log($"Focus session refused: {screenRefusal}", cause);
            return FocusArmOutcome.LeverRefused;
        }

        if (coversScreen && cover.Refusal() is { } coverRefusal)
        {
            log($"Focus session refused: {coverRefusal}", cause);
            return FocusArmOutcome.LeverRefused;
        }

        var started = now();
        var session = new FocusSessionRecord(
            started, started.AddMinutes(Math.Clamp(minutes, MinMinutes, MaxMinutes)),
            dimsScreen, coversScreen);

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
        if (coversScreen && !cover.Engage(cause)) return Rollback(session, cause);

        log($"Focus session started, running until "
          + $"{session.EndsAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}", cause);
        return FocusArmOutcome.Armed;
    }

    /// <summary>Undoes a half-armed session, so a lever that failed leaves nothing engaged.</summary>
    private FocusArmOutcome Rollback(FocusSessionRecord session, ActionCause cause)
    {
        if (session.DimsScreen) screen.Lift(cause);
        if (session.CoversScreen) cover.Lift(cause);
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

        record.Clear();
        _session = null;
        _cancelRequestedAt = null;

        // After the levers and the record, so a history write that throws cannot leave a session
        // still holding them.
        history?.Invoke(new FocusHistoryEntry(
            session.StartedAt, session.EndsAt, now(),
            session.DimsScreen, session.CoversScreen, outcome));

        log("Focus session ended", cause);
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
                                 session.DimsScreen, session.CoversScreen);
    }

    /// <summary>Brings the stage last reported into line with the stage now, and says whether it
    /// moved. Called with the lock held, so one comparison covers every path.</summary>
    private bool Sync()
    {
        var stage = Compose().Stage;
        if (stage == _published) return false;
        _published = stage;
        return true;
    }

    private void Raise() => Changed?.Invoke();
}
