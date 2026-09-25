using System.Security.Cryptography;
using System.Text;
using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The notification-area icon's identity. The shell keys the icon's position, and whether it was
/// pulled out of the overflow flyout, on this value; a changed value loses both, on every machine
/// FocusDesk is already installed on, with no way back.
/// </summary>
public class TrayIconIdentityTests
{
    /// <summary>A second, independent copy of the value. Held apart from the constant on purpose:
    /// the point is that an edit to the constant fails here.</summary>
    private const string Pinned = "ADAB45AC-EC29-6860-C480-EDA4F569A006";

    [Fact]
    public void TheIconsIdentityNeverMoves() =>
        Assert.Equal(Guid.Parse(Pinned), TrayIconIdentity.Value);

    /// <summary>The value the shared tray host would derive from the icon's name on its own — the
    /// first sixteen bytes of SHA-256 over the name in UTF-8. Stating the identity rather than
    /// letting it be derived only costs nothing while the two agree, so the agreement is held.</summary>
    [Fact]
    public void StatingTheIdentityMovesNoInstalledIcon()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(AppInfo.Name));
        Assert.Equal(new Guid(hash.AsSpan(0, 16)), TrayIconIdentity.Value);
    }

    /// <summary>An identity declared and not passed leaves the host deriving one, which is the
    /// silent version of the failure this whole file exists to stop.</summary>
    [Fact]
    public void TheTrayHostIsGivenTheIdentity() =>
        Assert.Matches(@"Id\s*=\s*TrayIconIdentity\.Value", RepoFiles.Read(@"UI\TrayIconHost.cs"));
}
