using FocusDesk.Services;
using FocusDesk.UI;
using Xunit;
using ZeroZero.Mqtt;
using ZeroZero.Tray.WinUI;

namespace FocusDesk.Tests;

/// <summary>
/// The hover text. The shell shows 127 units and the shared host drops whatever does not fit from the
/// end, so a line that grows silently takes the broker's line — the one that says whether the session
/// can be ended at all — off the tooltip.
/// </summary>
public class TrayTooltipTextTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The longest session there can be, in every running stage and every broker state:
    /// three digits of minutes and every lever held. Nothing is cut and the broker's line is still
    /// last.</summary>
    [Fact]
    public void TheLongestTooltipKeepsEveryLine()
    {
        MqttConnectionState?[] brokers = [null, .. Enum.GetValues<MqttConnectionState>().Cast<MqttConnectionState?>()];

        foreach (var stage in new[] { FocusSessionStage.Active, FocusSessionStage.Ending, FocusSessionStage.Confirm })
        foreach (var broker in brokers)
        {
            var session = new FocusSnapshot(stage, Noon, Noon.AddMinutes(999), DimsScreen: true,
                                            CoversScreen: true, BlocksInput: true, BlocksNetwork: true);
            var lines = TrayTooltipText.Lines(session, broker, Noon).ToList();

            string composed = TrayTooltip.Compose(lines);

            Assert.Equal(string.Join('\n', lines.Select(l => l.Text)), composed);
            Assert.StartsWith("Broker: ", composed.Split('\n')[^1], StringComparison.Ordinal);
        }
    }
}
