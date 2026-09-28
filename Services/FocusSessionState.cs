using System.Globalization;

namespace FocusDesk.Services;

/// <summary>What a focus session is doing right now, as a reader of the published surface needs it:
/// whether one is running at all, and how far a cancel attempt has got.</summary>
internal enum FocusSessionStage
{
    /// <summary>No session is running.</summary>
    Off,

    /// <summary>A session is running and no cancel attempt is in flight.</summary>
    Active,

    /// <summary>A cancel was asked for and the wait before it can be confirmed is running.</summary>
    Ending,

    /// <summary>The wait has run out; a second cancel request inside this window ends the session.</summary>
    Confirm,
}

/// <summary>The word each stage is published as, and the timing the staged cancel runs on.</summary>
/// <remarks>The words are part of the published contract: a receiver holds the state against the
/// declared list and every automation compares against these literals.</remarks>
internal static class FocusSessionStages
{
    /// <summary>How long a cancel request waits before it can be confirmed. A delay rather than a
    /// repeated prompt: a prompt becomes an autopilot tap, a wait does not.</summary>
    public static readonly TimeSpan CancelWait = TimeSpan.FromMinutes(5);

    /// <summary>How long the confirm window stands after the wait runs out. Missing it leaves the
    /// session running and the next attempt starts the wait afresh.</summary>
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(10);

    public static string Label(FocusSessionStage stage) => stage switch
    {
        FocusSessionStage.Active  => "Active",
        FocusSessionStage.Ending  => "Ending",
        FocusSessionStage.Confirm => "Confirm",
        _                         => "Off",
    };

    /// <summary>Every word the entity can publish, in the order the stages are declared.</summary>
    public static IReadOnlyList<string> Words { get; } =
        [.. Enum.GetValues<FocusSessionStage>().Select(Label)];

    /// <summary>The session in one line, led by its kind: the tray menu, the hover text, the status
    /// window and the Focus page all show this, so they can never disagree. Nothing it says can be
    /// acted on: nothing local ends a session.</summary>
    public static string Describe(FocusSnapshot session, DateTimeOffset now) =>
        Describe(session, now, AppText.Get);

    /// <summary>The same line, with the interface text read through <paramref name="text"/>, so a
    /// test can compose it from each shipped resource file.</summary>
    /// <remarks>One whole template per stage, with the kind, the minutes and the held levers as
    /// numbered placeholders, so a translation can move every part.</remarks>
    internal static string Describe(FocusSnapshot session, DateTimeOffset now, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!session.IsRunning) return text("FocusDescribeNotRunning");

        // The kind's own lever first, then the optional ones.
        var levers = new List<string>(5);
        if (session.CoversScreen)   levers.Add(text("FocusDetailScreenCovered"));
        if (session.LimitsPrograms) levers.Add(text("FocusDetailProgramsLimited"));
        if (session.BlocksInput)    levers.Add(text("FocusDetailInputBlocked"));
        if (session.DimsScreen)     levers.Add(text("FocusDetailScreenDimmed"));
        if (session.BlocksNetwork)  levers.Add(text("FocusDetailNetworkBlocked"));

        string template = text(session.Stage switch
        {
            FocusSessionStage.Ending  => "FocusDescribeEnding",
            FocusSessionStage.Confirm => "FocusDescribeConfirm",
            _                         => "FocusDescribeActive",
        });
        string kind = text(session.Kind == FocusSessionKind.ProgramFocus
                               ? "FocusKindProgramFocus"
                               : "FocusKindScreenBreak");
        string held = levers.Count > 0 ? string.Join(", ", levers) : text("FocusDetailNothingHeld");

        return string.Format(CultureInfo.CurrentCulture, template, kind, session.MinutesLeft(now) ?? 0, held);
    }
}

/// <summary>The session as the published surface reports it. Composed under the engine's own lock, so
/// the stage and the levers cannot disagree.</summary>
/// <param name="StartedAt">Null when no session is running. When the session was armed — the
/// reference a full countdown ring is drawn against, and nothing else.</param>
/// <param name="EndsAt">Null when no session is running. The instant the session ends, never a
/// countdown: the system clock keeps time whether or not the machine is awake to watch it.</param>
/// <param name="BlocksInput">Whether the mouse and keyboard are held. False for a session that
/// asked for the block and was refused it, or lost it: the lever fails safe.</param>
/// <param name="BlocksNetwork">Whether the network block is held, on the same terms.</param>
/// <param name="LimitsPrograms">Whether only the chosen programs can be used, on the same terms.</param>
/// <param name="Kind">The kind the running session was started as. Meaningless when none is
/// running.</param>
internal readonly record struct FocusSnapshot(
    FocusSessionStage Stage, DateTimeOffset? StartedAt, DateTimeOffset? EndsAt,
    bool DimsScreen, bool CoversScreen, bool BlocksInput = false, bool BlocksNetwork = false,
    bool LimitsPrograms = false, FocusSessionKind Kind = FocusSessionKind.ScreenBreak)
{
    public static readonly FocusSnapshot None =
        new(FocusSessionStage.Off, null, null, false, false);

    public bool IsRunning => Stage != FocusSessionStage.Off;

    /// <summary>Whole minutes until the session ends, never negative, and null when none is running.
    /// Rounded up, so a session with seconds left still reads as a minute rather than as none.</summary>
    public int? MinutesLeft(DateTimeOffset now) =>
        EndsAt is { } at ? Math.Max(0, (int)Math.Ceiling((at - now).TotalMinutes)) : null;
}

/// <summary>Why a session was not armed. <see cref="Armed"/> is the only outcome that changed
/// anything.</summary>
internal enum FocusArmOutcome
{
    Armed,

    /// <summary>A session is already running, so there is nothing to arm.</summary>
    AlreadyRunning,

    /// <summary>The lever the kind always holds refused — nothing is attached for a cover to go over,
    /// or no program is kept usable — or the display accepts no brightness for a chosen dim. Nothing is
    /// armed, and nothing is half-engaged.</summary>
    LeverRefused,

    /// <summary>Every lever agreed and one then failed to engage. Whatever did engage is lifted
    /// again before this is returned.</summary>
    LeverFailed,
}
