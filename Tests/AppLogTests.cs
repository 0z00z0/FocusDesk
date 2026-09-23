using System;
using System.IO;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The logging setup is copied from a sibling project whose logger name and log path carried its
/// own product name. A rename that misses one of the two would either write into the wrong
/// application's data folder or leave a stray identity in the log stream — both silent, so pinned
/// here rather than left to be noticed later.
/// </summary>
public class AppLogTests
{
    [Fact]
    public void LoggerNameIsFocusDesk() =>
        Assert.Equal("FocusDesk", AppLog.LoggerName);

    [Fact]
    public void NLogConfigWritesUnderFocusDesksOwnAppDataFolder()
    {
        var config = File.ReadAllText(Path.Combine(RepositoryRoot, "nlog.config"));

        Assert.Contains(@"FocusDesk\Logs\app.log", config, StringComparison.Ordinal);
        Assert.DoesNotContain("ChargeKeeper", config, StringComparison.Ordinal);
    }

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusDesk.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
