using System.Linq;
using System.Text.Json;
using FocusDesk.Services;
using Xunit;
using ZeroZero.Mqtt;
using ZeroZero.Mqtt.Discovery;

namespace FocusDesk.Tests;

/// <summary>
/// The one reading drawn from a declared list of words, and the words themselves.
/// </summary>
/// <remarks>
/// A receiver holds the state against the list announced with it and rejects anything not on it, so
/// the words are part of the published contract in the way a display name is not: changing one
/// breaks every automation and template comparing against it. The literals here are spelled in full
/// rather than composed from the source they guard, so a changed word fails instead of being
/// followed.
/// </remarks>
public class MqttEnumSensorTests
{
    private const string DeviceId = "focusdesk_office_x1";

    [Fact]
    public void TheFocusSessionWords_AreTheOnesAReceiverAlreadyMatchesOn() =>
        Assert.Equal(["Off", "Active", "Ending", "Confirm"], FocusSessionStages.Words);

    [Fact]
    public void TheStateSensor_AnnouncesExactlyItsOwnWords()
    {
        var entry = Component(MqttEntityCatalog.FocusSessionState);

        Assert.Equal("sensor", entry.GetProperty("p").GetString());
        Assert.Equal(MqttEnumSensor.DeviceClass, entry.GetProperty("device_class").GetString());
        Assert.Equal(
            FocusSessionStages.Words,
            entry.GetProperty("options").EnumerateArray().Select(v => v.GetString()!).ToList());
    }

    [Fact]
    public void EveryStage_PublishesAReadingFromTheDeclaredWords()
    {
        var declared = Component(MqttEntityCatalog.FocusSessionState)
            .GetProperty("options").EnumerateArray().Select(v => v.GetString()!).ToList();

        // Every stage the engine can be in, not a sample: a stage added without a word would
        // publish something the receiver rejects.
        foreach (var stage in System.Enum.GetValues<FocusSessionStage>())
        {
            string? state = MqttTestBed.Build(MqttTestBed.Surface(focusStage: stage))
                                       .Find(MqttEntityCatalog.FocusSessionState)!.ReadState();
            Assert.Contains(state, declared);
        }
    }

    [Fact]
    public void TheCancelStagesReadDifferentlyFromEachOther() =>
        // The whole point of the sensor: a cancel attempt is reported while it runs, and the switch
        // stays on throughout. Two stages reading the same word would hide how far one had got.
        Assert.Equal(4, FocusSessionStages.Words.Distinct(System.StringComparer.Ordinal).Count());

    private static JsonElement Component(string entityId)
    {
        string json = DiscoveryDocument.Build(
            MqttEntityCatalog.TopicRoot,
            new MqttDeviceIdentity(DeviceId, "homeassistant", "FocusDesk (Office-X1)"),
            new DiscoveryDevice("ZeroZero Software", "FocusDesk", "0.1.0"),
            new DiscoveryOrigin("FocusDesk", "0.1.0"),
            MqttTestBed.Declared().All,
            [],
            []);

        return JsonDocument.Parse(json).RootElement.GetProperty("cmps").GetProperty(entityId).Clone();
    }
}
