namespace FocusDesk.Services;

/// <summary>
/// Whether FocusDesk may be left on purpose: the tray menu's Exit, and the update flow handing over
/// to an installer. Refused while a focus session runs.
/// </summary>
/// <remarks>Leaving puts back every lever the session holds, and the hold marker it writes keeps the
/// watchdog from starting FocusDesk again. Allowed mid-session, it would end the session from the
/// keyboard with nothing left to bring it back.</remarks>
internal static class DeliberateExit
{
    /// <summary>Any stage but off refuses, a cancel in progress included: the session still holds its
    /// levers until Home Assistant's second request or the clock ends it.</summary>
    internal static bool Allows(FocusSnapshot session) => !session.IsRunning;
}
