using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The goal typed for a session: it stands in for a surface's own line only when one was set, it
/// survives a restart with the session, and its words never reach the log.
/// </summary>
public class FocusSessionGoalTests
{
    private const string Goal = "Finish chapter seven of the manual";

    [Theory]
    [InlineData(Goal, Goal)]
    [InlineData("  Finish chapter seven of the manual\t", Goal)]
    [InlineData("", "Focus session in progress")]
    [InlineData(" \t\r\n ", "Focus session in progress")]
    [InlineData(null, "Focus session in progress")]
    public void TheGoalReplacesASurfacesLine_OnlyWhereOneWasSet(string? typed, string shown)
    {
        Assert.Equal(shown, FocusSessionGoal.Or(typed, "Focus session in progress"));

        // Each surface hands its own line through the same choice: the dial's headline, the focus
        // point's line on the cover, and the line in the focus-point window.
        string cover = RepoFiles.Read(Path.Combine("UI", "ScreenCoverWindow.xaml.cs"));
        Assert.Contains("FocusSessionGoal.Or(goal, _headline)", cover, StringComparison.Ordinal);
        Assert.Contains("FocusSessionGoal.Or(goal, AppText.Get(\"CoverFocusPointHint\"))", cover, StringComparison.Ordinal);
        Assert.Contains("FocusSessionGoal.Or(_goal, AppText.Get(\"FocusPointWindowHint\"))",
                        RepoFiles.Read(Path.Combine("UI", "FocusPointWindow.xaml.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void AGoalIsOneLine_OfAtMostEightyCharacters()
    {
        Assert.Equal("ab c", FocusSessionGoal.Clean("a\u0007b\n c\u001b"));
        Assert.Equal(FocusSessionGoal.MaxLength, FocusSessionGoal.Clean(new string('x', 200)).Length);
    }

    /// <summary>The words go with the session and through a restart, and the log says only that a
    /// goal was set.</summary>
    [Fact]
    public void TheGoal_SurvivesARestart_AndNeverReachesTheLog()
    {
        var now = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
        var record = new FakeFocusSessionRecord();
        var log = new List<string>();
        FocusSessionEngine Engine() => new(
            new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(), record,
            () => now, (what, cause) => log.Add($"{what}{cause.Clause}"), _ => { }, new FakeFocusLever());

        var plan = FocusSessionPlan.For(FocusSessionKind.ScreenBreak, false, false, false);
        Assert.Equal(FocusArmOutcome.Armed, Engine().Arm(25, plan, "a test", $"  {Goal}\n"));
        Assert.Equal(Goal, record.Held?.Goal);

        var resumed = Engine();
        resumed.Start();
        Assert.Equal(Goal, resumed.Snapshot().Goal);

        now = now.AddMinutes(30);
        resumed.Tick();
        Assert.False(resumed.Snapshot().IsRunning);

        Assert.Contains(log, line => line.Contains("with a goal set", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.Contains("chapter seven", StringComparison.OrdinalIgnoreCase));
    }
}
