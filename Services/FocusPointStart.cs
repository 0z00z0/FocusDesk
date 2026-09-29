namespace FocusDesk.Services;

/// <summary>
/// The one-minute breathing exercise between the pop-out's Start button and the session it starts:
/// the kind and length chosen, whether the minute has run, and the single arm it may lead to.
/// </summary>
/// <remarks>Nothing is armed until <see cref="Begin"/> is called after the minute. Closing the window
/// settles it without arming, before the minute or after it, and nothing reopens it.</remarks>
internal sealed class FocusPointStart(FocusSessionKind kind, int minutes, Func<TimeSpan> elapsed)
{
    /// <summary>How long the exercise runs before the session can be started. Fixed here rather than
    /// read from anything drawn, so the button cannot show early whatever the window does.</summary>
    internal static readonly TimeSpan Length = TimeSpan.FromSeconds(60);

    private bool _settled;

    /// <summary>How long is left before the start button may show; zero once it may.</summary>
    internal TimeSpan Remaining => Length - elapsed() is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero;

    /// <summary>Whether the start button may show and be acted on: the minute has run, and neither a
    /// start nor a close has settled it.</summary>
    internal bool CanBegin => !_settled && elapsed() >= Length;

    /// <summary>Settles without arming. Called as the window closes, however it closes.</summary>
    internal void Cancel() => _settled = true;

    /// <summary>Arms the chosen kind for the chosen length through <paramref name="arm"/>, once. Null
    /// where the minute has not run or the start is already settled, and nothing is armed then.</summary>
    internal FocusArmOutcome? Begin(Func<int, FocusSessionKind, FocusArmOutcome> arm)
    {
        if (!CanBegin) return null;
        _settled = true;
        return arm(minutes, kind);
    }
}
