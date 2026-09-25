using FocusDesk.Helpers;
using FocusDesk.Services;
using ZeroZero.Mqtt;
using ZeroZero.Tray.WinUI;

namespace FocusDesk.UI;

/// <summary>
/// What a hover over the notification-area icon says: the product, whether a session runs, the time
/// it has left, the levers it holds and whether the broker that can end it is reachable, one fact to
/// a line and the most important first.
/// </summary>
/// <remarks>The shared host holds the whole to 127 units and drops a line that does not fit together
/// with every line after it, so the order is the priority. The longest case — a cancel asked for,
/// three digits of minutes, every lever and a broker not connected — comes to 126.</remarks>
internal static class TrayTooltipText
{
    /// <param name="broker">What the broker connection is doing; null where no publisher exists.</param>
    public static IEnumerable<TrayTooltipLine> Lines(FocusSnapshot session, MqttConnectionState? broker,
                                                     DateTimeOffset now)
    {
        yield return new TrayTooltipLine(AppInfo.Name);
        yield return new TrayTooltipLine(State(session.Stage));

        if (session.IsRunning)
        {
            yield return new TrayTooltipLine($"{session.MinutesLeft(now) ?? 0} min left");
            yield return new TrayTooltipLine(Levers(session));
        }

        yield return new TrayTooltipLine($"Broker: {Broker(broker)}");
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
