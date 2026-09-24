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

    /// <summary>Written on a deliberate exit so watchdog probes leave it alone. Nothing calls this
    /// yet — FocusDesk has no tray Exit action until PLAN.md step 8 builds the tray menu — but the
    /// mechanism is ready for it.</summary>
    internal static void WriteHoldMarker()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HoldMarkerPath)!);
            File.WriteAllText(HoldMarkerPath, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex) { AppLog.Error("WatchdogTask.WriteHoldMarker", ex); }
    }

    /// <summary>Cleared on every start, so resurrection is re-armed.</summary>
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
