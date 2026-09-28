using FocusDesk.Helpers;
using FocusDesk.Services;
using ZeroZero.Mqtt;
using ZeroZero.Tray.WinUI;

namespace FocusDesk.UI;

/// <summary>
/// What a hover over the notification-area icon says: the product and version, whether a session
/// runs, the time it has left, the levers it holds and whether Home Assistant — the only place a
/// running session can be ended early — is reachable, one fact to a line and the most important
/// first.
/// </summary>
/// <remarks>The shared host holds the whole to 127 units and cuts a line that does not fit, so the
/// order is the priority. The longest case — a cancel asked for, three digits of minutes, every
/// lever and Home Assistant not connected — comes to 145, past the limit: the connectivity line,
/// being last, is the one the shared host shortens. Its status word is carried as a protected
/// suffix, so a shortened line loses characters from the "Home Assistant" label first and drops
/// whole only once there is no room left even for the status word. A realistic tooltip (a shorter
/// session, fewer levers) stays well under the limit and shows every line whole.</remarks>
internal static class TrayTooltipText
{
    /// <param name="broker">What the Home Assistant connection is doing; null where no publisher
    /// exists.</param>
    public static IEnumerable<TrayTooltipLine> Lines(FocusSnapshot session, MqttConnectionState? broker,
                                                     DateTimeOffset now)
    {
        // A tray tooltip is plain text, so a colour emoji is the only way to carry a product icon.
        yield return new TrayTooltipLine($"🎯 {AppInfo.Name}  v{AppInfo.Version}");
        yield return new TrayTooltipLine(State(session.Stage));

        if (session.IsRunning)
        {
            yield return new TrayTooltipLine($"{session.MinutesLeft(now) ?? 0} min left");
            yield return new TrayTooltipLine(Levers(session));
        }

        // The status word is the suffix, kept whole by the shared host even where the label ahead of
        // it must be shortened: the one fact this line exists for — whether Home Assistant can end
        // the session — must never be shown as a cut fragment.
        yield return new TrayTooltipLine("Home Assistant", $": {Broker(broker)}");
    }

    private static string State(FocusSessionStage stage) => stage switch
    {
        FocusSessionStage.Active  => "Focus session running",
        FocusSessionStage.Ending  => "Focus session: cancel asked for",
        FocusSessionStage.Confirm => "Focus session: confirm cancel",
        _                         => "No focus session",
    };

    /// <summary>The levers the session actually holds. One that was asked for and refused, or lost,
    /// is not named: the snapshot reports what is held.</summary>
    private static string Levers(FocusSnapshot session)
    {
        var held = new List<string>(4);
        if (session.DimsScreen)    held.Add("dimming");
        if (session.CoversScreen)  held.Add("cover");
        if (session.BlocksInput)   held.Add("mouse and keyboard");
        if (session.BlocksNetwork) held.Add("network");
        if (session.LimitsPrograms) held.Add(AppText.Get("TrayHeldPrograms"));
        return held.Count > 0 ? $"Held: {string.Join(", ", held)}" : "Held: nothing";
    }

    private static string Broker(MqttConnectionState? state) => state switch
    {
        MqttConnectionState.Connected                                  => "connected",
        MqttConnectionState.Searching or MqttConnectionState.Connecting => "connecting",
        MqttConnectionState.Retrying                                   => "not connected",
        MqttConnectionState.Failed                                     => "refused",
        _                                                              => "off",
    };
}
