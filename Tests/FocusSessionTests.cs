using System;
using System.Collections.Generic;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>A lever that records what it was asked to do and answers however a test wants.</summary>
internal sealed class FakeFocusLever : IFocusLever
{
    public string? RefusalText { get; set; }

    public bool EngageSucceeds { get; set; } = true;

    public int Engagements { get; private set; }

    public int Lifts { get; private set; }

    public int Resumptions { get; private set; }

    public string? Refusal() => RefusalText;

    public bool Engage(ActionCause cause)
    {
        Engagements++;
        return EngageSucceeds;
    }

    public void Resume(ActionCause cause) => Resumptions++;

    public bool Lift(ActionCause cause)
    {
        Lifts++;
        return true;
    }
}

internal sealed class FakeFocusSessionRecord : IFocusSessionRecord
{
    public FocusSessionRecord? Held { get; set; }

    public bool SaveSucceeds { get; set; } = true;

    public FocusSessionRecord? Read() => Held;

    public bool Save(FocusSessionRecord session)
    {
        if (!SaveSucceeds) return false;
        Held = session;
        return true;
    }

    public void Clear() => Held = null;
}

/// <summary>
/// The rules a focus session cannot get wrong: it ends even if the machine was switched off through
/// its expiry, it cannot be talked out of early without the second request landing in its window,
/// and it refuses to arm with nothing to do.
/// </summary>
/// <remarks>Everything here runs against fakes. No brightness is written, no cover is shown and no
/// settings document is touched.</remarks>
public class FocusSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class Bed
    {
        public DateTimeOffset Now = Noon;
        public FakeFocusLever Screen { get; } = new();
        public FakeFocusLever Cover { get; } = new();
        public FakeFocusSessionRecord Record { get; } = new();
        public List<string> Log { get; } = [];
        public List<FocusHistoryEntry> History { get; } = [];
        public FocusSessionEngine Engine { get; }

        public Bed() => Engine = new FocusSessionEngine(
            Screen, Cover, Record, () => Now,
            (what, cause) => Log.Add($"{what} ({cause})"),
            History.Add);

        public void Advance(TimeSpan by) => Now += by;
    }

    // ── An expired session is always cleared at the next start ──────────────────────────────────

    [Fact]
    public void ASessionThatExpiredWhileTheMachineWasOff_IsLiftedAtTheNextStart()
    {
        // The whole of what makes this survive a power cut: nothing was running to notice the timer
        // pass, so the start is the only moment left that can end it.
        var bed = new Bed { Now = Noon };
        bed.Record.Held = new FocusSessionRecord(
            Noon.AddMinutes(-150), Noon.AddMinutes(-90), true, true);

        bed.Engine.Start();

        Assert.Equal(1, bed.Screen.Lifts);
        Assert.Equal(1, bed.Cover.Lifts);
        Assert.Null(bed.Record.Held);
        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
        Assert.Equal(FocusSessionOutcome.FoundStale, Assert.Single(bed.History).Outcome);
    }

    [Fact]
    public void ASessionStillWithinItsTime_ResumesAndTakesUpOnlyTheLeversItOwns()
    {
        var bed = new Bed();
        var ends = Noon.AddMinutes(30);
        bed.Record.Held = new FocusSessionRecord(Noon.AddMinutes(-30), ends, true, true);

        bed.Engine.Start();

        var session = bed.Engine.Snapshot();
        Assert.Equal(FocusSessionStage.Active, session.Stage);
        Assert.Equal(ends, session.EndsAt);
        // Resumed, never re-engaged: each lever decides what that means for itself, and neither is
        // lifted on the way.
        Assert.Equal(1, bed.Screen.Resumptions);
        Assert.Equal(1, bed.Cover.Resumptions);
        Assert.Equal(0, bed.Screen.Engagements);
        Assert.Equal(0, bed.Screen.Lifts);
    }

    [Fact]
    public void ASessionThatOwnsOnlyOneLever_LeavesTheOtherAlone()
    {
        var bed = new Bed();
        bed.Record.Held = new FocusSessionRecord(Noon, Noon.AddMinutes(30),
                                                 DimsScreen: true, CoversScreen: false);

        bed.Engine.Start();

        Assert.Equal(1, bed.Screen.Resumptions);
        Assert.Equal(0, bed.Cover.Resumptions);
    }

    [Fact]
    public void ALeverRecordWithNoSessionBehindIt_IsPutBackAtTheNextStart()
    {
        var bed = new Bed();

        bed.Engine.Start();

        Assert.Equal(1, bed.Screen.Lifts);
        // The cover is a window and died with the run that raised it. Taking it down again costs
        // nothing and is what stops a black screen outliving a session nobody owns.
        Assert.Equal(1, bed.Cover.Lifts);
    }

    [Fact]
    public void ARestartMidCancel_DiscardsTheAttemptAndResumesActive()
    {
        var bed = new Bed();
        bed.Record.Held = new FocusSessionRecord(Noon, Noon.AddMinutes(30), true, true);

        bed.Engine.Start();

        // The confirm window is a live interaction: a restart must neither end a session nor grant
        // an open-ended window.
        Assert.Equal(FocusSessionStage.Active, bed.Engine.Snapshot().Stage);
    }

    // ── Arming ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ASessionWithNeitherLeverChosen_IsRefusedAndArmsNothing()
    {
        var bed = new Bed();

        var outcome = bed.Engine.Arm(60, dimsScreen: false, coversScreen: false, "a test");

        Assert.Equal(FocusArmOutcome.NoLeverChosen, outcome);
        Assert.Null(bed.Record.Held);
        Assert.Equal(0, bed.Screen.Engagements);
        Assert.Equal(0, bed.Cover.Engagements);
        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
    }

    [Fact]
    public void ALeverThatWouldRefuse_StopsTheWholeSessionRatherThanHalfArmingIt()
    {
        var bed = new Bed();
        bed.Screen.RefusalText = "no display on this machine accepts a brightness from Windows";

        var outcome = bed.Engine.Arm(60, dimsScreen: true, coversScreen: true, "a test");

        Assert.Equal(FocusArmOutcome.LeverRefused, outcome);
        Assert.Null(bed.Record.Held);
        Assert.Equal(0, bed.Cover.Engagements);
    }

    [Fact]
    public void ALeverThatFailsToEngage_LeavesNothingEngagedBehindIt()
    {
        var bed = new Bed();
        bed.Cover.EngageSucceeds = false;

        var outcome = bed.Engine.Arm(60, dimsScreen: true, coversScreen: true, "a test");

        Assert.Equal(FocusArmOutcome.LeverFailed, outcome);
        Assert.Null(bed.Record.Held);
        Assert.Equal(1, bed.Screen.Lifts);
        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
    }

    [Fact]
    public void ASessionThatCouldNotBeWrittenDown_ArmsNothingAtAll()
    {
        // The record reaches disk before a lever moves, so a crash between the two leaves a session
        // the next start can end rather than a dimmed screen nothing owns.
        var bed = new Bed();
        bed.Record.SaveSucceeds = false;

        var outcome = bed.Engine.Arm(60, dimsScreen: true, coversScreen: true, "a test");

        Assert.Equal(FocusArmOutcome.LeverFailed, outcome);
        Assert.Equal(0, bed.Screen.Engagements);
        Assert.Equal(0, bed.Cover.Engagements);
    }

    [Fact]
    public void ASecondArmWhileOneRuns_ChangesNothing()
    {
        var bed = new Bed();
        bed.Engine.Arm(60, dimsScreen: true, coversScreen: false, "a test");

        Assert.Equal(FocusArmOutcome.AlreadyRunning,
                     bed.Engine.Arm(30, dimsScreen: true, coversScreen: true, "a test"));
        Assert.Equal(Noon.AddMinutes(60), bed.Engine.Snapshot().EndsAt);
        Assert.Equal(1, bed.Screen.Engagements);
    }

    [Fact]
    public void AnArmedSession_RunsToTheEndTimeAndIsWrittenDownBeforeALeverMoves()
    {
        var bed = new Bed();

        Assert.Equal(FocusArmOutcome.Armed,
                     bed.Engine.Arm(45, dimsScreen: true, coversScreen: true, "a test"));

        Assert.Equal(Noon.AddMinutes(45), bed.Record.Held!.Value.EndsAt);
        Assert.Equal(1, bed.Screen.Engagements);
        Assert.Equal(1, bed.Cover.Engagements);
    }

    [Fact]
    public void ASessionEndsItself_WhenItsOwnTimeRunsOutWhileTheApplicationIsRunning()
    {
        var bed = new Bed();
        bed.Engine.Arm(10, dimsScreen: true, coversScreen: false, "a test");

        bed.Advance(TimeSpan.FromMinutes(10));
        bed.Engine.Tick();

        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
        Assert.Equal(1, bed.Screen.Lifts);
        Assert.Null(bed.Record.Held);
        Assert.Equal(FocusSessionOutcome.RanToTime, Assert.Single(bed.History).Outcome);
    }

    // ── The cover ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCoverComesDown_WhenTheSessionRunsItsLength()
    {
        // A cover left up is a black screen with nothing on the machine able to clear it, so this is
        // the one thing about the cover that must never fail.
        var bed = new Bed();
        bed.Engine.Arm(30, dimsScreen: false, coversScreen: true, "a test");
        Assert.Equal(1, bed.Cover.Engagements);

        bed.Advance(TimeSpan.FromMinutes(30));
        bed.Engine.Tick();

        Assert.Equal(1, bed.Cover.Lifts);
        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
    }

    [Fact]
    public void ACoverIsTheOnlyLeverASessionNeeds()
    {
        // The cover alone is a session: the lever that made the screen dim too weak to matter is
        // reason enough to run one.
        var bed = new Bed();

        Assert.Equal(FocusArmOutcome.Armed,
                     bed.Engine.Arm(30, dimsScreen: false, coversScreen: true, "a test"));
        Assert.Equal(0, bed.Screen.Engagements);
    }

    [Fact]
    public void ADeadRunThatLeftARecordBehind_NeverLeavesTheCoverUp()
    {
        // The application died with a cover up and the session record already cleared. The window
        // went with the process; lifting again at the next start is what makes that certain.
        var bed = new Bed();

        bed.Engine.Start();

        Assert.Equal(1, bed.Cover.Lifts);
        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
    }

    // ── The length a start request runs for ─────────────────────────────────────────────────────

    [Fact]
    public void TheLengthChosenInTheStartBox_IsTheLengthTheSessionRunsFor()
    {
        // The start box hands its own value in; the stored default is what a request without one
        // falls back to. A box whose value were dropped would start an hour when it said 25.
        Assert.Equal(25, FocusStartRequest.Minutes(chosen: 25, storedDefault: 60));
        Assert.Equal(60, FocusStartRequest.Minutes(chosen: null, storedDefault: 60));
    }

    [Fact]
    public void ALengthOutsideTheRangeASessionAccepts_IsBroughtBackIntoIt() =>
        Assert.Equal((FocusSessionEngine.MinMinutes, FocusSessionEngine.MaxMinutes),
                     (FocusStartRequest.Minutes(0, 60), FocusStartRequest.Minutes(9_000, 60)));

    // ── The countdown ring ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRingIsWholeWhenASessionStartsAndGoneWhenItEnds()
    {
        var session = new FocusSnapshot(FocusSessionStage.Active, Noon, Noon.AddMinutes(60),
                                        false, true);

        Assert.Equal(1, FocusCoverCountdown.For(session, Noon)!.Value.FractionLeft, 3);
        Assert.Equal(0.5, FocusCoverCountdown.For(session, Noon.AddMinutes(30))!.Value.FractionLeft, 3);
        Assert.Equal(0, FocusCoverCountdown.For(session, Noon.AddMinutes(60))!.Value.FractionLeft, 3);
    }

    [Fact]
    public void NoSessionMeansNoRingAtAll() =>
        Assert.Null(FocusCoverCountdown.For(FocusSnapshot.None, Noon));

    [Fact]
    public void TheRingChangesColourAsItDrains()
    {
        // A ring that read one colour the whole way down would say nothing a plain arc does not.
        var session = new FocusSnapshot(FocusSessionStage.Active, Noon, Noon.AddMinutes(60),
                                        false, true);

        uint whole = FocusCoverCountdown.For(session, Noon)!.Value.Argb;
        uint spent = FocusCoverCountdown.For(session, Noon.AddMinutes(60))!.Value.Argb;

        Assert.NotEqual(whole, spent);
        Assert.Equal(0xFFu, whole >> 24);
    }

    // ── The staged cancel ───────────────────────────────────────────────────────────────────────

    /// <summary>The staged cancel's own timing, written out rather than read from the code it
    /// guards. A test that advances its clock by the constant follows a change to that constant
    /// instead of catching it.</summary>
    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    [Fact]
    public void TheStagedCancelRunsOnFiveMinutesAndTenSeconds() =>
        Assert.Equal((Wait, Window), (FocusSessionStages.CancelWait, FocusSessionStages.ConfirmWindow));

    private static Bed Running()
    {
        var bed = new Bed();
        Assert.Equal(FocusArmOutcome.Armed,
                     bed.Engine.Arm(120, dimsScreen: true, coversScreen: true, "a test"));
        return bed;
    }

    [Fact]
    public void AFirstCancelRequest_OpensTheWaitRatherThanEndingTheSession()
    {
        var bed = Running();

        bed.Engine.RequestCancel("a test");

        Assert.Equal(FocusSessionStage.Ending, bed.Engine.Snapshot().Stage);
        Assert.Equal(0, bed.Screen.Lifts);
        Assert.NotNull(bed.Record.Held);
    }

    [Fact]
    public void ARepeatedRequestDuringTheWait_ChangesNothingAtAll()
    {
        var bed = Running();
        bed.Engine.RequestCancel("a test");

        // The wait runs on a fixed clock from the first request: a repeat neither shortens nor
        // restarts it, so pressing again buys nothing worth relying on.
        bed.Advance(TimeSpan.FromMinutes(4));
        bed.Engine.RequestCancel("a test");
        bed.Engine.Tick();

        Assert.Equal(FocusSessionStage.Ending, bed.Engine.Snapshot().Stage);
        Assert.Equal(0, bed.Screen.Lifts);

        // One more minute is all the original wait had left, so the window opens on its own clock.
        bed.Advance(TimeSpan.FromMinutes(1));
        bed.Engine.Tick();
        Assert.Equal(FocusSessionStage.Confirm, bed.Engine.Snapshot().Stage);
    }

    [Fact]
    public void ASecondRequestInsideTheConfirmWindow_EndsTheSession()
    {
        var bed = Running();
        bed.Engine.RequestCancel("a test");

        bed.Advance(Wait);
        bed.Engine.Tick();
        Assert.Equal(FocusSessionStage.Confirm, bed.Engine.Snapshot().Stage);

        bed.Engine.RequestCancel("a test");

        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
        Assert.Equal(1, bed.Screen.Lifts);
        Assert.Equal(1, bed.Cover.Lifts);
        Assert.Null(bed.Record.Held);
        Assert.Equal(FocusSessionOutcome.EndedEarly, Assert.Single(bed.History).Outcome);
    }

    [Fact]
    public void ASecondRequestAfterTheConfirmWindow_LeavesTheSessionRunning()
    {
        var bed = Running();
        bed.Engine.RequestCancel("a test");

        bed.Advance(Wait + Window);
        bed.Engine.Tick();

        // Back to Active, and the attempt is forgotten: ending early again starts the wait afresh.
        Assert.Equal(FocusSessionStage.Active, bed.Engine.Snapshot().Stage);

        bed.Engine.RequestCancel("a test");
        Assert.Equal(FocusSessionStage.Ending, bed.Engine.Snapshot().Stage);
        Assert.Equal(0, bed.Screen.Lifts);
    }

    [Fact]
    public void ACancelAttempt_NeverPausesTheCountdownToTheOriginalEndTime()
    {
        var bed = Running();
        var ends = bed.Engine.Snapshot().EndsAt;

        bed.Engine.RequestCancel("a test");
        bed.Advance(Wait);
        bed.Engine.Tick();

        Assert.Equal(ends, bed.Engine.Snapshot().EndsAt);
    }

    [Fact]
    public void ACancelRequestWithNoSessionRunning_DoesNothing()
    {
        var bed = new Bed();

        bed.Engine.RequestCancel("a test");

        Assert.Equal(FocusSessionStage.Off, bed.Engine.Snapshot().Stage);
        Assert.Empty(bed.History);
    }

    // ── What the session reads as ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheSessionLineNamesTheLeversItOwnsAndTheTimeLeft()
    {
        var session = new FocusSnapshot(FocusSessionStage.Active, Noon, Noon.AddMinutes(30),
                                        DimsScreen: true, CoversScreen: true);

        string line = FocusSessionStages.Detail(session, Noon);

        Assert.Contains("30 min left", line, StringComparison.Ordinal);
        Assert.Contains("screen dimmed", line, StringComparison.Ordinal);
        Assert.Contains("screen covered", line, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStageHasAPublishedWord() =>
        // The words are the published contract: an automation compares against these literals, so a
        // stage added without one would publish nothing a receiver could hold.
        Assert.Equal(["Off", "Active", "Ending", "Confirm"], FocusSessionStages.Words);
}
