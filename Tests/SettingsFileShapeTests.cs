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
        "Focus",
        "Focus.FocusSessionMinutes",
        "Focus.FocusBlocksNetwork",
        "Focus.FocusLimitsPrograms",
        "Focus.FocusPrograms",
        "Focus.FocusProgramsDefaultAction",
        "Focus.FocusDimsScreen",
        "Focus.FocusCoversScreen",
        "Focus.FocusBlocksInput",
        "Focus.FocusStartFromDashboard",
        "Focus.FocusSessionStartedAt",
        "Focus.FocusSessionEndsAt",
        "Focus.FocusSessionBlockedNetwork",
        "Focus.FocusSessionDimmedScreen",
        "Focus.FocusSessionCoveredScreen",
        "Focus.FocusSessionBlockedInput",
        "Focus.FocusSessionLimitedPrograms",
        "Focus.FocusSavedFirewall",
        "Screen",
        "Screen.ScreenSavedBrightness",
        "Appearance",
        "Appearance.PromoteTrayIcon",
        // State rather than a setting: what the shell held before the row above was first switched
        // on. It trails the row for that reason.
        "Appearance.TrayIconPromotionRestoreFor",
        "Appearance.TrayIconPromotionRestoreValue",
        // Not a page: where the Settings window was last left. It trails the pages for that reason.
        "Window",
        "Window.SettingsWindowX",
        "Window.SettingsWindowY",
        "Window.SettingsWindowWidth",
        "Window.SettingsWindowHeight",
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
            // Read from an earlier document to migrate and never written, so no setting carries it.
            .Where(n => !SettingsFile.ReadOnlyKeys.Contains(n, StringComparer.Ordinal))
            .ToList();

        Assert.Empty(grouped.GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));

        string missing = string.Join(", ", persisted.Except(grouped, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        string extra   = string.Join(", ", grouped.Except(persisted, StringComparer.Ordinal).Order(StringComparer.Ordinal));

        Assert.Equal("", $"not in any group: [{missing}]  in a group but not persisted: [{extra}]"
                             .Replace("not in any group: []  in a group but not persisted: []", "", StringComparison.Ordinal));
    }

    /// <summary>The firewall state a block displaced survives the document, profile by profile and by
    /// name. A record lost or misread here is a firewall nothing puts back.</summary>
    [Fact]
    public void TheSavedFirewallStateSurvivesARoundTrip()
    {
        Directory.CreateDirectory(_dir);
        FirewallProfileSetting[] saved =
        [
            new(FirewallProfile.Domain, BlockOutbound: false, BlockAllInbound: true),
            new(FirewallProfile.Private, BlockOutbound: false, BlockAllInbound: false),
            new(FirewallProfile.Public, BlockOutbound: true, BlockAllInbound: false),
        ];
        Assert.True(SettingsService.WriteTo(new AppSettings { FocusSavedFirewall = [.. saved] }, File_));

        Assert.Contains("\"Public\"", System.IO.File.ReadAllText(File_), StringComparison.Ordinal);
        Assert.Equal(saved, SettingsService.ReadFrom(File_)!.FocusSavedFirewall);
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
            FocusBlocksNetwork        = true,
            FocusPrograms             =
            [
                new() { Kind = FocusProgramKind.ProgramFile, Id = @"C:\Program Files\Example\editor.exe",
                        StartEntry = @"C:\ProgramData\Example\Editor.lnk", CanRun = true, CanUseNetwork = true },
                new() { Kind = FocusProgramKind.StorePackage, Id = "Example.Notes_8wekyb3d8bbwe",
                        StartEntry = "Example.Notes_8wekyb3d8bbwe!App", CanRun = true,
                        WhenNotAllowed = FocusProgramAction.AskToClose },
                new() { Kind = FocusProgramKind.WebApp, Id = "Brave._crx_abcdefghijklmnopqrstuvwxyz",
                        BrowserPath = @"C:\Program Files\Example\browser.exe",
                        WhenNotAllowed = FocusProgramAction.ForceClose },
            ],
            FocusProgramsDefaultAction = FocusProgramAction.AskToClose,
            FocusDimsScreen           = false,
            FocusBlocksInput          = true,
            FocusSessionEndsAt        = new DateTimeOffset(2026, 9, 20, 13, 0, 0, TimeSpan.Zero),
            FocusSessionCoveredScreen = true,
            FocusSessionBlockedInput  = true,
            FocusSessionBlockedNetwork = true,
            FocusLimitsPrograms       = true,
            FocusSessionLimitedPrograms = true,
        };
        Assert.True(SettingsService.WriteTo(before, File_));

        var after = SettingsService.ReadFrom(File_);

        Assert.NotNull(after);
        Assert.Equal(Describe(before), Describe(after!));
    }

    /// <summary>A document from before the one program list carries a bare path list. Each path was
    /// chosen to keep the network, so it keeps it; whether it may run is a choice nobody made, so it
    /// may not. The store never deletes a key, so the earlier list stays in the file; it must never
    /// migrate a second time, or a program taken off the list comes back at the next start.</summary>
    [Fact]
    public void AnEarlierPathListMigratesOnce_ToNetworkOnRunOffAndMinimise()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File_,
            "{ \"ConfigVersion\": 1, \"Focus\": { \"FocusAllowedPrograms\": "
          + "[ \"C:\\\\Program Files\\\\Example\\\\editor.exe\" ] } }");

        var loaded = SettingsService.ReadFrom(File_);

        Assert.NotNull(loaded);
        var only = Assert.Single(loaded!.FocusPrograms);
        Assert.Equal(new FocusProgramEntry
        {
            Kind = FocusProgramKind.ProgramFile, Id = @"C:\Program Files\Example\editor.exe",
            CanRun = false, CanUseNetwork = true, WhenNotAllowed = FocusProgramAction.Minimise,
        }, only);

        Assert.True(SettingsService.WriteTo(loaded, File_));
        Assert.Contains("\"ProgramFile\"", System.IO.File.ReadAllText(File_), StringComparison.Ordinal);
        Assert.Single(SettingsService.ReadFrom(File_)!.FocusPrograms);

        loaded.FocusPrograms.Clear();
        Assert.True(SettingsService.WriteTo(loaded, File_));
        Assert.Empty(SettingsService.ReadFrom(File_)!.FocusPrograms);
    }

    private static string Describe(AppSettings s) => string.Join('|',
        s.ScreenSavedBrightness, s.FocusSessionMinutes, s.FocusBlocksNetwork,
        string.Join(';', s.FocusPrograms), s.FocusProgramsDefaultAction, s.FocusSessionBlockedNetwork,
        s.FocusDimsScreen, s.FocusCoversScreen, s.FocusLimitsPrograms, s.FocusSessionLimitedPrograms,
        s.FocusBlocksInput, s.FocusStartFromDashboard, s.FocusSessionStartedAt, s.FocusSessionEndsAt,
        s.FocusSessionDimmedScreen, s.FocusSessionCoveredScreen, s.FocusSessionBlockedInput);

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
