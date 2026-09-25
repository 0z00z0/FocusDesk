using System;
using System.Collections.Generic;
using System.Linq;
using FocusDesk.Services;
using Xunit;
using ZeroZero.Mqtt;
using ZeroZero.Mqtt.Discovery;

namespace FocusDesk.Tests;

/// <summary>
/// The published surface as a declaration: the nine entity ids, the component each is announced
/// under, and the discovery keys that decide how a receiver draws it.
/// </summary>
/// <remarks>
/// The expectations here are written out rather than derived. An entity id composes half of a
/// <c>unique_id</c>, so changing one silently discards the name, the entity id, the area, the labels
/// and every automation a user attached to that entity — and there is no recovery. This table is
/// what makes that a failing test rather than an upgrade nobody notices.
/// </remarks>
public class MqttEntityCatalogTests
{
    private sealed record Expected(
        string EntityId,
        string Platform,
        string Name,
        string Group,
        MqttEntityCategory Category,
        string? Icon = null,
        string? DeviceClass = null,
        string? Unit = null);

    private static readonly Expected[] _table =
    [
        new(MqttEntityCatalog.ScreenBrightness, "number", "Screen brightness",
            MqttPublishGroups.Screen, MqttEntityCategory.Primary, Icon: "mdi:brightness-6", Unit: "%"),
        new(MqttEntityCatalog.ScreenBrightnessRestore, "button", "Screen brightness restore",
            MqttPublishGroups.Screen, MqttEntityCategory.Primary, Icon: "mdi:brightness-auto"),

        new(MqttEntityCatalog.FocusSession, "switch", "Focus session",
            MqttPublishGroups.Focus, MqttEntityCategory.Primary, Icon: "mdi:meditation"),
        new(MqttEntityCatalog.FocusSessionMinutes, "number", "Focus session minutes",
            MqttPublishGroups.Focus, MqttEntityCategory.Config, Icon: "mdi:timer-outline", Unit: "min"),
        new(MqttEntityCatalog.FocusSessionDimsScreen, "switch", "Focus session dims screen",
            MqttPublishGroups.Focus, MqttEntityCategory.Config, Icon: "mdi:brightness-2"),
        new(MqttEntityCatalog.FocusSessionCoversScreen, "switch", "Focus session covers screen",
            MqttPublishGroups.Focus, MqttEntityCategory.Config, Icon: "mdi:monitor-off"),
        new(MqttEntityCatalog.FocusSessionBlocksInput, "switch", "Focus session blocks input",
            MqttPublishGroups.Focus, MqttEntityCategory.Config, Icon: "mdi:keyboard-off"),
        new(MqttEntityCatalog.FocusSessionState, "sensor", "Focus session state",
            MqttPublishGroups.Focus, MqttEntityCategory.Diagnostic,
            Icon: "mdi:progress-clock", DeviceClass: "enum"),
        new(MqttEntityCatalog.FocusSessionRemaining, "sensor", "Focus session remaining",
            MqttPublishGroups.Focus, MqttEntityCategory.Primary,
            Icon: "mdi:timer-sand", DeviceClass: "duration", Unit: "min"),
    ];

    public static TheoryData<string> EveryEntityId()
    {
        var data = new TheoryData<string>();
        foreach (var row in _table) data.Add(row.EntityId);
        return data;
    }

    private static Expected Row(string entityId) => _table.Single(r => r.EntityId == entityId);

    private static string? UnitOf(MqttEntity entity) => entity switch
    {
        MqttSensor sensor => sensor.Unit,
        MqttNumber number => number.Unit,
        _ => null,
    };

    [Fact]
    public void TheTable_HoldsExactlyTheNineEntitiesTheAppPublishes() =>
        Assert.Equal(
            _table.Select(r => r.EntityId).Order(StringComparer.Ordinal),
            MqttTestBed.Declared().All.Select(e => e.EntityId).Order(StringComparer.Ordinal));

    /// <summary>The lever this build does not have. Absent rather than announced and inert: a
    /// receiver given a switch that changes nothing is worse off than one given no switch.</summary>
    [Fact]
    public void NoEntityIsAnnouncedForTheNetworkLeverThisBuildDoesNotHave() =>
        Assert.DoesNotContain(MqttTestBed.Declared().All,
                              e => e.EntityId == "focus_session_blocks_network");

    [Fact]
    public void TheEntityMix_IsFourSwitchesTwoSensorsTwoNumbersAndAButton()
    {
        var byPlatform = MqttTestBed.Declared().All
            .GroupBy(e => e.Platform)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["switch"] = 4, ["sensor"] = 2, ["number"] = 2, ["button"] = 1,
            },
            byPlatform);
    }

    [Theory]
    [MemberData(nameof(EveryEntityId))]
    public void EachEntity_IsAnnouncedUnderTheComponentAndNameItAlreadyHas(string entityId)
    {
        var entity = MqttTestBed.Declared().Find(entityId);
        Assert.NotNull(entity);

        var row = Row(entityId);
        Assert.Equal((row.Platform, row.Name, row.Group, row.Category),
                     (entity!.Platform, entity.Name, entity.Group, entity.Category));
    }

    [Theory]
    [MemberData(nameof(EveryEntityId))]
    public void EachEntity_CarriesTheIconDeviceClassAndUnitAReceiverDrawsItWith(string entityId)
    {
        var entity = MqttTestBed.Declared().Find(entityId)!;
        var row = Row(entityId);

        Assert.Equal((row.Icon, row.DeviceClass, row.Unit),
                     (entity.Icon, entity.DeviceClass, UnitOf(entity)));
    }

    [Fact]
    public void EveryEntityId_IsDistinct() =>
        // A duplicate would give two entities one unique_id, and a receiver keeps whichever arrived
        // last.
        Assert.Equal(_table.Length,
                     MqttTestBed.Declared().All.Select(e => e.EntityId).Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void EveryEntity_CarriesOneOfTheDeclaredGroupKeys()
    {
        var keys = MqttPublishGroups.Declared.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        Assert.All(MqttTestBed.Declared().All, e => Assert.Contains(e.Group, keys));
    }

    // ── What a command does ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheBrightnessNumber_SetsTheLevelAndNamesItsOwnEntity()
    {
        var actions = new FakeSettingsActions();
        var set = MqttTestBed.Build(MqttTestBed.Surface(), settings: actions);

        MqttTestBed.Run(MqttTestBed.Command(set, MqttEntityCatalog.ScreenBrightness).Accept("40"));

        Assert.Equal(["ScreenBrightness=40"], actions.Calls);
        Assert.Equal([MqttEntityCatalog.ScreenBrightness], actions.Entities);
    }

    [Fact]
    public void TheRestoreButton_PutsTheLevelBackAndNamesItsOwnEntity()
    {
        var actions = new FakeSettingsActions();
        var set = MqttTestBed.Build(MqttTestBed.Surface(), settings: actions);

        MqttTestBed.Run(MqttTestBed.Command(set, MqttEntityCatalog.ScreenBrightnessRestore).Accept("PRESS"));

        Assert.Equal(["RestoreScreenBrightness"], actions.Calls);
        Assert.Equal([MqttEntityCatalog.ScreenBrightnessRestore], actions.Entities);
    }

    [Fact]
    public void TheSessionSwitch_ArmsOnAndAsksToCancelOff()
    {
        // Off is a request, not an ending: it opens the staged wait. The switch is the only surface
        // that ends a session at all.
        var actions = new FakeSettingsActions();
        var set = MqttTestBed.Build(MqttTestBed.Surface(), settings: actions);

        MqttTestBed.Run(MqttTestBed.Command(set, MqttEntityCatalog.FocusSession).Accept("ON"));
        MqttTestBed.Run(MqttTestBed.Command(set, MqttEntityCatalog.FocusSession).Accept("OFF"));

        Assert.Equal(["FocusSession=True", "FocusSession=False"], actions.Calls);
    }

    [Fact]
    public void TheLeverSwitchesAndTheLength_WriteTheDefaultTheNextSessionStartsFrom()
    {
        var actions = new FakeSettingsActions();
        var set = MqttTestBed.Build(MqttTestBed.Surface(), settings: actions);

        MqttTestBed.Run(MqttTestBed.Command(set, MqttEntityCatalog.FocusSessionMinutes).Accept("90"));
        MqttTestBed.Run(MqttTestBed.Command(set, MqttEntityCatalog.FocusSessionDimsScreen).Accept("OFF"));
        MqttTestBed.Run(MqttTestBed.Command(set, MqttEntityCatalog.FocusSessionCoversScreen).Accept("ON"));

        Assert.Equal(
            ["FocusSessionMinutes=90", "FocusDimsScreen=False", "FocusCoversScreen=True"],
            actions.Calls);
    }

    [Fact]
    public void TheSessionLength_IsBoundedByWhatASessionAccepts()
    {
        var number = Assert.IsType<MqttNumber>(
            MqttTestBed.Declared().Find(MqttEntityCatalog.FocusSessionMinutes));

        Assert.Equal((FocusSessionEngine.MinMinutes, (double)FocusSessionEngine.MaxMinutes),
                     (number.Min, number.Max));
    }

    [Fact]
    public void TheBrightnessNumber_IsBoundedByWhatTheParkAccepts()
    {
        var number = Assert.IsType<MqttNumber>(
            MqttTestBed.Declared().Find(MqttEntityCatalog.ScreenBrightness));

        Assert.Equal(((double)ScreenBrightnessPark.Minimum, (double)ScreenBrightnessPark.Maximum),
                     (number.Min, number.Max));
    }

    // ── What a reading says ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSessionSwitch_ReadsOnForEveryStageButOff()
    {
        // It stays on through a cancel attempt: the attempt is reported by the state sensor, and a
        // switch that flicked off at the first request would say the session had ended.
        foreach (var stage in new[]
                 { FocusSessionStage.Active, FocusSessionStage.Ending, FocusSessionStage.Confirm })
        {
            var entity = Assert.IsType<MqttSwitch>(
                MqttTestBed.Build(MqttTestBed.Surface(focusStage: stage))
                           .Find(MqttEntityCatalog.FocusSession));
            Assert.True(entity.Read!());
        }

        var off = Assert.IsType<MqttSwitch>(
            MqttTestBed.Build(MqttTestBed.Surface(focusStage: FocusSessionStage.Off))
                       .Find(MqttEntityCatalog.FocusSession));
        Assert.False(off.Read!());
    }

    [Fact]
    public void EveryReading_IsAbsentRatherThanZeroBeforeTheFirstSnapshot()
    {
        // Null is "not read yet". A zero would publish a brightness of nothing and a session of no
        // minutes as though both had been measured.
        var set = MqttTestBed.Build(surface: null);

        Assert.Null(Assert.IsType<MqttNumber>(set.Find(MqttEntityCatalog.ScreenBrightness)).Read!());
        Assert.Null(Assert.IsType<MqttSwitch>(set.Find(MqttEntityCatalog.FocusSession)).Read!());
        Assert.Null(Assert.IsType<MqttSensor>(set.Find(MqttEntityCatalog.FocusSessionRemaining)).Read!());
    }

    [Fact]
    public void TheCountdown_ReportsTheMinutesTheSurfaceCarries()
    {
        var sensor = Assert.IsType<MqttSensor>(
            MqttTestBed.Build(MqttTestBed.Surface(focusStage: FocusSessionStage.Active, focusRemaining: 17))
                       .Find(MqttEntityCatalog.FocusSessionRemaining));

        Assert.Equal("17", sensor.Read!());
    }
}
