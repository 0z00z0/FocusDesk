using System.Text.RegularExpressions;
using FocusDesk.Helpers;
using FocusDesk.Services;
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
        Assert.Matches(@"TrayMenuItem\.Command\(AppText\.Get\(""TrayMenuExit""\),\s*\(\)\s*=>\s*_exit\?\.Invoke\(\)\)",
            RepoFiles.Read(@"UI\TrayIconHost.cs"));
        Assert.Contains("await TrayIconHost.StartAsync(Shutdown);", AppCode, StringComparison.Ordinal);

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

        string constructor = Between("public App()", "protected override async void OnLaunched");
        Assert.Contains("TaskDefinitions.WatchdogArg", constructor, StringComparison.Ordinal);
        int hold = constructor.IndexOf("WatchdogTask.HoldsProbeOff", StringComparison.Ordinal);
        int guard = constructor.IndexOf("SingleInstanceGuard.TryAcquire()", StringComparison.Ordinal);
        Assert.InRange(hold, 0, guard);

        Assert.Single(Regex.Matches(AppCode, @"WatchdogTask\.TryClearHoldMarker\(\)"));
        Assert.Matches(@"if \(_watchdogProbe\)\s+AppLog\.Info\([^;]*\);\s+else\s+WatchdogTask\.TryClearHoldMarker\(\);",
            AppCode);
    }

    /// <summary>A marker never keeps a probe down while a session is recorded as running. Exit is
    /// refused mid-session, but a session Home Assistant arms while an Exit is already under way, or
    /// a marker written by hand, would otherwise leave that session's levers down with nothing to
    /// bring them back.</summary>
    [Fact]
    public void AMarkerNeverHoldsAProbeOffWhileASessionIsRecorded()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var running = new FocusSessionRecord(now.AddMinutes(-5), now.AddMinutes(55), true, true);
        var ended = new FocusSessionRecord(now.AddMinutes(-65), now.AddMinutes(-5), true, true);

        Assert.True(WatchdogTask.RecordsARunningSession(running, now));
        Assert.False(WatchdogTask.RecordsARunningSession(ended, now));
        Assert.False(WatchdogTask.RecordsARunningSession(null, now));

        Assert.Matches(@"HoldsProbeOff =>\s*HoldMarkerExists && !RecordsARunningSession\(RecordedSession\(\), DateTimeOffset\.Now\);",
            RepoFiles.Read(@"Helpers\WatchdogTask.cs"));
    }
}
