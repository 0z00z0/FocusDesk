using FocusDesk.Helpers;
using FocusDesk.Services;
using ZeroZero.Mqtt;
using ZeroZero.Tray.WinUI;

namespace FocusDesk.UI;

/// <summary>
/// What a hover over the notification-area icon says: the product and version, the one shared
/// session description — the kind, the time left, the levers held and how far a cancel has got — and
/// whether Home Assistant, the only place a running session can be ended early, is reachable. One
/// fact to a line and the most important first.
/// </summary>
/// <remarks>The shared host holds the whole to 127 units and cuts a line that does not fit, so the
/// order is the priority. The longest case — a screen break with a cancel waiting for its second
/// request, three digits of minutes and every lever it can hold — does not fit: the connectivity
/// line, being last, is the one the shared host shortens. Its status word is carried as a protected
/// suffix, so a shortened line loses characters from the "Home Assistant" label first and drops whole
/// only once there is no room left even for the status word. A realistic tooltip (a shorter session,
/// fewer levers) stays under the limit and shows every line whole.</remarks>
internal static class TrayTooltipText
{
    /// <param name="broker">What the Home Assistant connection is doing; null where no publisher
    /// exists.</param>
    public static IEnumerable<TrayTooltipLine> Lines(FocusSnapshot session, MqttConnectionState? broker,
                                                     DateTimeOffset now) =>
        Lines(session, broker, now, AppText.Get);

    /// <summary>The same lines, with the interface text read through <paramref name="text"/>, so a
    /// test can compose them from each shipped resource file.</summary>
    internal static IEnumerable<TrayTooltipLine> Lines(FocusSnapshot session, MqttConnectionState? broker,
                                                       DateTimeOffset now, Func<string, string> text)
    {
        // A tray tooltip is plain text, so a colour emoji is the only way to carry a product icon.
        yield return new TrayTooltipLine($"🎯 {AppInfo.Name}  v{AppInfo.Version}");
        yield return new TrayTooltipLine(FocusSessionStages.Describe(session, now, text));

        // The status word is the suffix, kept whole by the shared host even where the label ahead of
        // it must be shortened: the one fact this line exists for — whether Home Assistant can end
        // the session — must never be shown as a cut fragment.
        yield return new TrayTooltipLine("Home Assistant", $": {Broker(broker)}");
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
