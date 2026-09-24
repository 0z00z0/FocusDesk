using FocusDesk.Helpers;
using Microsoft.Win32.TaskScheduler;
using Xunit;
using ZeroZero.Startup;

namespace FocusDesk.Tests;

/// <summary>
/// Locks down the watchdog task definition: the settings that stop Task Scheduler killing the app,
/// and the fields a repair must recognise so re-registration stays idempotent.
///
/// <para>Definitions are only built here — <see cref="TaskService.NewTask"/> is an in-memory COM
/// object — so the machine's real tasks are untouched.</para>
/// </summary>
public class TaskDefinitionsTests
{
    private const string Exe = @"C:\Users\Someone\AppData\Local\Programs\FocusDesk\FocusDesk.exe";
    private static readonly TaskIdentity User = new("AzureAD\\SomeUser", "S-1-5-21-1-2-3-1001");

    private static TaskDefinition Watchdog(TaskService ts) => TaskDefinitions.BuildWatchdog(ts, Exe, User);

    [Fact]
    public void Watchdog_IsImmuneToTheSchedulersKillDefaults()
    {
        // A virgin definition carries DisallowStartIfOnBatteries, StopIfGoingOnBatteries,
        // AllowHardTerminate and ExecutionTimeLimit=PT72H. The scheduler acts on all four, so
        // forgetting one kills the app at undock, or silently three days in.
        using var ts = new TaskService();
        TaskDefinition td = Watchdog(ts);

        Assert.False(td.Settings.AllowHardTerminate);
        Assert.False(td.Settings.StopIfGoingOnBatteries);
        Assert.False(td.Settings.DisallowStartIfOnBatteries);
        Assert.Equal(TimeSpan.Zero, td.Settings.ExecutionTimeLimit);
    }

    [Fact]
    public void Triggers_TakeTheAccountName_WhilePrincipalTakesTheSid()
    {
        // The two identity fields are not interchangeable: a trigger stores the resolved account
        // name (the scheduler rewrites a SID into one), the principal stores the SID verbatim.
        using var ts = new TaskService();
        TaskDefinition wd = Watchdog(ts);

        Assert.Equal(User.AccountName, ((SessionStateChangeTrigger)wd.Triggers[1]).UserId);
        Assert.Equal(User.Sid, wd.Principal.UserId);
        Assert.Equal(TaskLogonType.InteractiveToken, wd.Principal.LogonType);
        Assert.Equal(TaskRunLevel.Highest, wd.Principal.RunLevel);
    }

    [Fact]
    public void Watchdog_KeepsAllThreeProbes()
    {
        // The repetition is the general backstop; unlock and resume close the window in which a
        // kill during sleep would leave the app down while the machine is in use.
        using var ts = new TaskService();
        TaskDefinition td = Watchdog(ts);

        Assert.Equal(TimeSpan.FromMinutes(5), td.Triggers.OfType<TimeTrigger>().Single().Repetition.Interval);

        SessionStateChangeTrigger unlock = td.Triggers.OfType<SessionStateChangeTrigger>().Single();
        Assert.Equal(TaskSessionStateChangeType.SessionUnlock, unlock.StateChange);
        Assert.Equal(TimeSpan.FromSeconds(5), unlock.Delay);

        EventTrigger resume = td.Triggers.OfType<EventTrigger>().Single();
        Assert.Equal(TimeSpan.FromSeconds(15), resume.Delay);
        Assert.Contains("Microsoft-Windows-Power-Troubleshooter", resume.Subscription);
        Assert.Contains("EventID=1", resume.Subscription);
    }

    [Fact]
    public void Watchdog_RunsTheProbeArgument()
    {
        using var ts = new TaskService();
        Assert.Equal(TaskDefinitions.WatchdogArg, ((ExecAction)Watchdog(ts).Actions[0]).Arguments);
    }

    [Fact]
    public void ExecPath_IsQuoted_ButStillMatchesTheRawPath()
    {
        // The install path can contain a space, so the registered Command is quoted.
        using var ts = new TaskService();
        TaskDefinition td = Watchdog(ts);

        Assert.Equal($"\"{Exe}\"", ((ExecAction)td.Actions[0]).Path);
        Assert.True(TaskDefinitions.TargetsExe(td, Exe));   // the quoting must not break the lookup
    }

    [Fact]
    public void Matches_RequiresBothTheStampAndTheExe()
    {
        // The stamp alone is not enough: an upgrade that moves the exe must still be rewritten, and
        // a task pointing at another exe must never be mistaken for this one.
        using var ts = new TaskService();

        Assert.True(TaskDefinitions.Matches(Watchdog(ts), Exe));
        Assert.False(TaskDefinitions.Matches(Watchdog(ts), @"C:\Elsewhere\FocusDesk.exe"));

        TaskDefinition unstamped = Watchdog(ts);
        unstamped.RegistrationInfo.Description = "Something a previous version wrote";
        Assert.False(TaskDefinitions.Matches(unstamped, Exe));
        Assert.True(TaskDefinitions.TargetsExe(unstamped, Exe));   // but it still points at the exe
    }

    [Fact]
    public void Description_CarriesTheStamp_SoTheRepairCanRecogniseIt()
    {
        Assert.Contains(TaskDefinitions.DefStamp, TaskDefinitions.WatchdogDescription);
    }
}
