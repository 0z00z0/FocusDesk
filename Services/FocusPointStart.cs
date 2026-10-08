namespace FocusDesk.Services;

/// <summary>
/// The breathing exercise between the pop-out's Start button and the session it starts: the kind and
/// length chosen, how long the exercise runs, whether its time has run, and the single arm it may
/// lead to.
/// </summary>
/// <remarks>Nothing is armed before the exercise's time has run. Closing the window settles it without
/// arming, before the end or after it, and nothing reopens it.</remarks>
internal sealed class FocusPointStart(FocusSessionKind kind, int minutes, TimeSpan length, bool startsItself,
                                      Func<TimeSpan> elapsed)
{
    internal const int DefaultSeconds = 60;
    internal const int MinSeconds     = 10;
    internal const int MaxSeconds     = 300;

    private bool _settled;

    /// <summary>A stored duration in seconds, or the default where it falls outside the bounds.</summary>
    internal static int SecondsOrDefault(int? seconds) =>
        seconds is >= MinSeconds and <= MaxSeconds ? seconds.Value : DefaultSeconds;

    /// <summary>How long the exercise runs before the session can be started. Fixed when the window
    /// opens rather than read from anything drawn, so the start cannot come early whatever the window
    /// does.</summary>
    internal TimeSpan Length { get; } = length;

    /// <summary>Whether the session starts by itself when the time runs out, with no button.</summary>
    internal bool StartsItself { get; } = startsItself;

    /// <summary>How long is left before the session may start; zero once it may.</summary>
    internal TimeSpan Remaining => Length - elapsed() is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero;

    /// <summary>Whether the session may start: the time has run, and neither a start nor a close has
    /// settled it.</summary>
    internal bool CanBegin => !_settled && elapsed() >= Length;

    /// <summary>Settles without arming. Called as the window closes, however it closes.</summary>
    internal void Cancel() => _settled = true;

    /// <summary>Arms the chosen kind for the chosen length through <paramref name="arm"/>, once. Null
    /// where the time has not run or the start is already settled, and nothing is armed then.</summary>
    internal FocusArmOutcome? Begin(Func<int, FocusSessionKind, FocusArmOutcome> arm)
    {
        if (!CanBegin) return null;
        _settled = true;
        return arm(minutes, kind);
    }

    /// <summary>What the end of the time does by itself: arms once where the start is set to start
    /// itself, and nothing otherwise, which leaves the button to do it.</summary>
    internal FocusArmOutcome? Finish(Func<int, FocusSessionKind, FocusArmOutcome> arm) =>
        StartsItself ? Begin(arm) : null;
}
