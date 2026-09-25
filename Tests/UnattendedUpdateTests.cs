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

    /// <summary>Setup writes the refusal and the next start reads it. A different name on either side
    /// leaves a refused update reported without its reason.</summary>
    [Fact]
    public void TheInstallerWritesTheRefusalFileTheApplicationReads()
    {
        Assert.Contains($@"\{UnattendedUpdate.RefusalFileName}'", Installer, StringComparison.Ordinal);
    }

    /// <summary>The direction is the whole report, and only a real failed update exercises it:
    /// inverted, every update that landed would announce a failure and every failure would pass
    /// unmentioned.</summary>
    [Fact]
    public void AnOlderRunningVersionReportsAnUpdateThatDidNotComplete()
    {
        Assert.Equal(UpdateVerdict.DidNotComplete,    UnattendedUpdate.VerdictFor("1.43.0", "1.42.1"));
        Assert.Equal(UpdateVerdict.Installed,         UnattendedUpdate.VerdictFor("1.43.0", "1.43.0"));
        Assert.Equal(UpdateVerdict.Installed,         UnattendedUpdate.VerdictFor("1.43.0", "1.44.0"));
        Assert.Equal(UpdateVerdict.NothingHandedOver, UnattendedUpdate.VerdictFor(null, "1.42.1"));
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
