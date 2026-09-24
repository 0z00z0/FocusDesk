namespace FocusDesk.Services;

/// <summary>
/// The settings and session values behind every published entity. Read on its own signal, so a
/// settings change does not have to wait for anything else.
/// </summary>
/// <remarks>The broker credentials are deliberately absent, and there is no field they could reach:
/// publishing them over the very broker they authenticate to would put them in plain text in the
/// receiver's log and in a retained topic.</remarks>
internal readonly record struct SurfaceState(
    int? ScreenBrightness,
    FocusSessionStage FocusStage,
    int? FocusRemainingMinutes,
    int FocusSessionMinutes,
    bool FocusDimsScreen,
    bool FocusCoversScreen);

/// <summary>What this machine can actually do. Announcing a control the machine cannot honour would
/// leave the receiver with an entity that silently does nothing.</summary>
internal readonly record struct PublishCapabilities(bool ScreenBrightness)
{
    /// <summary>A machine with every gate open — the baseline the tests compare against.</summary>
    public static readonly PublishCapabilities Full = new(true);
}

/// <summary>
/// Gathers the current <see cref="SurfaceState"/> from the settings and the live services. The one
/// impure half of the published surface: the entity table it feeds is pure.
/// </summary>
/// <remarks>Runs on the MQTT threads, so it must not block on the UI.</remarks>
internal static class SurfaceReader
{
    public static SurfaceState Read()
    {
        var focus = FocusSessionService.Current;
        int? brightness = ScreenBrightnessService.Current;
        var now = DateTimeOffset.Now;
        return SettingsService.Read(s => From(s, focus, brightness, now));
    }

    /// <summary>
    /// The projection itself, over supplied state rather than the singletons, so what does and does
    /// not reach an entity is testable.
    /// </summary>
    internal static SurfaceState From(
        AppSettings s, FocusSnapshot focus, int? brightness, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new SurfaceState(
            ScreenBrightness:      brightness,
            FocusStage:            focus.Stage,
            FocusRemainingMinutes: focus.MinutesLeft(now),
            FocusSessionMinutes:   s.FocusSessionMinutes,
            // A running session reports the levers it actually owns; with none running the two read
            // the defaults the next session would start from.
            FocusDimsScreen:       focus.IsRunning ? focus.DimsScreen : s.FocusDimsScreen,
            FocusCoversScreen:     focus.IsRunning ? focus.CoversScreen : s.FocusCoversScreen);
    }
}
