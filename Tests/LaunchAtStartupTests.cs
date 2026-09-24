using System.Text.RegularExpressions;
using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The logon task's name. The installer registers the task and the menu's switch reads and writes
/// it, and the only thing keeping the two on the same task is the spelling. A mismatch shows as a
/// switch that reports off, turns on without complaint and changes nothing.
/// </summary>
public class LaunchAtStartupTests
{
    [Fact]
    public void TheMenuAndTheInstallerNameTheSameTask()
    {
        string script = RepoFiles.Read(@"installer\FocusDesk.iss");
        Match define = Regex.Match(script, @"#define\s+TaskName\s+""(?<name>[^""]+)""");

        Assert.True(define.Success, "The installer script no longer defines TaskName.");
        Assert.Equal(define.Groups["name"].Value, LaunchAtStartup.TaskName);
    }
}
