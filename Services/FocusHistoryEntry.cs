namespace FocusDesk.Services;

/// <summary>How a focus session ended. The three ways a session can leave the engine, and the words
/// the history file holds.</summary>
/// <remarks>The words are written into a file a person reads, so they are a stored vocabulary rather
/// than a display choice: renaming one makes every earlier row unreadable.</remarks>
internal enum FocusSessionOutcome
{
    /// <summary>The session reached the end time it was armed for.</summary>
    RanToTime,

    /// <summary>Home Assistant's staged cancel was confirmed inside its window.</summary>
    EndedEarly,

    /// <summary>The end time had already passed when the application next started, so nothing was
    /// running to notice it go.</summary>
    FoundStale,
}

/// <summary>One finished session, as the history file holds it.</summary>
/// <param name="DueAt">The end time the session was armed for, which a session ended early never
/// reached.</param>
/// <param name="EndedAt">When the session actually left the engine.</param>
/// <param name="BlockedInput">Whether the session still held the mouse and keyboard when it ended.</param>
internal readonly record struct FocusHistoryEntry(
    DateTimeOffset StartedAt, DateTimeOffset DueAt, DateTimeOffset EndedAt,
    bool DimmedScreen, bool CoveredScreen, FocusSessionOutcome Outcome, bool BlockedInput = false);
