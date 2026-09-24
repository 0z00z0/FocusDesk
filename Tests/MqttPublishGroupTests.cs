using System.Linq;
using FocusDesk.Services;
using Xunit;
using ZeroZero.Mqtt;

namespace FocusDesk.Tests;

/// <summary>
/// The publish groups: their keys, the defaults a fresh installation starts on, and the per-key
/// persistence that keeps a user's choices attached to the group they were made about.
/// </summary>
public class MqttPublishGroupTests
{
    private static PublishGroupSet NewSet() =>
        new(new FakeMqttSettingsStore(), MqttPublishGroups.Declared);

    [Fact]
    public void TwoGroupsAreDeclared_OnePerSettingsPage() =>
        Assert.Equal(["screen", "focus"], MqttPublishGroups.Declared.Select(g => g.Key));

    [Fact]
    public void EveryGroupShipsOn()
    {
        // The published surface is the point of a feature, and a group is switched off to reduce it
        // rather than to opt into it.
        Assert.All(MqttPublishGroups.Declared, g => Assert.True(g.DefaultOn));
    }

    [Fact]
    public void NoGroupNeedsItsDefaultJustifying() =>
        // A description exists to explain a default that would otherwise surprise. Both ship on.
        Assert.All(MqttPublishGroups.Declared, g => Assert.Equal("", g.Description));

    [Fact]
    public void EveryGroup_SaysWhatIsInItBehindItsOwnIcon() =>
        // A row with no info text gets no icon at all, which is better than one opening on nothing.
        Assert.All(MqttPublishGroups.Declared, g => Assert.NotEqual("", g.Info));

    [Fact]
    public void EveryLabel_IsDistinctSoNoTwoRowsReadTheSame() =>
        Assert.Equal(MqttPublishGroups.Declared.Count,
                     MqttPublishGroups.Declared.Select(g => g.Label).Distinct().Count());

    [Fact]
    public void AGroupNobodyHasTouched_TakesItsOwnDeclaredDefault()
    {
        var snapshot = NewSet().Snapshot();

        Assert.True(snapshot.IsEnabled(MqttPublishGroups.Screen));
        Assert.True(snapshot.IsEnabled(MqttPublishGroups.Focus));
    }

    [Fact]
    public void SwitchingAGroup_IsStoredAgainstItsKeyAndLeavesTheOthersAlone()
    {
        var set = NewSet();
        set.Set(MqttPublishGroups.Screen, false);

        var snapshot = set.Snapshot();
        Assert.False(snapshot.IsEnabled(MqttPublishGroups.Screen));
        Assert.True(snapshot.IsEnabled(MqttPublishGroups.Focus));
    }

    [Fact]
    public void SeveralGroupsAtOnce_CostOneWriteRatherThanOnePerGroup()
    {
        var store = new FakeMqttSettingsStore();
        var set = new PublishGroupSet(store, MqttPublishGroups.Declared);

        set.Set([
            new(MqttPublishGroups.Screen, false),
            new(MqttPublishGroups.Focus, false),
        ]);

        Assert.Equal(1, store.Writes);
    }
}
