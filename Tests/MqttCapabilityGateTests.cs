using System;
using System.Linq;
using FocusDesk.Services;
using Xunit;
using ZeroZero.Mqtt.Discovery;

namespace FocusDesk.Tests;

/// <summary>
/// What decides whether an entity is announced: the group toggle the user set, and the gate the
/// machine answers.
/// </summary>
public class MqttCapabilityGateTests
{
    private static string[] Published(MqttEntitySet set) =>
        [.. set.Published(null).Select(e => e.EntityId).Order(StringComparer.Ordinal)];

    private static MqttEntitySet WithCapabilities(PublishCapabilities capabilities) =>
        MqttTestBed.Build(MqttTestBed.Surface(), capabilities);

    [Fact]
    public void OnAMachineWithASettableDisplay_EveryEntityIsAnnounced() =>
        Assert.Equal(9, WithCapabilities(PublishCapabilities.Full).Published(null).Count);

    [Fact]
    public void WithNoDisplayThatAcceptsABrightness_BothScreenEntitiesAndTheFocusScreenLeverGo()
    {
        // A slider and a button that reach nothing are worse than no entities at all: the receiver
        // shows a control, the machine ignores it, and nothing says why. The session's screen lever
        // is the same control under another name, so it goes with them. The cover lever stays: a
        // black window goes over a panel whether or not that panel accepts a brightness.
        var published = Published(WithCapabilities(
            PublishCapabilities.Full with { ScreenBrightness = false }));

        Assert.DoesNotContain(MqttEntityCatalog.ScreenBrightness, published);
        Assert.DoesNotContain(MqttEntityCatalog.ScreenBrightnessRestore, published);
        Assert.DoesNotContain(MqttEntityCatalog.FocusSessionDimsScreen, published);
        Assert.Contains(MqttEntityCatalog.FocusSessionCoversScreen, published);
        Assert.Contains(MqttEntityCatalog.FocusSession, published);
    }

    [Fact]
    public void ASwitchedOffGroup_AnswersFalseWithoutTheMachineBeingReadAtAll()
    {
        // There is nothing to publish either way, and reading the display for an entity nobody wants
        // is work for its own sake.
        int reads = 0;
        var set = MqttTestBed.Build(
            MqttTestBed.Surface(),
            capabilityReader: () => { reads++; return PublishCapabilities.Full; });

        var groups = MqttTestBed.Groups((MqttPublishGroups.Screen, false));

        Assert.False(set.Find(MqttEntityCatalog.ScreenBrightness)!.IsPublished(groups));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void SwitchingAGroupOff_WithholdsExactlyItsOwnEntities()
    {
        var set = MqttTestBed.Declared();
        var groups = MqttTestBed.Groups((MqttPublishGroups.Screen, false));

        string[] screenEntities =
        [
            MqttEntityCatalog.ScreenBrightness, MqttEntityCatalog.ScreenBrightnessRestore,
        ];

        Assert.Equal(
            screenEntities.Order(StringComparer.Ordinal),
            set.Withheld(groups).Select(e => e.EntityId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AFreshInstallation_AnnouncesEveryGroup()
    {
        // Nothing has been toggled, so each key takes its own declared default, and both are on.
        Assert.Empty(MqttTestBed.Declared().Withheld(MqttTestBed.Groups()));
    }

    /// <summary>Switching the focus group off leaves the feature with no way to end a session but
    /// its own clock — nothing on the machine offers one. Asserted so the consequence is written
    /// down rather than discovered.</summary>
    [Fact]
    public void SwitchingTheFocusGroupOff_LeavesNoRemoteWayToEndASession()
    {
        var groups = MqttTestBed.Groups((MqttPublishGroups.Focus, false));

        Assert.False(MqttTestBed.Declared().Find(MqttEntityCatalog.FocusSession)!.IsPublished(groups));
    }

    [Fact]
    public void EveryEntity_CarriesOneOfTheDeclaredGroupKeys()
    {
        // A key nothing declares is announced regardless, so a typo would publish an entity no
        // settings row can ever switch off.
        var declared = MqttPublishGroups.Declared.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var entity in MqttTestBed.Declared().All)
            Assert.Contains(entity.Group!, declared);
    }
}
