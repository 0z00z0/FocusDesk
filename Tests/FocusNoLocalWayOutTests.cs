using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The feature's central property, held against the source: a person at the keyboard can start a
/// focus session and cannot end one. Ending is Home Assistant's, after a five-minute wait and a
/// second request, or the session's own clock.
/// </summary>
/// <remarks>Source text and the engine's own surface rather than behaviour: the windows are WinUI
/// code-behind that cannot be driven without a display, and what must never appear there is a call,
/// not a value.</remarks>
public class FocusNoLocalWayOutTests
{
    /// <summary>Every public method the engine offers, each read and found to end nothing but through
    /// the staged cancel and the clock. A method added beside these could be a second route, and the
    /// wait and the second request would guard neither.</summary>
    private static readonly string[] EngineMethods =
        ["Snapshot", "Start", "Arm", "RequestCancel", "Tick", "KeepRecord"];

    [Fact]
    public void TheEngineOffersNoWayToEndASessionBesideTheStagedCancelAndTheClock()
    {
        var declared = typeof(FocusSessionEngine)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(FocusSessionEngine) && !m.IsSpecialName)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(EngineMethods.OrderBy(n => n, StringComparer.Ordinal), declared);
    }

    /// <summary>The only three shipped files that may name the cancel: the engine that declares it,
    /// the service that forwards it, and the MQTT command seam that is the one route in. A fourth
    /// is a second way out.</summary>
    private static readonly string[] MayNameTheCancel =
    [
        Path.Combine("Services", "FocusSession.cs"),
        Path.Combine("Services", "FocusSessionService.cs"),
        Path.Combine("Services", "MqttCommandActions.cs"),
    ];

    /// <summary>Every shipped source file, tests excluded — they drive the engine directly.</summary>
    private static IReadOnlyList<string> ShippedSource()
    {
        string root = RepoFiles.Root;
        string tests = Path.Combine(root, "Tests") + Path.DirectorySeparatorChar;

        return [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar)
                          .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("publish", StringComparison.OrdinalIgnoreCase)))
            .Where(p => !p.StartsWith(tests, StringComparison.OrdinalIgnoreCase))];
    }

    [Fact]
    public void ExactlyThreeFilesNameTheCancel_AndNoneOfThemIsAWindow()
    {
        // RequestCancel is the only route to ending a session early, and it belongs to the MQTT
        // command seam alone. A button wired to it would hand the keyboard the way out the feature
        // exists to refuse. Every window lands inside this sweep as it is built, so a fourth name
        // fails here rather than shipping.
        var naming = ShippedSource()
            .Where(path => File.ReadAllText(path).Contains("RequestCancel", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepoFiles.Root, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(MayNameTheCancel.Order(StringComparer.OrdinalIgnoreCase), naming);
    }

    [Fact]
    public void TheGuardIsLookingAtFilesThatExist() =>
        // Guards the guard: a sweep that found nothing would pass by reading nothing at all.
        Assert.NotEmpty(ShippedSource());

    [Fact]
    public void ASessionRefusesToEndBeforeTheWaitHasRun()
    {
        // The property stated as behaviour rather than as source text: one request never ends a
        // session, however many times it arrives.
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var screen = new FakeFocusLever();
        var cover = new FakeFocusLever();
        var engine = new FocusSessionEngine(
            screen, cover, new FakeFocusLever(), new FakeFocusLever(),
            new FakeFocusSessionRecord(), () => now, (_, _) => { });

        engine.Arm(120, FocusSessionPlan.For(FocusSessionKind.ScreenBreak, blocksNetwork: false,
                                             dimsScreen: true, blocksInput: false), "a test");
        for (int i = 0; i < 20; i++) engine.RequestCancel("a test");

        Assert.Equal(FocusSessionStage.Ending, engine.Snapshot().Stage);
        Assert.Equal(0, screen.Lifts);
    }

    // ── Leaving the application ─────────────────────────────────────────────────────────────────
    // Leaving puts back every lever the session holds, and the hold marker it writes keeps the
    // watchdog from starting FocusDesk again. An exit allowed mid-session is a way out nothing undoes.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExitIsRefusedWhileASessionRuns_AndAllowedOnceNoneDoes(bool programFocus)
    {
        // Either kind, and through the cancel's wait: the session holds its levers until the second
        // request or the clock ends it.
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var engine = new FocusSessionEngine(
            new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(),
            new FakeFocusSessionRecord(), () => now, (_, _) => { }, programs: new FakeFocusLever());
        var kind = programFocus ? FocusSessionKind.ProgramFocus : FocusSessionKind.ScreenBreak;

        Assert.True(DeliberateExit.Allows(engine.Snapshot()));

        Assert.Equal(FocusArmOutcome.Armed, engine.Arm(60, FocusSessionPlan.For(
            kind, blocksNetwork: false, dimsScreen: false, blocksInput: false), "a test"));
        Assert.Equal(kind, engine.Snapshot().Kind);
        Assert.False(DeliberateExit.Allows(engine.Snapshot()));

        engine.RequestCancel("a test");
        Assert.Equal(FocusSessionStage.Ending, engine.Snapshot().Stage);
        Assert.False(DeliberateExit.Allows(engine.Snapshot()));

        now = now.AddMinutes(61);
        engine.Tick();
        Assert.Equal(FocusSessionStage.Off, engine.Snapshot().Stage);
        Assert.True(DeliberateExit.Allows(engine.Snapshot()));
    }

    private static string AppCode => RepoFiles.Read("App.xaml.cs");

    /// <summary>The process's own end, however it is spelled: the application object's exit and the
    /// runtime's two.</summary>
    private static readonly Regex EndsTheProcess = new(@"\bExit\s*\(|\bFailFast\s*\(");

    [Fact]
    public void OnlyTheApplicationObjectEndsTheProcess_AndItsDeliberateExitRefusesASessionFirst()
    {
        // Every route that leaves on purpose meets the refusal inside Shutdown: the menu's Exit, the
        // update flow's hand-over and anything wired to either later. A second file ending the
        // process would be a route that never meets it, so it fails here rather than shipping.
        var ending = ShippedSource()
            .Where(path => EndsTheProcess.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepoFiles.Root, path))
            .ToArray();
        Assert.Equal(new[] { "App.xaml.cs" }, ending);

        // One deliberate exit, and the refusal is its first statement, ahead of the hold marker.
        var exits = Regex.Matches(AppCode, @"(?<![\w.])Exit\(\);");
        Assert.Single(exits);
        int shutdown = AppCode.IndexOf("private void Shutdown()", StringComparison.Ordinal);
        Assert.InRange(shutdown, 0, exits[0].Index);
        Assert.DoesNotMatch(new Regex(@"\n    (private|public|protected|internal) "),
                            AppCode[(shutdown + 1)..exits[0].Index]);
        Assert.Matches(new Regex(
            @"private void Shutdown\(\)\s*\{(\s*//[^\n]*)*\s*if \(!DeliberateExit\.Allows\(FocusSessionService\.Current\)\)\s*\{[^}]*return;\s*\}(\s*//[^\n]*)*\s*WatchdogTask\.WriteHoldMarker\(\);"),
            AppCode);

        // The runtime's exit only turns away a second instance or a held probe, in the constructor
        // before this process owns anything a session could hold.
        int constructor = AppCode.IndexOf("public App()", StringComparison.Ordinal);
        int owns = AppCode.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        var early = Regex.Matches(AppCode, @"Environment\.Exit\(");
        Assert.NotEmpty(early);
        Assert.All(early, m => Assert.InRange(m.Index, constructor, owns));
    }

    [Fact]
    public void TheMenuGreysExitOutWhileASessionRuns() =>
        // The row decides on the same rule the exit does, and while a session runs it carries no
        // action to reach at all.
        Assert.Matches(new Regex(
            @"DeliberateExit\.Allows\(session\)\s*\?\s*TrayMenuItem\.Command\(AppText\.Get\(""TrayMenuExit""\),[^;]*:\s*TrayMenuItem\.Command\(AppText\.Get\(""TrayMenuExitRefused""\), null, isEnabled: false\);"),
            RepoFiles.Read(Path.Combine("UI", "TrayIconHost.cs")));

    // ── The input block ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheInputBlockIsReleasedWhenTheApplicationCloses() =>
        // Leaving from the menu must hand back a machine that answers. The thread's exit would also
        // release it, but nothing inside the process can measure what a kill does to the block.
        Assert.Matches(new Regex(@"void Stop\(\)[\s\S]*?_inputBlock\.Release\(ActionCause\.ApplicationClosing\(\)\)"),
                       RepoFiles.Read(Path.Combine("Services", "FocusSessionService.cs")));

    [Fact]
    public void TheFirewallIsPutBackWhenTheApplicationCloses() =>
        // The block outlives the process, so one left in place by an exit that nothing restarts
        // would have no owner at all. The next start writes it again for a session still running.
        Assert.Matches(new Regex(@"void Stop\(\)[\s\S]*?_firewall\.Lift\(ActionCause\.ApplicationClosing\(\)\)"),
                       RepoFiles.Read(Path.Combine("Services", "FocusSessionService.cs")));

    // ── The cover ───────────────────────────────────────────────────────────────────────────────
    // Source text rather than behaviour: the cover is WinUI code-behind that cannot be driven
    // without a display, and what must never disappear from it is a call, not a value.

    private static string Cover => RepoFiles.Read(Path.Combine("UI", "ScreenCoverWindow.xaml.cs"));

    private static string CoverService => RepoFiles.Read(Path.Combine("Services", "ScreenCoverService.cs"));

    [Fact]
    public void TheCoverRefusesEveryCloseRequest() =>
        // Alt+F4, the switcher's close and an ordinary End task all reach the window as a close
        // request. Without this the cover is one keystroke away from gone, with the session still
        // running and nothing on screen saying so.
        Assert.Contains("NativeMethods.RefuseClose(", Cover, StringComparison.Ordinal);

    [Fact]
    public void TheOneCloseThatWorksLiftsTheRefusalFirst() =>
        // The session's own teardown and a rebuild both go through Dismiss, which allows the close
        // before asking for it. A teardown that did not would leave a cover nothing can take down.
        Assert.Matches(new Regex(@"_refusal\?\.Allow\(\);\s*Close\(\);"), Cover);

    [Fact]
    public void ACoverThatHasGoneIsPutBackByTheTick() =>
        // The second defence, and the one that does not depend on knowing how a cover went away.
        // The reading has to drive the rebuild: naming the method anywhere would also be satisfied
        // by its own declaration.
        Assert.Matches(new Regex(@"if \(AnyCoverIsGone\(\)\)[\s\S]{0,160}?Rebuild\(\);"), CoverService);

    [Fact]
    public void TheCoverAssertsItsStylesOnEveryTick() =>
        // A cover that could take focus is a cover that can be closed and typed at. Re-asserting
        // costs nothing and corrects a style anything else put back within the same second.
        Assert.Matches(new Regex(@"KeepOnTop\(\)\s*\{[^}]*MakeClickThroughAndUnfocusable"), Cover);

    [Fact]
    public void TheCoverComesDownWhenNoSessionIsBehindIt() =>
        // A cover with no session behind it is a black screen nobody can explain, and the tick is
        // the only thing left that would notice.
        Assert.Matches(new Regex(@"if \(!session\.IsRunning\) \{ Hide\("), CoverService);

    [Fact]
    public void TheCoverSaysWhatTheSessionIsDoing()
    {
        // The line is what stops the machine looking broken to whoever is sitting at it. A session
        // owning no lever says nothing rather than an empty sentence.
        var both = new FocusSnapshot(FocusSessionStage.Active, null, null,
                                     DimsScreen: true, CoversScreen: true);

        string line = ScreenCoverService.Levers(both);

        Assert.Contains("the screen is dimmed", line, StringComparison.Ordinal);
        Assert.Contains("the screen is covered", line, StringComparison.Ordinal);
        Assert.Equal("", ScreenCoverService.Levers(FocusSnapshot.None));
    }
}
