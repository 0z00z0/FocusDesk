using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The two values the application and the installer script agree on without being able to read each
/// other. Either one moving breaks an update silently: Setup either installs interactively behind a
/// closed application, or the download never finds an asset by that name and the update never
/// starts.
/// </summary>
public class UnattendedUpdateTests
{
    private static readonly string Installer = RepoFiles.Read(@"installer\FocusDesk.iss");

    /// <summary>The switch names this run to Setup, which restarts the application it killed only
    /// for a run carrying it. Without the match, an update leaves the tray empty until the next
    /// sign-in.</summary>
    [Fact]
    public void TheInstallerReadsTheSwitchTheApplicationPasses()
    {
        // "/UPDATEFROMAPP=1" against the "{param:UPDATEFROMAPP|0}" the script expands.
        string parameter = UnattendedUpdate.StartedByApplicationSwitch.TrimStart('/').Split('=')[0];

        Assert.Contains($"{{param:{parameter}", Installer, StringComparison.Ordinal);
    }

    /// <summary>The asset match is exact and case-sensitive, and the first executable in a release is
    /// never taken instead.</summary>
    [Fact]
    public void TheInstallerIsBuiltUnderTheNameTheUpdaterDownloads()
    {
        string stem = AppUpdates.InstallerAssetName
            .Replace("{version}", "{#AppVersion}", StringComparison.Ordinal)
            .Replace(".exe", "", StringComparison.Ordinal);

        Assert.Contains($"OutputBaseFilename={stem}", Installer, StringComparison.Ordinal);
    }
}
