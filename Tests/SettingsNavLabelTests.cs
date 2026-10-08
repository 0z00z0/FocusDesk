using System.Text.RegularExpressions;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The Settings navigation pane clips a label longer than its width and shows no ellipsis, so a
/// person sees a half word. The face is monospace, which makes the capacity a fixed character count.
/// </summary>
public class SettingsNavLabelTests
{
    /// <summary>Longest navigation label, in characters, the pane shows whole.</summary>
    private const int NavLabelCap = 20;

    [Fact]
    public void EveryNavigationLabelFitsThePaneInBothLanguages()
    {
        string shellHost = RepoFiles.Read(@"UI\SettingsShellHost.cs");
        var labels = Regex.Matches(shellHost, @"Label = (?:""(?<literal>[^""]+)""|AppText\.Get\(""(?<key>\w+)""\))");
        Assert.NotEmpty(labels);

        foreach (var language in new[] { "en-GB", "nb-NO" })
        {
            var strings = ShippedStrings.For(language);
            foreach (Match m in labels)
            {
                string text = m.Groups["literal"].Success ? m.Groups["literal"].Value : strings(m.Groups["key"].Value);
                Assert.True(text.Length <= NavLabelCap, $"{language}: '{text}' is {text.Length} characters; the pane shows {NavLabelCap}.");
            }
        }
    }
}
