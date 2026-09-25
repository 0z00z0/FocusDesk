using System;
using System.Collections.Generic;
using System.Threading;
using FocusDesk.Services;
using ZeroZero.Mqtt;
using ZeroZero.Mqtt.Discovery;

namespace FocusDesk.Tests;

/// <summary>An <see cref="IMqttSettingsStore"/> over a field. The module's whole storage dependency
/// is three members, so a test needs no file and no directory.</summary>
internal sealed class FakeMqttSettingsStore : IMqttSettingsStore
{
    private readonly MqttSettings _settings = new();

    public int Writes { get; private set; }

    public MqttSettings Read() => _settings;

    public void Update(Action<MqttSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        mutate(_settings);
        Writes++;
        Changed?.Invoke();
    }

    public event Action? Changed;
}

/// <summary>Records every settings write a command produces.</summary>
internal sealed class FakeSettingsActions : ISettingsActions
{
    /// <summary>Every call, as "member=value", in order. One list keeps a test's assertion about
    /// which setter ran as short as the assertion about the value.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The entity each recorded command arrived on, in order. It is what the log line
    /// names, so a test can show a command is attributed to the entity that carried it.</summary>
    public List<string> Entities { get; } = [];

    private void Record(string call, string entityId)
    {
        Calls.Add(call);
        Entities.Add(entityId);
    }

    public void SetScreenBrightness(int percent, string entityId) =>
        Record($"ScreenBrightness={percent}", entityId);

    public void RestoreScreenBrightness(string entityId) => Record("RestoreScreenBrightness", entityId);

    public void SetFocusSession(bool on, string entityId) => Record($"FocusSession={on}", entityId);

    public void SetFocusSessionMinutes(int minutes) => Calls.Add($"FocusSessionMinutes={minutes}");

    public void SetFocusBlocksNetwork(bool on) => Calls.Add($"FocusBlocksNetwork={on}");

    public void SetFocusDimsScreen(bool on) => Calls.Add($"FocusDimsScreen={on}");

    public void SetFocusCoversScreen(bool on) => Calls.Add($"FocusCoversScreen={on}");

    public void SetFocusBlocksInput(bool on) => Calls.Add($"FocusBlocksInput={on}");
}

/// <summary>Composes the entity table over fakes, and the snapshot it reads. Every default is a
/// plausible machine, so a test states only the field it is about.</summary>
internal static class MqttTestBed
{
    public static SurfaceState Surface(
        int? screenBrightness = 70,
        FocusSessionStage focusStage = FocusSessionStage.Off, int? focusRemaining = null,
        int focusSessionMinutes = 60, bool focusDimsScreen = true, bool focusCoversScreen = true,
        bool focusBlocksInput = false, bool focusBlocksNetwork = false) =>
        new(screenBrightness, focusStage, focusRemaining, focusSessionMinutes, focusBlocksNetwork,
            focusDimsScreen, focusCoversScreen, focusBlocksInput);

    /// <summary>The sources, with every reader answering the same snapshot every time.</summary>
    public static MqttEntitySources Sources(
        SurfaceState? surface = null, PublishCapabilities? capabilities = null,
        Func<PublishCapabilities>? capabilityReader = null, ISettingsActions? settings = null)
    {
        var caps = capabilities ?? PublishCapabilities.Full;
        return new MqttEntitySources
        {
            Surface = () => surface,
            Capabilities = capabilityReader ?? (() => caps),
            Settings = settings ?? new FakeSettingsActions(),
        };
    }

    /// <inheritdoc cref="MqttEntityCatalog.Build"/>
    public static MqttEntitySet Build(
        SurfaceState? surface = null, PublishCapabilities? capabilities = null,
        Func<PublishCapabilities>? capabilityReader = null, ISettingsActions? settings = null) =>
        MqttEntityCatalog.Build(Sources(surface, capabilities, capabilityReader, settings));

    /// <summary>The whole table with the snapshot present, for a test about declarations only.</summary>
    public static MqttEntitySet Declared() => Build(Surface());

    /// <summary>A group snapshot with every declared group in a given state, for the gating tests.</summary>
    public static PublishGroupSnapshot Groups(params (string Key, bool On)[] states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var store = new FakeMqttSettingsStore();
        var set = new PublishGroupSet(store, MqttPublishGroups.Declared);
        foreach (var (key, on) in states) set.Set(key, on);
        return set.Snapshot();
    }

    /// <summary>Runs an accepted verdict's work to completion, so a test can assert on what it did.</summary>
    public static void Run(MqttCommandVerdict verdict)
    {
        Xunit.Assert.True(verdict.IsAccepted, $"The verdict was {verdict.Outcome}: {verdict.Detail}");
        verdict.Run!(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>The command entity behind an id, for a test about what one payload does.</summary>
    public static MqttCommandEntity Command(MqttEntitySet set, string entityId) =>
        Xunit.Assert.IsAssignableFrom<MqttCommandEntity>(set.Find(entityId));
}
