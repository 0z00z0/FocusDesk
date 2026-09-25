using System;
using System.Collections.Generic;
using System.Linq;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Xunit;
using static FocusDesk.Tests.MeasuredWindows;

namespace FocusDesk.Tests;

/// <summary>
/// The lever while it runs, against a composed desktop, a driven event watch and recorded minimises:
/// what the start sweep touches, a restore minimised again, the limit on how often, the stored action
/// read as Minimise, and nothing after the lever lifts.
/// </summary>
public class ProgramGateTests
{
    private const string Editor = @"C:\Program Files\Example\editor.exe";
    // In a folder of its own: a program in the allowed editor's own folder counts as part of it.
    private const string Game   = @"C:\Program Files\Games\game.exe";

    private readonly FakeDesktop _desktop = new();
    private readonly FakeWindowWatch _watch = new();
    private readonly RecordedMinimises _actions;
    private readonly List<string> _log = [];
    private DateTimeOffset _now = new(2026, 9, 25, 18, 0, 0, TimeSpan.Zero);
    private readonly ProgramGate _gate;

    public ProgramGateTests()
    {
        _actions = new RecordedMinimises(_desktop);
        _gate = new ProgramGate(_desktop, _watch, _actions, () => _now, _log.Add);
    }

    private static GateContext Allowing(params FocusProgramEntry[] entries) =>
        new(entries, FocusProgramAction.Minimise, WindowsFolder, FocusDeskPath, [], [@"C:\Program Files"], _ => false);

    private static FocusProgramEntry Allowed(string path) =>
        new() { Kind = FocusProgramKind.ProgramFile, Id = path, CanRun = true };

    [Fact]
    public void TheStartSweepMinimisesOnlyWhatIsNotAllowed()
    {
        var editor = _desktop.Add(Window(Editor));
        var game = _desktop.Add(Window(Game));
        var explorer = _desktop.Add(Window(@"C:\Windows\explorer.exe"));

        Assert.True(_gate.Arm(Allowing(Allowed(Editor))));

        Assert.Equal([game], _actions.Minimised);
        Assert.DoesNotContain(editor, _actions.Minimised);
        Assert.DoesNotContain(explorer, _actions.Minimised);
    }

    [Fact]
    public void ARestoreIsMinimisedAgain_ButNoFasterThanOnceEvery250Milliseconds()
    {
        var game = _desktop.Add(Window(Game));
        _gate.Arm(Allowing());
        Assert.Single(_actions.Minimised);

        // Restored at once: too soon to act again.
        _now += TimeSpan.FromMilliseconds(100);
        _desktop.Restore(game);
        _watch.Raise(game);
        Assert.Single(_actions.Minimised);

        // Restored again after the interval: minimised again.
        _now += TimeSpan.FromMilliseconds(200);
        _watch.Raise(game);
        Assert.Equal(2, _actions.Minimised.Count);
    }

    /// <summary>A window already minimised is left alone by the tick, so a sweep every second does not
    /// ask for the same minimise over and over.</summary>
    [Fact]
    public void AWindowAlreadyMinimisedIsNotMinimisedAgainByTheTick()
    {
        _desktop.Add(Window(Game));
        _gate.Arm(Allowing());

        _now += TimeSpan.FromSeconds(5);
        Assert.True(_gate.Hold());

        Assert.Single(_actions.Minimised);
    }

    /// <summary>An action a later version stores arrives through the roaming settings file. This
    /// version cannot close or end anything, so it minimises, and says so once.</summary>
    [Fact]
    public void AStoredAskToCloseOrForceClose_IsCarriedOutAsMinimiseAndLoggedOnce()
    {
        _desktop.Add(Window(Game));
        _desktop.Add(Window(@"C:\Program Files\Example\other.exe"));
        var context = Allowing() with { DefaultAction = FocusProgramAction.ForceClose };

        _gate.Arm(context);

        Assert.Equal(2, _actions.Minimised.Count);
        Assert.Single(_log, line => line.Contains("carried out as Minimise", StringComparison.Ordinal));
    }

    [Fact]
    public void NothingHappensAfterTheLeverIsLifted()
    {
        var game = _desktop.Add(Window(Game));
        _gate.Arm(Allowing());
        _gate.Lift();
        _desktop.Restore(game);

        _now += TimeSpan.FromSeconds(1);
        _watch.Raise(game);

        Assert.Single(_actions.Minimised);
        Assert.False(_gate.Hold());
        Assert.Equal(1, _watch.Stops);
    }

    [Fact]
    public void AWatchThatWillNotStartArmsNothing()
    {
        _desktop.Add(Window(Game));
        _watch.RefuseStart = true;

        Assert.False(_gate.Arm(Allowing()));
        Assert.False(_gate.IsArmed);
        Assert.Empty(_actions.Minimised);
    }

    /// <summary>The tick starts a watch whose thread died, and says the lever is lost only when it
    /// will not start again.</summary>
    [Fact]
    public void AWatchThatDiedIsStartedAgainOnTheTick_AndTheLeverIsLostOnlyWhenItWillNot()
    {
        _gate.Arm(Allowing());

        _watch.Die();
        Assert.True(_gate.Hold());
        Assert.Equal(2, _watch.Starts);

        _watch.Die();
        _watch.RefuseStart = true;
        Assert.False(_gate.Hold());
    }

    /// <summary>Each window is written to the log by its program's identifier, never by its title,
    /// which can carry the name of a document or a message.</summary>
    [Fact]
    public void TheLogNamesTheProgramNeverAWindowTitle()
    {
        _desktop.Add(Window(Game));
        _gate.Arm(Allowing());

        Assert.Contains(_log, line => line.Contains(Game, StringComparison.Ordinal));
    }
}
