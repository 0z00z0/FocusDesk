using FocusDesk.Services;
using ZeroZero.Startup;

namespace FocusDesk.Helpers;

/// <summary>
/// Whether FocusDesk starts at sign-in. The installer registers the logon task and offers the
/// choice; this is the same switch from the notification-area menu.
/// </summary>
/// <remarks>Reading is cheap enough for a menu rebuild: the shared task fetches by name rather than
/// walking the scheduler's folder. Every failure path is quiet — a machine installed without the
/// task, or a scheduler that refuses, leaves the switch off rather than taking the menu down.</remarks>
internal static class LaunchAtStartup
{
    /// <summary>The logon task's name in the scheduler's root folder. The installer registers it
    /// under exactly this name, and the two cannot be kept in step by anything but the spelling.</summary>
    internal const string TaskName = "FocusDesk AutoStart";

    /// <summary>True where the logon task is registered and enabled.</summary>
    public static bool IsOn
    {
        get
        {
            try
            {
                using var task = new StartupTask(new StartupTaskOptions { TaskName = TaskName });
                return task.IsEnabled;
            }
            catch (Exception ex)
            {
                AppLog.Error("LaunchAtStartup.IsOn", ex);
                return false;
            }
        }
    }

    /// <summary>Writes the state asked for and says whether the write landed. The caller passes the
    /// state the click is moving to, never a fresh read: a read and a write cannot then race.</summary>
    public static bool TrySet(bool on)
    {
        try
        {
            using var task = new StartupTask(new StartupTaskOptions { TaskName = TaskName });
            if (on) task.Enable(); else task.Disable();
            AppLog.Info($"Launch at startup switched {(on ? "on" : "off")}.");
            return true;
        }
        catch (Exception ex)
        {
            // Most often an installation without the logon task, which the shared task reports as
            // an invalid operation rather than a missing file.
            AppLog.Error("LaunchAtStartup.TrySet", ex);
            return false;
        }
    }
}
