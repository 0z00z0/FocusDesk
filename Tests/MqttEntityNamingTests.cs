using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FocusDesk.Services;
using Xunit;
using ZeroZero.Mqtt;
using ZeroZero.Mqtt.Discovery;

namespace FocusDesk.Tests;

/// <summary>
/// The naming policy, and the identifiers a rename must never move with it.
/// </summary>
/// <remarks>
/// <para>Home Assistant keys its entity registry on the <c>unique_id</c>, so an installation keeps
/// its entity ids, areas, labels and automations across a display-name change — but only for as long
/// as the two stay unrelated. The identifiers are read out of the composed discovery document and
/// compared against literals written out here, so an identifier derived from a name, or an entity id
/// edited alongside a name, fails rather than shipping.</para>
/// <para>The literals are spelled in full rather than composed from <see cref="MqttEntityCatalog"/>'s
/// constants: a test built from the same constants as the code would follow a changed constant
/// instead of catching it.</para>
/// </remarks>
public class MqttEntityNamingTests
{
    /// <summary>A fixed device id, so the expected identifiers are literals rather than a formula.</summary>
    private const string DeviceId = "focusdesk_office_x1";

    /// <summary>Every <c>unique_id</c> an installation holds, in the order the catalogue declares
    /// them. <b>Frozen.</b> An entry changes only when an entity is added or removed.</summary>
    private static readonly string[] _uniqueIds =
    [
        "focusdesk_office_x1_screen_brightness",
        "focusdesk_office_x1_screen_brightness_restore",
        "focusdesk_office_x1_focus_session",
        "focusdesk_office_x1_focus_session_minutes",
        "focusdesk_office_x1_focus_session_dims_screen",
        "focusdesk_office_x1_focus_session_covers_screen",
        "focusdesk_office_x1_focus_session_blocks_input",
        "focusdesk_office_x1_focus_session_state",
        "focusdesk_office_x1_focus_session_remaining",
    ];

    /// <summary>The identifiers as a receiver reads them: out of a composed document, rather than
    /// re-derived from the table the document was built from.</summary>
    private static List<string> PublishedUniqueIds()
    {
        string json = DiscoveryDocument.Build(
            MqttEntityCatalog.TopicRoot,
            new MqttDeviceIdentity(DeviceId, "homeassistant", "FocusDesk (Office-X1)"),
            new DiscoveryDevice("ZeroZero Software", "FocusDesk", "0.1.0"),
            new DiscoveryOrigin("FocusDesk", "0.1.0"),
            MqttTestBed.Declared().All,
            [],
            []);

        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty("cmps").EnumerateObject()
                           .Select(entry => entry.Value.GetProperty("unique_id").GetString()!)];
    }

    [Fact]
    public void EveryUniqueId_IsTheOneAnInstallationAlreadyHas() =>
        // The guard the renaming rides on. A display name reaching a unique_id, or an entity id
        // edited alongside a name, discards the entity id, the area, the labels and every
        // automation attached to it, with no recovery.
        Assert.Equal(_uniqueIds, PublishedUniqueIds());

    /// <summary>One settings page, one leading word, and the entities that deliberately do not
    /// carry it. Named individually so the test states the policy rather than today's strings.</summary>
    private sealed record GroupWord
    {
        public required string Group { get; init; }

        public required string Word { get; init; }

        public IReadOnlyList<string> Exceptions { get; init; } = [];
    }

    private static readonly GroupWord[] _policy =
    [
        new() { Group = MqttPublishGroups.Screen, Word = "Screen" },

        // Two words, because every entity on the page carries both and the word is the longest form
        // they can all take. The master switch is named exactly that, so it is both the group's word
        // and the prefix the rest sort below.
        new() { Group = MqttPublishGroups.Focus, Word = "Focus session" },
    ];

    [Fact]
    public void EveryEntity_LeadsWithItsGroupWord()
    {
        var byGroup = MqttTestBed.Declared().All.ToLookup(e => e.Group);

        foreach (var policy in _policy)
        {
            var entities = byGroup[policy.Group].ToList();
            Assert.NotEmpty(entities);

            foreach (var entity in entities)
            {
                if (policy.Exceptions.Contains(entity.Name!, StringComparer.Ordinal))
                    continue;

                Assert.StartsWith(policy.Word, entity.Name!, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void EveryGroup_IsCoveredByThePolicy() =>
        // A group added without a leading word would otherwise pass by never being looked at.
        Assert.Equal(
            MqttPublishGroups.Declared.Select(g => g.Key).Order(StringComparer.Ordinal),
            _policy.Select(p => p.Group).Order(StringComparer.Ordinal));

    [Fact]
    public void TheSwitchesName_IsAPrefixOfEveryValueItGoverns()
    {
        // The other half of the naming rule: the master switch sorts above the rows it decides,
        // because a receiver sorts a section by display name.
        var focus = MqttTestBed.Declared().All
            .Where(e => e.Group == MqttPublishGroups.Focus)
            .Select(e => e.Name!)
            .ToList();

        Assert.Contains("Focus session", focus);
        Assert.All(focus, name => Assert.StartsWith("Focus session", name, StringComparison.Ordinal));
    }

    [Fact]
    public void TheConfigurationRows_SortAsOneUninterruptedBlock()
    {
        // All four are Configuration, and the receiver sorts a section by display name. Nothing
        // else in that section leads with the word, so the four stand together.
        var configuration = MqttTestBed.Declared().All
            .Where(e => e.Category == MqttEntityCategory.Config)
            .Select(e => e.Name!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(
            ["Focus session blocks input", "Focus session covers screen", "Focus session dims screen",
             "Focus session minutes"],
            configuration);
    }
}
