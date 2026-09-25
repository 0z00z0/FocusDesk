using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>A firewall held in memory: three profiles and a list of rule names.</summary>
internal sealed class FakeFirewallPolicy : IFirewallPolicy
{
    private readonly Dictionary<FirewallProfile, FirewallProfileSetting> _state;

    public FakeFirewallPolicy(params FirewallProfileSetting[] state) =>
        _state = state.ToDictionary(s => s.Profile);

    public bool ReadFails { get; set; }

    /// <summary>Refuses every write naming this profile, so a block can fail part-way.</summary>
    public FirewallProfile? RefuseWritesTo { get; set; }

    public List<string> Rules { get; } = [];

    /// <summary>Every rule added, so a test can read back what a rule was scoped to.</summary>
    public List<FirewallAllowRule> Added { get; } = [];

    public IReadOnlyList<FirewallProfileSetting> Now() =>
        [.. Enum.GetValues<FirewallProfile>().Select(p => _state[p])];

    public IReadOnlyList<FirewallProfileSetting>? ReadAll() => ReadFails ? null : Now();

    public bool Write(FirewallProfileSetting setting)
    {
        if (setting.Profile == RefuseWritesTo) return false;
        _state[setting.Profile] = setting;
        return true;
    }

    public bool AddAllowRule(FirewallAllowRule rule)
    {
        Rules.Add(rule.Name);
        Added.Add(rule);
        return true;
    }

    /// <summary>Removes by the same reading the live policy uses — whether the name belongs to this
    /// feature — rather than by a fixed list, which cannot reach an allowed program's rule.</summary>
    public bool RemoveOwnRules()
    {
        Rules.RemoveAll(FocusFirewallRules.IsOwnName);
        return true;
    }
}

internal sealed class FakeFirewallRecord : IFirewallBlockRecord
{
    public IReadOnlyList<FirewallProfileSetting>? Held { get; set; }

    public IReadOnlyList<FirewallProfileSetting>? Read() => Held;

    public bool Save(IReadOnlyList<FirewallProfileSetting> settings)
    {
        Held = [.. settings];
        return true;
    }

    public void Clear() => Held = null;
}

internal sealed class FakeNetworkTargets : IFocusNetworkTargets
{
    public string? Host { get; set; } = "broker.example.invalid";

    public int? Port { get; set; } = 8883;

    public string Addresses { get; set; } = "198.51.100.7";

    public string ResolverAddresses { get; set; } = "198.51.100.1";

    public string? BrokerHost() => Host;

    public int? BrokerPort() => Port;

    public string Resolve(string host) => Addresses;

    public string Resolvers() => ResolverAddresses;
}

/// <summary>
/// The network block: the firewall comes back exactly as it was found, no rule outlives the block
/// that wrote it, and the broker stays reachable while the block holds.
/// </summary>
/// <remarks>Everything here runs against fakes. No firewall rule is created, changed or removed on
/// the machine running the tests.</remarks>
public class FirewallBlockTests
{
    /// <summary>Three profiles that disagree with each other, so a restore that writes one blanket
    /// value passes nothing.</summary>
    private static FakeFirewallPolicy Firewall() => new(
        new FirewallProfileSetting(FirewallProfile.Domain, BlockOutbound: false, BlockAllInbound: true),
        new FirewallProfileSetting(FirewallProfile.Private, BlockOutbound: false, BlockAllInbound: false),
        new FirewallProfileSetting(FirewallProfile.Public, BlockOutbound: true, BlockAllInbound: false));

    private static IReadOnlyList<FirewallAllowRule> Exceptions() =>
        FocusFirewallRules.For("198.51.100.7", 8883, "198.51.100.1");

    private static FirewallBlockPark Park(FakeFirewallPolicy policy, FakeFirewallRecord record) =>
        new(policy, record, (_, _) => { });

    // ── The firewall park ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFirewallStateRecordedBeforeTheBlock_IsExactlyWhatIsPutBack()
    {
        var policy = Firewall();
        var before = policy.Now();
        var record = new FakeFirewallRecord();
        var park = Park(policy, record);

        Assert.True(park.Engage(Exceptions(), "a test"));
        Assert.All(policy.Now(), p => Assert.True(p.BlockOutbound && p.BlockAllInbound));
        Assert.Equal(before, record.Held);

        Assert.True(park.Lift("a test"));

        Assert.Equal(before, policy.Now());
        Assert.Null(record.Held);
        Assert.Empty(policy.Rules);
    }

    [Fact]
    public void TheRecordIsNeverRecapturedWhileTheBlockStands()
    {
        // A second engage that read the blocked state back into the record would make the block its
        // own "before", and the firewall would never come off. A session resuming after a crash
        // engages exactly like this.
        var policy = Firewall();
        var before = policy.Now();
        var record = new FakeFirewallRecord();
        var park = Park(policy, record);

        park.Engage(Exceptions(), "a test");
        park.Engage(Exceptions(), "a test");

        Assert.Equal(before, record.Held);
        park.Lift("a test");
        Assert.Equal(before, policy.Now());
    }

    [Fact]
    public void TheExceptionsGoInBeforeTheBlock_AndGoAgainWithIt()
    {
        var policy = Firewall();
        var park = Park(policy, new FakeFirewallRecord());

        park.Engage(Exceptions(), "a test");
        Assert.Equal([FocusFirewallRules.BrokerRuleName, FocusFirewallRules.ResolverRuleName],
                     policy.Rules);

        park.Lift("a test");
        Assert.Empty(policy.Rules);
    }

    /// <summary>An allowed program's rule has to go with the block that wrote it. One left standing
    /// is a program permanently outside every later block.</summary>
    [Fact]
    public void AnAllowedProgramsRule_GoesInWithTheBlockAndGoesAgainWithIt()
    {
        var policy = Firewall();
        var park = Park(policy, new FakeFirewallRecord());
        var exceptions = FocusFirewallRules.For(
            "198.51.100.7", 8883, "198.51.100.1", [@"C:\Program Files\Example\editor.exe"]);

        park.Engage(exceptions, "a test");

        Assert.Contains(FocusFirewallRules.AllowedProgramName(1), policy.Rules);
        Assert.Equal(@"C:\Program Files\Example\editor.exe",
                     policy.Added.Single(r => r.ApplicationPath.Length > 0).ApplicationPath);

        park.Lift("a test");
        Assert.Empty(policy.Rules);
    }

    /// <summary>A rule an earlier session wrote for a program since taken off the list still has to
    /// go. The removal reads the rules that are there, never the list as it stands now.</summary>
    [Fact]
    public void ARuleFromASessionWithALongerList_IsStillRemoved()
    {
        var policy = Firewall();
        policy.Rules.Add(FocusFirewallRules.AllowedProgramName(4));
        var park = Park(policy, new FakeFirewallRecord());

        park.Engage(FocusFirewallRules.For("198.51.100.7", 8883, "", []), "a test");
        park.Lift("a test");

        Assert.Empty(policy.Rules);
    }

    [Fact]
    public void AFirewallThatCannotBeRead_LeavesTheMachineOpenRatherThanBlockingWithNothingToRestore()
    {
        var policy = Firewall();
        var before = policy.Now();
        policy.ReadFails = true;
        var record = new FakeFirewallRecord();
        var park = Park(policy, record);

        Assert.False(park.Engage(Exceptions(), "a test"));

        Assert.Null(record.Held);
        Assert.Equal(before, policy.Now());
        Assert.Empty(policy.Rules);
    }

    /// <summary>A block that fails part-way leaves no rule behind and no profile blocked — and the
    /// record stays until the restore has landed, so a restore that fails is retried at the next
    /// start rather than forgotten.</summary>
    [Fact]
    public void ABlockThatFailsPartWay_IsRolledBackAndKeepsItsRecordUntilTheRestoreLands()
    {
        var policy = Firewall();
        var before = policy.Now();
        var record = new FakeFirewallRecord();
        policy.RefuseWritesTo = FirewallProfile.Public;
        var park = Park(policy, record);

        Assert.False(park.Engage(Exceptions(), "a test"));

        Assert.Empty(policy.Rules);
        // The Public profile refused every write, so its restore did not land either.
        Assert.Equal(before, record.Held);

        policy.RefuseWritesTo = null;
        Assert.True(park.Lift("a test"));
        Assert.Equal(before, policy.Now());
        Assert.Null(record.Held);
    }

    [Fact]
    public void ALiftWithNothingRecorded_StillSweepsAwayARuleARunThatDiedLeftBehind()
    {
        var policy = Firewall();
        policy.Rules.Add(FocusFirewallRules.BrokerRuleName);
        policy.Rules.Add("Some other program");

        Assert.True(Park(policy, new FakeFirewallRecord()).Lift("a test"));

        Assert.Equal(["Some other program"], policy.Rules);
    }

    // ── The rules a session writes ──────────────────────────────────────────────────────────────

    [Fact]
    public void AMachineWhoseBrokerIsNamedByAddress_NeedsNoNameResolutionException()
    {
        var rules = FocusFirewallRules.For("198.51.100.7", 8883, "");

        Assert.Equal([FocusFirewallRules.BrokerRuleName], rules.Select(r => r.Name));
    }

    /// <summary>With the port on Automatic the connection sweeps several ports and transports, and
    /// the broker staying reachable is how a session is ended. The broker's own addresses are left
    /// open on every TCP port rather than on a guess.</summary>
    [Fact]
    public void ABrokerPortOnAutomatic_LeavesTheBrokerOpenOnEveryTcpPort()
    {
        var broker = FocusFirewallRules.For("198.51.100.7", null, "").Single();

        Assert.Equal(FocusFirewallRules.ProtocolTcp, broker.Protocol);
        Assert.Equal("198.51.100.7", broker.RemoteAddresses);
        Assert.Equal("", broker.RemotePorts);

        Assert.Equal("8883", FocusFirewallRules.For("198.51.100.7", 8883, "").Single().RemotePorts);
    }

    [Fact]
    public void AnIPv6ZoneIndexNeverReachesARule() =>
        // A rule's address list has no place for one, and an address the firewall refuses would
        // leave the network open with the lever reported as not held.
        Assert.Equal("fe80::1", LiveFocusNetworkTargets.Spelled(IPAddress.Parse("fe80::1%12")));

    // ── The lever's own refusals ────────────────────────────────────────────────────────────────

    private static FocusNetworkLever Lever(FakeNetworkTargets targets, string? elevationRefusal = null) =>
        new(Park(Firewall(), new FakeFirewallRecord()), targets, () => elevationRefusal,
            (_, _) => { });

    [Fact]
    public void TheNetworkLever_RefusesWithNoBrokerToEndTheSessionFrom() =>
        Assert.NotNull(Lever(new FakeNetworkTargets { Host = "" }).Refusal());

    [Fact]
    public void TheNetworkLever_RefusesWithoutAdministratorRights() =>
        Assert.NotNull(Lever(new FakeNetworkTargets(), elevationRefusal: "no rights").Refusal());

    [Fact]
    public void TheNetworkLever_HasNothingToRefuseWithABrokerOnAutomatic() =>
        Assert.Null(Lever(new FakeNetworkTargets { Port = null }).Refusal());

    [Fact]
    public void ABrokerThatCannotBeLookedUp_LeavesTheNetworkOpen()
    {
        var policy = Firewall();
        var before = policy.Now();
        var lever = new FocusNetworkLever(Park(policy, new FakeFirewallRecord()),
                                          new FakeNetworkTargets { Addresses = "" }, () => null,
                                          (_, _) => { });

        Assert.False(lever.Engage("a test"));
        Assert.Equal(before, policy.Now());
        Assert.Empty(policy.Rules);
    }
}
