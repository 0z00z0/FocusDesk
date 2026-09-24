namespace FocusDesk.Services;

/// <summary>What duration a start request runs for. Its own type because two surfaces ask for a
/// session and only one of them names a duration.</summary>
internal static class FocusStartRequest
{
    /// <summary>The duration to use: the one chosen in the start box where there is one, the stored
    /// default otherwise, and never outside the range a session accepts.</summary>
    internal static int Minutes(int? chosen, int storedDefault) =>
        Math.Clamp(chosen ?? storedDefault,
                   FocusSessionEngine.MinMinutes, FocusSessionEngine.MaxMinutes);
}
