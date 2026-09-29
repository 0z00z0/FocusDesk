using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>What the cover's countdown draws: how much of the session is left, in whole minutes, in
/// seconds and against the session's own length, with the colour that reads as.</summary>
/// <param name="SecondsLeft">Seconds until the session ends, never negative. Fractional, so the ring
/// moves smoothly between the ticks that drive it.</param>
/// <param name="TotalSeconds">The session's whole length. Zero for a record written before the start
/// time was kept, which draws a whole ring rather than dividing by nothing.</param>
internal readonly record struct FocusCoverReading(
    int MinutesLeft, double SecondsLeft, double TotalSeconds, uint Argb)
{
    /// <summary>The last stretch of a session, where the dial rereads itself a second at a
    /// time.</summary>
    internal const double FinalStretchSeconds = 60;

    /// <summary>1 at the moment the session is armed, falling to 0 at its end time.</summary>
    internal double FractionLeft =>
        TotalSeconds > 0 ? Math.Clamp(SecondsLeft / TotalSeconds, 0, 1) : 1;

    /// <summary>Whether the session is inside its final minute. A session no longer than that minute
    /// is never in it: the dial has nothing finer to rescale to.</summary>
    internal bool IsFinalStretch =>
        TotalSeconds > FinalStretchSeconds && SecondsLeft <= FinalStretchSeconds;

    /// <summary>What the tick dial reads. The whole session ordinarily; inside the final stretch the
    /// dial rescales, so one tick is one second and the instrument reads the last minute at the
    /// resolution that minute deserves.</summary>
    internal double DialFraction => IsFinalStretch
        ? Math.Clamp(SecondsLeft / FinalStretchSeconds, 0, 1)
        : FractionLeft;
}

/// <summary>
/// The countdown as a value. Separate from the window that draws it so the reading is exercised
/// without a display: the cover itself is never shown under test.
/// </summary>
/// <remarks><para>The ring empties rather than fills — it is whole at the start of a session and
/// gone at its end — and takes its colour from a draining scale, so a session running down reads in
/// one colour language rather than a second one.</para>
/// <para>Two surfaces draw this same reading: the screen cover and the tray pop-out. One value, so
/// the two instruments cannot disagree about how much is left.</para></remarks>
internal static class FocusCoverCountdown
{
    /// <summary>The reading for a running session, or null when none is.</summary>
    internal static FocusCoverReading? For(FocusSnapshot session, DateTimeOffset now)
    {
        if (!session.IsRunning || session.EndsAt is not { } ends) return null;

        var started = session.StartedAt ?? ends;
        double total = Math.Max(0, (ends - started).TotalSeconds);
        double left  = Math.Max(0, (ends - now).TotalSeconds);

        // A session with no recorded length — a record written before the start time was kept —
        // draws a whole ring rather than dividing by zero.
        double fraction = total > 0 ? Math.Clamp(left / total, 0, 1) : 1;

        return new FocusCoverReading(
            session.MinutesLeft(now) ?? 0,
            left,
            total,
            CountdownPalette.Sample(CountdownPalette.Draining, (int)Math.Round(fraction * 100)));
    }
}
