using System.Collections.Generic;
using System.Linq;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The allow-list on the network lever, and the rules a session writes from it.
/// </summary>
/// <remarks>Everything here runs against the list and the rule composition. No firewall rule is
/// created, changed or removed, and no settings document is touched.</remarks>
public class FocusAllowedProgramsTests
{
    private const string Editor = @"C:\Program Files\Example\editor.exe";
    private const string Reader = @"C:\Program Files\Example\reader.exe";

    // ── The two published rule names ────────────────────────────────────────────────────────────

    /// <summary>The names a person looks for in Windows Defender Firewall to get out of a session.
    /// They are quoted on the Focus page, and they are what a removal falls back to, so they are
    /// written out here as literals rather than read from the code they guard.</summary>
    [Fact]
    public void ThePublishedRuleNamesAreTheOnesAPersonIsToldToDelete()
    {
        Assert.Equal("FocusDesk focus session: broker", FocusFirewallRules.BrokerRuleName);
        Assert.Equal("FocusDesk focus session: name resolution", FocusFirewallRules.ResolverRuleName);
        Assert.Equal("FocusDesk focus session: allowed program 1", FocusFirewallRules.AllowedProgramName(1));
        Assert.Equal("FocusDesk focus session", FocusFirewallRules.Group);
    }

    /// <summary>The Focus page names the group and the broker rule exactly as the code writes them.
    /// A line that drifted from the rules would send a person stuck behind a block looking for names
    /// that are not there.</summary>
    [Fact]
    public void TheFocusPageQuotesTheRuleNamesTheCodeWrites()
    {
        string page = RepoFiles.Read(System.IO.Path.Combine("UI", "FocusSettingsPanel.xaml"));

        Assert.Contains($"&quot;{FocusFirewallRules.Group}&quot;", page, System.StringComparison.Ordinal);
        Assert.Contains($"&quot;{FocusFirewallRules.BrokerRuleName}&quot;", page, System.StringComparison.Ordinal);
    }

    /// <summary>Every rule this application creates has to be recognised again later. One left
    /// behind is a program permanently outside every later block.</summary>
    [Fact]
    public void EveryRuleASessionWrites_IsRecognisedAsOneOfItsOwn()
    {
        var rules = FocusFirewallRules.For("198.51.100.7", 8883, "198.51.100.1", [Editor, Reader]);

        Assert.All(rules, r => Assert.True(FocusFirewallRules.IsOwnName(r.Name), r.Name));
        Assert.False(FocusFirewallRules.IsOwnName("Some other program"));
        Assert.False(FocusFirewallRules.IsOwnName(null));
    }

    // ── What a session writes from the list ─────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyList_WritesExactlyWhatASessionAlwaysWrote()
    {
        var rules = FocusFirewallRules.For("198.51.100.7", 8883, "198.51.100.1", []);

        Assert.Equal([FocusFirewallRules.BrokerRuleName, FocusFirewallRules.ResolverRuleName],
                     rules.Select(r => r.Name));
    }

    [Fact]
    public void EachAllowedProgram_BecomesOneMoreOutboundExceptionNamedByItsPath()
    {
        var rules = FocusFirewallRules.For("198.51.100.7", 8883, "198.51.100.1", [Editor, Reader]);

        Assert.Equal(
            [FocusFirewallRules.BrokerRuleName, FocusFirewallRules.ResolverRuleName,
             FocusFirewallRules.AllowedProgramName(1), FocusFirewallRules.AllowedProgramName(2)],
            rules.Select(r => r.Name));

        var program = rules.Single(r => r.Name == FocusFirewallRules.AllowedProgramName(1));
        Assert.Equal(Editor, program.ApplicationPath);
        Assert.Equal(FirewallDirection.Outbound, program.Direction);
        // Every protocol, so a rule cannot carry a port: the firewall refuses one on a rule that has
        // not committed to TCP or UDP.
        Assert.Equal(FocusFirewallRules.ProtocolAny, program.Protocol);
        Assert.Equal("", program.RemotePorts);
    }

    [Fact]
    public void AMachineWhoseBrokerNeedsNoLookup_StillNumbersItsProgramsFromOne()
    {
        var rules = FocusFirewallRules.For("198.51.100.7", 8883, "", [Editor]);

        Assert.Equal([FocusFirewallRules.BrokerRuleName, FocusFirewallRules.AllowedProgramName(1)],
                     rules.Select(r => r.Name));
    }

    [Fact]
    public void AStoredEntryThatNoLongerNamesAProgram_WritesNoRuleAtAll()
    {
        var rules = FocusFirewallRules.For("198.51.100.7", 8883, "",
                                           ["", "not-a-path", @"C:\Program Files\Example\notes.txt", Editor]);

        Assert.Equal([FocusFirewallRules.BrokerRuleName, FocusFirewallRules.AllowedProgramName(1)],
                     rules.Select(r => r.Name));
    }

    // ── Editing the list ────────────────────────────────────────────────────────────────────────

    private const string StoreApp = "Example.Notes_8wekyb3d8bbwe";
    private const string WebApp   = "Brave._crx_abcdefghijklmnopqrstuvwxyz";

    private static FocusProgramEntry File(string path, bool run = true, bool network = true) =>
        new() { Kind = FocusProgramKind.ProgramFile, Id = path, CanRun = run, CanUseNetwork = network };

    private static FocusProgramEntry Package(string family) =>
        new() { Kind = FocusProgramKind.StorePackage, Id = family, CanRun = true, CanUseNetwork = true };

    private static FocusProgramEntry Web(string identity) =>
        new() { Kind = FocusProgramKind.WebApp, Id = identity, CanRun = true, CanUseNetwork = true,
                BrowserPath = @"C:\Program Files\Example\browser.exe" };

    /// <summary>The refusal that matters: the rules were written when the session armed, so a list
    /// that moved under them would describe a firewall state the machine is not in, and a run list
    /// that moved would let a program in part-way through.</summary>
    [Fact]
    public void TheListCannotChangeWhileASessionRuns()
    {
        var list = new List<FocusProgramEntry> { File(Editor) };

        Assert.Equal(FocusAllowVerdict.SessionRunning,
                     FocusAllowedPrograms.Add(list, File(Reader), sessionRunning: true));
        Assert.Equal(FocusAllowVerdict.SessionRunning,
                     FocusAllowedPrograms.Remove(list, FocusProgramKind.ProgramFile, Editor, sessionRunning: true));
        Assert.Equal(FocusAllowVerdict.SessionRunning,
                     FocusAllowedPrograms.SetCanRun(list, FocusProgramKind.ProgramFile, Editor, false, sessionRunning: true));
        Assert.Equal(FocusAllowVerdict.SessionRunning,
                     FocusAllowedPrograms.SetCanUseNetwork(list, FocusProgramKind.ProgramFile, Editor, false, sessionRunning: true));

        Assert.Equal([File(Editor)], list);
    }

    [Fact]
    public void AProgramGoesOnTheListByItsFullPathAndComesOffAgain()
    {
        var list = new List<FocusProgramEntry>();

        Assert.Equal(FocusAllowVerdict.Added, FocusAllowedPrograms.Add(list, File(Editor), false));
        Assert.Equal([File(Editor)], list);

        Assert.Equal(FocusAllowVerdict.Removed,
                     FocusAllowedPrograms.Remove(list, FocusProgramKind.ProgramFile, Editor, false));
        Assert.Empty(list);
    }

    [Fact]
    public void TheSameProgramSpeltTwoWays_IsOneEntry()
    {
        var list = new List<FocusProgramEntry> { File(Editor) };

        Assert.Equal(FocusAllowVerdict.AlreadyAllowed,
                     FocusAllowedPrograms.Add(list, File(Editor.ToUpperInvariant()), false));
        Assert.Equal(FocusAllowVerdict.AlreadyAllowed,
                     FocusAllowedPrograms.Add(list, File($"\"{Editor}\" "), false));
        Assert.Single(list);
    }

    [Fact]
    public void SomethingThatIsNotAProgram_NeverReachesTheList()
    {
        var list = new List<FocusProgramEntry>();

        Assert.Equal(FocusAllowVerdict.NotAProgram, FocusAllowedPrograms.Add(list, File(""), false));
        Assert.Equal(FocusAllowVerdict.NotAProgram, FocusAllowedPrograms.Add(list, File("editor.exe"), false));
        Assert.Equal(FocusAllowVerdict.NotAProgram,
                     FocusAllowedPrograms.Add(list, File(@"C:\Program Files\Example\notes.txt"), false));
        Assert.Equal(FocusAllowVerdict.NotAProgram, FocusAllowedPrograms.Add(list, Package(Editor), false));
        Assert.Empty(list);
    }

    /// <summary>A Store app or a web app never carries a network choice: no rule this application
    /// writes could honour one, so a ticked box would promise a network the session then blocks.</summary>
    [Fact]
    public void OnlyAProgramFileKeepsItsNetworkChoice()
    {
        var list = new List<FocusProgramEntry>();

        Assert.Equal(FocusAllowVerdict.Added, FocusAllowedPrograms.Add(list, Package(StoreApp), false));
        Assert.Equal(FocusAllowVerdict.Added, FocusAllowedPrograms.Add(list, Web(WebApp), false));
        Assert.All(list, e => Assert.False(e.CanUseNetwork));

        Assert.Equal(FocusAllowVerdict.NotOffered,
                     FocusAllowedPrograms.SetCanUseNetwork(list, FocusProgramKind.StorePackage, StoreApp, true, false));
        Assert.All(list, e => Assert.False(e.CanUseNetwork));
    }

    [Fact]
    public void TakingOffAProgramThatIsNotOnTheList_ChangesNothing()
    {
        var list = new List<FocusProgramEntry> { File(Editor) };

        Assert.Equal(FocusAllowVerdict.NotAllowed,
                     FocusAllowedPrograms.Remove(list, FocusProgramKind.ProgramFile, Reader, false));
        Assert.Equal([File(Editor)], list);
    }

    // ── The two lists the one list is read as ───────────────────────────────────────────────────

    /// <summary>The firewall is fed from program-file rows with the network ticked, and from nothing
    /// else: a rule by path naming a package family or an identity would be written and match
    /// nothing, and a row with only "can run" must not open the network.</summary>
    [Fact]
    public void NetworkPathsComeOnlyFromProgramFileRowsWithTheNetworkTicked()
    {
        FocusProgramEntry[] entries =
        [
            File(Editor, run: true, network: true),
            File(Reader, run: true, network: false),
            Package(StoreApp) with { CanUseNetwork = true },
            Web(WebApp),
        ];

        Assert.Equal([Editor], FocusAllowedPrograms.NetworkPaths(entries));
    }

    /// <summary>An installation upgrading must keep every program's network exactly as it had it: a
    /// program that loses its rule is cut off in the middle of a session with nothing said.</summary>
    [Fact]
    public void AMigratedList_WritesTheSameRulesAsThePathsItCameFrom()
    {
        string[] old = [Editor, Reader];

        var migrated = FocusAllowedPrograms.Migrate(old);

        Assert.All(migrated, e =>
        {
            Assert.Equal(FocusProgramKind.ProgramFile, e.Kind);
            Assert.True(e.CanUseNetwork);
            Assert.False(e.CanRun);
            Assert.Equal(FocusProgramAction.Minimise, e.WhenNotAllowed);
        });
        Assert.Equal(
            FocusFirewallRules.For("198.51.100.7", 8883, "198.51.100.1", old),
            FocusFirewallRules.For("198.51.100.7", 8883, "198.51.100.1",
                                   FocusAllowedPrograms.NetworkPaths(migrated)));
    }

    [Fact]
    public void TheRowShowsTheProgramsOwnNameWhileTheStoredValueStaysThePath() =>
        Assert.Equal("editor", FocusAllowedPrograms.DisplayName(File(Editor)));
}
