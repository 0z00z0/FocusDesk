using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The on-disk shape: that the key order is the one the Settings window presents, that no persisted
/// setting is left out of a group, and that a document this build must not touch is left alone.
/// </summary>
public class SettingsFileShapeTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"focusdesk-shape-test-{Guid.NewGuid():N}");

    private string File_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The key order the file is written in, spelled out rather than read back from the shape: a
    /// test deriving the sequence from the writer's own source follows a reordering instead of
    /// catching it. Groups run in navigation order; rows run in the order they appear on the page.
    /// </summary>
    private static readonly string[] ExpectedKeyOrder =
    [
        // The store's own document key, written first. This application's Version key is not written
        // by this build; where a document already carries one it survives beside this.
        "ConfigVersion",
        "Screen",
        "Screen.ScreenSavedBrightness",
        "Focus",
        "Focus.FocusSessionMinutes",
        "Focus.FocusDimsScreen",
        "Focus.FocusCoversScreen",
        "Focus.FocusStartFromDashboard",
        "Focus.FocusSessionStartedAt",
        "Focus.FocusSessionEndsAt",
        "Focus.FocusSessionDimmedScreen",
        "Focus.FocusSessionCoveredScreen",
    ];

    /// <summary>Group name then each of its keys, in the order they appear in the written file.</summary>
    private static List<string> KeyOrderOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var order = new List<string>();
        foreach (var group in doc.RootElement.EnumerateObject())
        {
            order.Add(group.Name);
            if (group.Value.ValueKind != JsonValueKind.Object) continue;   // the version key
            foreach (var leaf in group.Value.EnumerateObject())
                order.Add($"{group.Name}.{leaf.Name}");
        }
        return order;
    }

    [Fact]
    public void TheFileIsWrittenInTheOrderTheSettingsWindowPresents()
    {
        Directory.CreateDirectory(_dir);
        Assert.True(SettingsService.WriteTo(new AppSettings(), File_));

        Assert.Equal(ExpectedKeyOrder, KeyOrderOf(System.IO.File.ReadAllText(File_)));
    }

    /// <summary>
    /// The loss guard. Reflects over <c>AppSettings</c> — a different source from the shape — so a
    /// persisted setting that never reached a group is named here rather than vanishing from the
    /// file. Sets, not counts: a count matches for the wrong reason.
    /// </summary>
    [Fact]
    public void EveryPersistedSettingLandsInExactlyOneGroup()
    {
        var persisted = typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var grouped = typeof(SettingsFile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(g => g.PropertyType.IsNested)          // skips the version key, which groups nothing
            .SelectMany(g => g.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(grouped.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));

        string missing = string.Join(", ", persisted.Except(grouped, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        string extra   = string.Join(", ", grouped.Except(persisted, StringComparer.Ordinal).Order(StringComparer.Ordinal));

        Assert.Equal("", $"not in any group: [{missing}]  in a group but not persisted: [{extra}]"
                             .Replace("not in any group: []  in a group but not persisted: []", "", StringComparison.Ordinal));
    }

    /// <summary>The two excluded levers have no key at all. They are absent rather than stored off:
    /// a key written now would be read by the build that reintroduces them as a deliberate
    /// choice.</summary>
    [Theory]
    [InlineData("FocusBlocksNetwork")]
    [InlineData("FocusBlocksInput")]
    [InlineData("FocusSessionBlockedNetwork")]
    [InlineData("FocusSessionBlockedInput")]
    [InlineData("FocusSavedFirewall")]
    [InlineData("FocusAllowedPrograms")]
    public void TheDocumentCarriesNoKeyForALeverThisBuildDoesNotHave(string key)
    {
        Directory.CreateDirectory(_dir);
        Assert.True(SettingsService.WriteTo(new AppSettings(), File_));

        Assert.DoesNotContain($"\"{key}\"", System.IO.File.ReadAllText(File_), StringComparison.Ordinal);
    }

    /// <summary>A round trip must not quietly lower a value. The defaults are what an installation
    /// that has opened nothing carries, so they are the reading that matters most.</summary>
    [Fact]
    public void TheDefaultsSurviveARoundTrip()
    {
        Directory.CreateDirectory(_dir);
        var before = new AppSettings
        {
            ScreenSavedBrightness     = 42,
            FocusSessionMinutes       = 90,
            FocusDimsScreen           = false,
            FocusSessionEndsAt        = new DateTimeOffset(2026, 9, 20, 13, 0, 0, TimeSpan.Zero),
            FocusSessionCoveredScreen = true,
        };
        Assert.True(SettingsService.WriteTo(before, File_));

        var after = SettingsService.ReadFrom(File_);

        Assert.NotNull(after);
        Assert.Equal(Describe(before), Describe(after!));
    }

    private static string Describe(AppSettings s) => string.Join('|',
        s.ScreenSavedBrightness, s.FocusSessionMinutes, s.FocusDimsScreen, s.FocusCoversScreen,
        s.FocusStartFromDashboard, s.FocusSessionStartedAt, s.FocusSessionEndsAt,
        s.FocusSessionDimmedScreen, s.FocusSessionCoveredScreen);

    /// <summary>An empty document reads as this application's defaults, not the section types'. The
    /// two differ: a section type declares no session length and no lever, so binding an empty
    /// document as sections would leave a session that could never be armed.</summary>
    [Fact]
    public void AnAbsentFileYieldsNothingAndAnEmptyOneYieldsDefaults()
    {
        Directory.CreateDirectory(_dir);
        Assert.Null(SettingsService.ReadFrom(File_));

        System.IO.File.WriteAllText(File_, "{}");
        var loaded = SettingsService.ReadFrom(File_);

        Assert.NotNull(loaded);
        Assert.Equal(Describe(new AppSettings()), Describe(loaded!));
        Assert.Empty(Directory.GetFiles(_dir, "settings.*.bad.json"));
    }

    /// <summary>Genuinely broken JSON is set aside and yields nothing. Nothing is returned rather
    /// than the section types' own defaults, which are not this application's.</summary>
    [Fact]
    public void ABrokenFileIsSetAsideAndYieldsNothing()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File_, "{ not json");

        Assert.Null(SettingsService.ReadFrom(File_));
        Assert.Single(Directory.GetFiles(_dir, "settings.*.bad.json"));
    }

    /// <summary>A document from a newer build is neither read nor overwritten, on either version key.
    /// Setting it aside as unreadable would let the next save replace it with defaults.</summary>
    [Theory]
    [InlineData(SettingsFile.VersionKey)]
    [InlineData(SettingsStore.StoreVersionKey)]
    public void AFileFromANewerBuildIsLeftUntouched(string versionKey)
    {
        Directory.CreateDirectory(_dir);
        Assert.True(SettingsService.WriteTo(new AppSettings(), File_));

        string newer = System.IO.File.ReadAllText(File_)
            .Replace($"\"{SettingsStore.StoreVersionKey}\": {SettingsFile.CurrentVersion}",
                     $"\"{versionKey}\": {SettingsFile.CurrentVersion + 1}", StringComparison.Ordinal);
        Assert.Contains($"\"{versionKey}\": {SettingsFile.CurrentVersion + 1}", newer, StringComparison.Ordinal);
        System.IO.File.WriteAllText(File_, newer);

        Assert.Null(SettingsService.ReadFrom(File_));
        Assert.False(SettingsService.WriteTo(new AppSettings(), File_));

        Assert.Equal(newer, System.IO.File.ReadAllText(File_));
        Assert.Empty(Directory.GetFiles(_dir, "settings.*.bad.json"));
    }
}
