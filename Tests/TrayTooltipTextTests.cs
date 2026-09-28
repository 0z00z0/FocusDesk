using FocusDesk.Services;
using FocusDesk.UI;
using Xunit;
using ZeroZero.Mqtt;
using ZeroZero.Tray.WinUI;

namespace FocusDesk.Tests;

/// <summary>
/// The hover text. The shell shows 127 units and the shared host cuts whatever does not fit from the
/// end, so a line that grows silently could take the Home Assistant line — the one that says whether
/// the session can be ended at all — down to an unreadable fragment or off the tooltip entirely.
/// </summary>
public class TrayTooltipTextTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] BrokerStatusWords =
        ["connected", "connecting", "not connected", "refused", "off"];

    /// <summary>The longest session there can be, in every running stage and every broker state:
    /// three digits of minutes and every lever held. The whole never exceeds the shell's budget, and
    /// wherever the Home Assistant line still appears its status word is whole, never a cut
    /// fragment — the label ahead of it is what the shared host shortens first, since the status is
    /// carried as the line's protected suffix.</summary>
    [Theory]
    [InlineData("en-GB")]
    [InlineData("nb-NO")]
    public void TheLongestTooltipNeverGarblesHomeAssistantStatus(string language)
    {
        MqttConnectionState?[] brokers = [null, .. Enum.GetValues<MqttConnectionState>().Cast<MqttConnectionState?>()];
        var text = ShippedStrings.For(language);

        foreach (var stage in new[] { FocusSessionStage.Active, FocusSessionStage.Ending, FocusSessionStage.Confirm })
        foreach (var broker in brokers)
        {
            var session = new FocusSnapshot(stage, Noon, Noon.AddMinutes(999), DimsScreen: true,
                                            CoversScreen: true, BlocksInput: true, BlocksNetwork: true);
            var lines = TrayTooltipText.Lines(session, broker, Noon, text).ToList();

            string composed = TrayTooltip.Compose(lines);

            Assert.True(composed.Length <= TrayTooltip.MaxUnits,
                $"tooltip is {composed.Length} units, over the shell's {TrayTooltip.MaxUnits}-unit budget");

            string? homeLine = composed.Split('\n')
                .FirstOrDefault(l => l.StartsWith("Home Assistant", StringComparison.Ordinal));
            if (homeLine is not null)
            {
                Assert.True(BrokerStatusWords.Any(w => homeLine.EndsWith(w, StringComparison.Ordinal)),
                    $"Home Assistant line lost its status word: '{homeLine}'");
            }
        }
    }

    /// <summary>An ordinary screen break — covered with the mouse and keyboard blocked, the switch's
    /// default — shows every line whole in both languages: the product line, the session line led by
    /// its kind, and the Home Assistant status.</summary>
    [Theory]
    [InlineData("en-GB", "Screen break: 25 min left")]
    [InlineData("nb-NO", "Skjermpause: 25 min igjen")]
    public void AnOrdinarySessionKeepsEveryLineWhole(string language, string lead)
    {
        var session = new FocusSnapshot(FocusSessionStage.Active, Noon, Noon.AddMinutes(25),
                                        DimsScreen: false, CoversScreen: true, BlocksInput: true);
        var lines = TrayTooltipText.Lines(session, MqttConnectionState.Connected, Noon,
                                          ShippedStrings.For(language)).ToList();

        string composed = TrayTooltip.Compose(lines);

        Assert.Equal(string.Join('\n', lines.Select(l => (l.Text ?? "") + (l.Suffix ?? ""))), composed);
        Assert.Contains(lead, composed, StringComparison.Ordinal);
        Assert.EndsWith("Home Assistant: connected", composed, StringComparison.Ordinal);
    }
}
