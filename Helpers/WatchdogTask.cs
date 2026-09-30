using FocusDesk.Services;
using ZeroZero.Startup;

// Microsoft.Win32.TaskScheduler.Task would otherwise be ambiguous against System.Threading.Tasks.Task
// (ImplicitUsings), and "Task" alone reads like the async one at every use site here.
using ScheduledTask = Microsoft.Win32.TaskScheduler.Task;

namespace FocusDesk.Helpers;

/// <summary>
/// Keeps FocusDesk alive via Task Scheduler: general crash-resilience infrastructure, independent
/// of any focus-session lever — the same role the watchdog plays in ChargeKeeper. It is entirely
/// FocusDesk's own responsibility: created and kept correct unconditionally, every time the
/// application starts, per REQUIREMENTS-FOR-SHARED-LIBRARY.md section F. ZeroZero.Startup supplies
/// <see cref="TaskIdentity"/>; it deliberately owns nothing else here.
/// </summary>
internal static class WatchdogTask
{
    private static string HoldMarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusDesk", "watchdog-hold.marker");

    internal static bool HoldMarkerExists => File.Exists(HoldMarkerPath);

    /// <summary>Whether a probe stays down: a hold marker, and no session recorded as running. Exit
    /// is refused mid-session, but a session Home Assistant arms while Exit is already under way, or
    /// a marker written by hand, would otherwise keep a running session's levers down for good.</summary>
    internal static bool HoldsProbeOff =>
        HoldMarkerExists && !RecordsARunningSession(RecordedSession(), DateTimeOffset.Now);

    /// <summary>A record whose end is still ahead. One already past is ended by the next start, so
    /// there is nothing a probe needs to bring back for it.</summary>
    internal static bool RecordsARunningSession(FocusSessionRecord? session, DateTimeOffset now) =>
        session is { } recorded && recorded.EndsAt > now;

    /// <summary>The record in settings.json. Unreadable counts as none: a start that cannot read it
    /// could not resume the session either.</summary>
    private static FocusSessionRecord? RecordedSession()
    {
        try { return new SettingsFocusSessionRecord().Read(); }
        catch (Exception ex) { AppLog.Error("WatchdogTask.RecordedSession", ex); return null; }
    }

    /// <summary>Written on a deliberate exit so watchdog probes leave it alone. Only ever written
    /// with no session running: the exit is refused otherwise.</summary>
    internal static void WriteHoldMarker()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HoldMarkerPath)!);
            File.WriteAllText(HoldMarkerPath, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex) { AppLog.Error("WatchdogTask.WriteHoldMarker", ex); }
    }

    /// <summary>Cleared on every deliberate start, so resurrection is re-armed. A watchdog probe
    /// never clears it.</summary>
    internal static void TryClearHoldMarker()
    {
        try { File.Delete(HoldMarkerPath); }
        catch { /* best-effort */ }
    }

    /// <summary>Registers the watchdog task. Never throws. Skipped for non-installed runs: a task
    /// pointing at a build-output exe would resurrect stale dev binaries for weeks.</summary>
    internal static void TryEnsureTask()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return;
            if (!InstallLocations.IsInstalledExe(exe))
            {
                AppLog.Info("Watchdog: not running from the install directory — task registration skipped.");
                return;
            }

            TaskIdentity user = TaskIdentity.Current();

            using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
            using ScheduledTask? existing = ts.GetTask(TaskDefinitions.TaskPath(TaskDefinitions.WatchdogTaskName));
            if (existing is not null && TaskDefinitions.Matches(existing.Definition, exe))
                return;   // current definition already registered

            using var definition = TaskDefinitions.BuildWatchdog(ts, exe, user);
            bool ok = Register(ts, definition);
            AppLog.Info(ok
                ? $"Watchdog: scheduled task '{TaskDefinitions.WatchdogTaskName}' registered (5-min + unlock + resume probes)."
                : $"Watchdog: FAILED to register '{TaskDefinitions.WatchdogTaskName}' — no external restart safety net this run.");
        }
        catch (Exception ex) { AppLog.Error("WatchdogTask.TryEnsureTask", ex); }
    }

    /// <summary>Best-effort wrapper: a failure here costs the safety net, and must never cost the
    /// app its startup.</summary>
    private static bool Register(Microsoft.Win32.TaskScheduler.TaskService ts, Microsoft.Win32.TaskScheduler.TaskDefinition definition)
    {
        try
        {
            TaskDefinitions.Register(ts, TaskDefinitions.WatchdogTaskName, definition);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("WatchdogTask.Register", ex);
            return false;
        }
    }
}
