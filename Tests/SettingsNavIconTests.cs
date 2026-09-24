using System.Text.RegularExpressions;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The Settings pane's artwork. An icon URI that resolves to no file raises nothing at all — the
/// pane draws an empty box, which is what the artwork was added to replace — so the names the shell
/// host asks for are held to the files that ship.
/// </summary>
public class SettingsNavIconTests
{
    private static readonly string ShellHost = RepoFiles.Read(@"UI\SettingsShellHost.cs");

    public static TheoryData<string> NavIconNames()
    {
        var names = new TheoryData<string>();
        foreach (Match m in Regex.Matches(ShellHost, @"NavIcon\(""(?<name>[a-z0-9-]+)""\)"))
            names.Add(m.Groups["name"].Value);

        Assert.NotEmpty(names);
        return names;
    }

    [Theory]
    [MemberData(nameof(NavIconNames))]
    public void EveryPaneEntrysArtworkShips(string name) =>
        Assert.True(File.Exists(Path.Combine(RepoFiles.Root, "Assets", "nav", $"{name}.svg")),
            $"Assets\\nav\\{name}.svg is asked for by the Settings shell host and is not in the tree.");

    [Fact]
    public void TheProductMarkShips() =>
        Assert.True(File.Exists(Path.Combine(RepoFiles.Root, "Assets", "mark.svg")));

    /// <summary>The build copies the artwork beside the executable. Without the copy the URIs
    /// resolve to nothing on an installed machine while a development run from the source tree
    /// looks correct.</summary>
    [Fact]
    public void TheArtworkReachesTheBuildOutput()
    {
        string project = RepoFiles.Read("FocusDesk.csproj");
        Assert.Contains(@"Assets\*.svg", project, StringComparison.Ordinal);
        Assert.Contains(@"Assets\nav\*.svg", project, StringComparison.Ordinal);
    }
}
