using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>What the network lever needs to keep the broker reachable, behind an interface so the
/// lever can be exercised without a broker, a resolver or a firewall.</summary>
internal interface IFocusNetworkTargets
{
    /// <summary>The configured broker host, or null when publishing is not set up.</summary>
    string? BrokerHost();

    /// <summary>The configured broker port, or null when it is Automatic.</summary>
    int? BrokerPort();

    /// <summary>The host's addresses, comma-separated as Windows Firewall spells a list, or empty
    /// when it cannot be resolved.</summary>
    string Resolve(string host);

    /// <summary>The configured resolver addresses, comma-separated, or empty where none is known.</summary>
    string Resolvers();
}

/// <summary>The network lever: every connection blocked but the broker, the name resolution it
/// depends on, and the programs on the allow-list.</summary>
/// <remarks>Fails safe like the input lever: refused or failed, the session runs on with the network
/// open and the lever reported as not held. The broker staying reachable is how a session is ended,
/// so no block is written without its exception. A block put back by hand in the firewall console is
/// not re-applied while the application runs: that is the published way out of this lever.</remarks>
/// <param name="allowedPrograms">The programs that keep the network, read at the moment a session
/// arms. The list cannot move while one runs, so one reading covers the whole session.</param>
internal sealed class FocusNetworkLever(
    FirewallBlockPark park, IFocusNetworkTargets targets, Func<string?> elevationRefusal,
    Action<string, ActionCause> log, Func<IReadOnlyList<string>>? allowedPrograms = null) : IFocusLever
{
    /// <summary>Why the firewall cannot be written: without administrator rights the policy refuses
    /// every change.</summary>
    internal static string? ElevationRefusal() =>
        Elevation.IsElevated
            ? null
            : "blocking the network needs administrator rights, which this run does not have";

    public string? Refusal()
    {
        if (targets.BrokerHost() is not { Length: > 0 })
            return "no MQTT broker is configured, so nothing would be left to end the session from";
        return elevationRefusal();
    }

    public bool Engage(ActionCause cause)
    {
        if (targets.BrokerHost() is not { Length: > 0 } host) return false;

        // Resolved once, before the block lands: a firewall rule matches an address, not a name, and
        // once the block is on there is no lookup left to make.
        string addresses = targets.Resolve(host);
        if (addresses.Length == 0)
        {
            log("The network is left open: the broker's address could not be looked up, so the "
              + "exception that keeps it reachable could not be written", cause);
            return false;
        }

        return park.Engage(
            FocusFirewallRules.For(addresses, targets.BrokerPort(), targets.Resolvers(),
                                   allowedPrograms?.Invoke()),
            cause);
    }

    /// <summary>Writes the block again. Leaving the application lifts it, so a block with no owner
    /// never outlives the process; after a crash it is still there, and writing it again from the
    /// saved record changes nothing.</summary>
    public bool Resume(ActionCause cause) => Engage(cause);

    public bool Lift(ActionCause cause) => park.Lift(cause);
}

/// <summary>The live broker and resolver addresses the network lever writes its exceptions
/// against.</summary>
/// <param name="broker">The broker host and port the connection is configured with, or null where
/// publishing is not set up. A null port is Automatic.</param>
internal sealed class LiveFocusNetworkTargets(Func<(string? Host, int? Port)?> broker)
    : IFocusNetworkTargets
{
    public string? BrokerHost() => broker()?.Host;

    public int? BrokerPort() => broker()?.Port;

    public string Resolve(string host)
    {
        // An address needs no lookup.
        if (IPAddress.TryParse(host, out var literal)) return Spelled(literal);

        try
        {
            return string.Join(',', Dns.GetHostAddresses(host)
                                       .Where(Routable)
                                       .Select(Spelled)
                                       .Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            AppLog.Error($"LiveFocusNetworkTargets.Resolve({host})", ex);
            return "";
        }
    }

    public string Resolvers()
    {
        try
        {
            return string.Join(',', NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().DnsAddresses)
                .Where(Routable)
                .Select(Spelled)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            AppLog.Error("LiveFocusNetworkTargets.Resolvers", ex);
            return "";
        }
    }

    /// <summary>Whether an address needs a rule at all. Windows Firewall never filters loopback, so a
    /// rule naming it would be dead weight.</summary>
    private static bool Routable(IPAddress address) =>
        !IPAddress.IsLoopback(address)
        && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;

    /// <summary>The address as a firewall rule takes it. An IPv6 zone index (<c>%12</c>) is dropped:
    /// the rule's address list has no place for one.</summary>
    internal static string Spelled(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0
            ? new IPAddress(address.GetAddressBytes()).ToString()
            : address.ToString();
}
