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

    /// <summary>A held probe ends in Main before the UI framework starts, which a broken installation
    /// crashes every five minutes; a deliberate start never reads the marker; a probe never wipes it.</summary>
    [Fact]
    public void AHeldProbeEndsBeforeTheUiFrameworkAndNeverClearsTheMarker()
    {
        Assert.Equal("--watchdog-relaunch", TaskDefinitions.WatchdogArg);

        string program = RepoFiles.Read("Program.cs");
        Assert.Matches(@"static void Main\(string\[\] args\)\s*\{(\s*//[^\n]*)*\s*if \(args\.Contains\(TaskDefinitions\.WatchdogArg\) && WatchdogTask\.HoldsProbeOff\)\s*return;\s*StartTheApplication\(\);\s*\}",
            program);
        Assert.Matches(@"\[MethodImpl\(MethodImplOptions\.NoInlining\)\]\s*private static void StartTheApplication\(\) => XamlGeneratedProgram\.XamlGeneratedMain\(\);",
            program);
        Assert.DoesNotContain("Microsoft.UI", program, StringComparison.Ordinal);
        Assert.Contains("<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>",
            RepoFiles.Read("FocusDesk.csproj"), StringComparison.Ordinal);
        Assert.DoesNotContain("HoldsProbeOff", AppCode, StringComparison.Ordinal);

        Assert.Single(Regex.Matches(AppCode, @"WatchdogTask\.TryClearHoldMarker\(\)"));
        Assert.Matches(@"if \(_watchdogProbe\)\s+AppLog\.Info\([^;]*\);\s+else\s+WatchdogTask\.TryClearHoldMarker\(\);",
            AppCode);
    }

    /// <summary>A start at sign-in can lose the race with the taskbar. It must end without the hold
    /// marker, with the watchdog task already armed, so the probe brings it back; and no window may
    /// open before the icon, or the process ends holding nothing on screen.</summary>
    [Fact]
    public void AFailedStartLeavesTheProbeArmedAndNoWindowPrecedesTheIcon()
    {
        string constructor = Between("public App()", "protected override async void OnLaunched");
        int guard = constructor.IndexOf("SingleInstanceGuard.TryAcquire()", StringComparison.Ordinal);
        int armed = constructor.IndexOf("WatchdogTask.TryEnsureTask();", StringComparison.Ordinal);
        int register = constructor.IndexOf("CrashHandlers.Register(", StringComparison.Ordinal);
        Assert.InRange(guard, 0, armed);
        Assert.InRange(armed, 0, register);
        string options = constructor[register..constructor.IndexOf("});", register, StringComparison.Ordinal)];
        Assert.Contains("OnStartupFailure", options, StringComparison.Ordinal);
        Assert.DoesNotContain("HoldMarker", options, StringComparison.Ordinal);

        string launched = Between("protected override async void OnLaunched", "private void Shutdown()");
        int tray = launched.IndexOf("await TrayIconHost.StartAsync(Shutdown);", StringComparison.Ordinal);
        int failed = launched.IndexOf("_crashHandlers.Report(\"tray\", started.LastError);", StringComparison.Ordinal);
        int window = launched.IndexOf("new MainWindow()", StringComparison.Ordinal);
        int complete = launched.IndexOf("_crashHandlers.MarkStartupComplete();", StringComparison.Ordinal);
        Assert.InRange(tray, 0, failed);
        Assert.InRange(failed, 0, window);
        Assert.InRange(window, 0, complete);
        Assert.Single(Regex.Matches(AppCode, @"MarkStartupComplete\("));
        Assert.DoesNotMatch(@"new \w*Window\(|\.Activate\(", launched[..tray]);
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

        Assert.Matches(@"HoldsProbeOff =>\s*HoldMarkerExists\s*&& !RecordsARunningSession\(RecordedSession\(\), DateTimeOffset\.Now\)\s*&& !MarksAnUnfinishedUpdate\(",
            RepoFiles.Read(@"Helpers\WatchdogTask.cs"));
    }

    /// <summary>Honoured after its update failed, the marker kept the installation down for good.</summary>
    [Fact]
    public void AnUnfinishedUpdatesMarkerStopsHoldingTheProbe()
    {
        var started = new DateTimeOffset(2026, 10, 11, 14, 15, 0, TimeSpan.Zero);
        var handover = new UnattendedUpdate.Handover { TargetVersion = "0.5.2", StartedUtc = started.ToString("O") };
        DateTime exitForUpdate = started.AddSeconds(10).UtcDateTime;

        Assert.True(WatchdogTask.MarksAnUnfinishedUpdate(handover, exitForUpdate, started.AddMinutes(11)));
        Assert.False(WatchdogTask.MarksAnUnfinishedUpdate(handover, exitForUpdate, started.AddMinutes(2)));
        Assert.False(WatchdogTask.MarksAnUnfinishedUpdate(handover, started.AddHours(1).UtcDateTime, started.AddHours(2)));
        Assert.False(WatchdogTask.MarksAnUnfinishedUpdate(null, exitForUpdate, started.AddMinutes(11)));
    }
}
