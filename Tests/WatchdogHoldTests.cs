using System.Text.RegularExpressions;
using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// Exit from the notification-area menu keeps FocusDesk closed. Without the hold marker the
/// watchdog task starts an installed copy again within five minutes, and nothing fails: the
/// application simply comes back after being told to leave.
/// </summary>
public class WatchdogHoldTests
{
    private static readonly string AppCode = RepoFiles.Read("App.xaml.cs");

    private static string Between(string from, string to)
    {
        int start = AppCode.IndexOf(from, StringComparison.Ordinal);
        int end = AppCode.IndexOf(to, start + 1, StringComparison.Ordinal);
        Assert.InRange(start, 0, end);
        return AppCode[start..end];
    }

    [Fact]
    public void TrayExitWritesTheHoldMarkerBeforeAnyTeardown()
    {
        Assert.Matches(@"TrayMenuItem\.Command\(""Exit"",\s*\(\)\s*=>\s*_exit\?\.Invoke\(\)\)",
            RepoFiles.Read(@"UI\TrayIconHost.cs"));
        Assert.Contains("TrayIconHost.Start(Shutdown);", AppCode, StringComparison.Ordinal);

        string shutdown = Between("private void Shutdown()", "Exit();");
        int marker = shutdown.IndexOf("WatchdogTask.WriteHoldMarker();", StringComparison.Ordinal);
        int teardown = shutdown.IndexOf("TrayIconHost.Stop();", StringComparison.Ordinal);
        Assert.InRange(marker, 0, teardown);
    }

    /// <summary>The marker only holds if a probe reads it before taking the single-instance guard,
    /// and if a probe that does start never wipes it.</summary>
    [Fact]
    public void AProbeHonoursTheHoldMarkerAndNeverClearsIt()
    {
        Assert.Equal("--watchdog-relaunch", TaskDefinitions.WatchdogArg);

        string constructor = Between("public App()", "protected override void OnLaunched");
        Assert.Contains("TaskDefinitions.WatchdogArg", constructor, StringComparison.Ordinal);
        int hold = constructor.IndexOf("WatchdogTask.HoldMarkerExists", StringComparison.Ordinal);
        int guard = constructor.IndexOf("SingleInstanceGuard.TryAcquire()", StringComparison.Ordinal);
        Assert.InRange(hold, 0, guard);

        Assert.Single(Regex.Matches(AppCode, @"WatchdogTask\.TryClearHoldMarker\(\)"));
        Assert.Matches(@"if \(_watchdogProbe\)\s+AppLog\.Info\([^;]*\);\s+else\s+WatchdogTask\.TryClearHoldMarker\(\);",
            AppCode);
    }
}
