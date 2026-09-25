using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FocusDesk.Services;
using Xunit;
using static FocusDesk.Tests.MeasuredWindows;

namespace FocusDesk.Tests;

/// <summary>
/// The focus-app lever inside a session: refused where it would minimise everything, a session holding
/// it alone, dropped when the window watch is gone for good, taken up again with a sweep when a session
/// resumes, and stopped on lift and on exit.
/// </summary>
/// <remarks>Against a composed desktop and a driven event watch; nothing lists or touches a real
/// window.</remarks>
public class FocusProgramLeverTests
{
    private const string Editor = @"C:\Program Files\Example\editor.exe";
    private const string Game   = @"C:\Program Files\Games\game.exe";

    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDesktop _desktop = new();
    private readonly FakeWindowWatch _watch = new();
    private readonly RecordedMinimises _actions;
    private readonly ProgramGate _gate;

    public FocusProgramLeverTests()
    {
        _actions = new RecordedMinimises(_desktop);
        _gate = new ProgramGate(_desktop, _watch, _actions, () => Noon, _ => { });
    }

    private static GateContext Context(params FocusProgramEntry[] entries) =>
        new(entries, FocusProgramAction.Minimise, WindowsFolder, FocusDeskPath, [], [@"C:\Program Files"], _ => false);

    private static FocusProgramEntry Row(string path, bool run) =>
        new() { Kind = FocusProgramKind.ProgramFile, Id = path, CanRun = run, CanUseNetwork = true };

    /// <summary>With nothing kept usable, every program window would be minimised the moment the
    /// session started. A Windows program's row does not count: it is usable anyway.</summary>
    [Fact]
    public void TheLeverIsRefusedWhenNoRowKeepsAProgramUsable()
    {
        Assert.NotNull(new FocusProgramLever(_gate, () => Context()).Refusal());
        Assert.NotNull(new FocusProgramLever(_gate, () => Context(Row(Editor, run: false))).Refusal());
        Assert.NotNull(new FocusProgramLever(_gate, () => Context(Row(@"C:\Windows\System32\cmd.exe", run: true))).Refusal());
        Assert.NotNull(new FocusProgramLever(_gate, () => null).Refusal());

        Assert.Null(new FocusProgramLever(_gate, () => Context(Row(Editor, run: true))).Refusal());
    }

    [Fact]
    public void ASessionHoldingOnlyThisLeverIsArmed_AndARefusedOneLeavesTheOtherLeversRunning()
    {
        var programs = new FakeFocusLever();
        var engine = Engine(programs, out _);

        Assert.Equal(FocusArmOutcome.Armed,
                     engine.Arm(30, false, false, false, false, "a test", limitsPrograms: true));
        Assert.True(engine.Snapshot().LimitsPrograms);

        var refused = new FakeFocusLever { RefusalText = "nothing kept usable" };
        var second = Engine(refused, out _);
        Assert.Equal(FocusArmOutcome.Armed,
                     second.Arm(30, dimsScreen: true, false, false, false, "a test", limitsPrograms: true));
        Assert.False(second.Snapshot().LimitsPrograms);
        Assert.Equal(0, refused.Engagements);
    }

    /// <summary>A watch that died and will not start again is a lever no longer held: the session runs
    /// on without it and says so, rather than reporting a limit nothing enforces.</summary>
    [Fact]
    public void TheLeverIsDroppedWhenTheWatchDiesAndWillNotStartAgain()
    {
        _desktop.Add(Window(Editor));
        var lever = new FocusProgramLever(_gate, () => Context(Row(Editor, run: true)));
        var engine = Engine(lever, out var record);

        engine.Arm(30, dimsScreen: true, false, false, false, "a test", limitsPrograms: true);
        Assert.True(engine.Snapshot().LimitsPrograms);

        _watch.Die();
        _watch.RefuseStart = true;
        engine.Tick();

        Assert.True(engine.Snapshot().IsRunning);
        Assert.False(engine.Snapshot().LimitsPrograms);
        Assert.False(record.Held!.Value.LimitsPrograms);
    }

    /// <summary>Nothing of the watch survives the process, so a session that outlived the application
    /// is taken up again with a sweep at once: a program started while FocusDesk was down is minimised
    /// without waiting for it to be restored.</summary>
    [Fact]
    public void AResumedSessionTakesTheLeverUpAgainWithAnImmediateSweep()
    {
        var game = _desktop.Add(Window(Game));
        var lever = new FocusProgramLever(_gate, () => Context(Row(Editor, run: true)));
        var record = new FakeFocusSessionRecord
        {
            Held = new FocusSessionRecord(Noon.AddMinutes(-10), Noon.AddMinutes(20), false, false,
                                          LimitsPrograms: true),
        };
        var engine = new FocusSessionEngine(new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(),
                                            new FakeFocusLever(), record, () => Noon, (_, _) => { },
                                            programs: lever);

        engine.Start();

        Assert.True(engine.Snapshot().LimitsPrograms);
        Assert.Equal([game], _actions.Minimised);
    }

    [Fact]
    public void TheWatchStopsWhenTheSessionEnds()
    {
        var lever = new FocusProgramLever(_gate, () => Context(Row(Editor, run: true)));
        var engine = Engine(lever, out _);

        engine.Arm(1, false, false, false, false, "a test", limitsPrograms: true);
        Assert.True(_watch.IsRunning);

        _now = Noon.AddMinutes(2);
        engine.Tick();

        Assert.False(engine.Snapshot().IsRunning);
        Assert.False(_watch.IsRunning);
        Assert.False(_gate.IsArmed);
    }

    [Fact]
    public void TheWatchStopsWhenTheApplicationExits() =>
        // The record keeps the lever and the next start takes it up again; an exit must not leave a
        // watch running with nothing owning it.
        Assert.Matches(new Regex(@"void Stop\(\)[\s\S]*?_programGate\.Lift\(\)"),
                       RepoFiles.Read(Path.Combine("Services", "FocusSessionService.cs")));

    private DateTimeOffset _now = Noon;

    private FocusSessionEngine Engine(IFocusLever programs, out FakeFocusSessionRecord record)
    {
        record = new FakeFocusSessionRecord();
        return new FocusSessionEngine(new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(),
                                      new FakeFocusLever(), record, () => _now, (_, _) => { },
                                      programs: programs);
    }
}
