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
    /// <summary>Every way out of a running session the engine offers. A method added beside these
    /// would be a second route, and the wait and the second request would guard neither.</summary>
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

    /// <summary>Every shipped source file a person's own actions can reach: the whole tree but the
    /// session engine that declares the cancel, and the tests that drive it.</summary>
    private static IReadOnlyList<string> LocalSurfaces()
    {
        string root = RepoFiles.Root;
        string engine = Path.Combine(root, "Services", "FocusSession.cs");
        string tests = Path.Combine(root, "Tests") + Path.DirectorySeparatorChar;

        return [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar)
                          .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("publish", StringComparison.OrdinalIgnoreCase)))
            .Where(p => !p.StartsWith(tests, StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Equals(engine, StringComparison.OrdinalIgnoreCase))];
    }

    [Fact]
    public void NoSurfaceOnTheMachineAsksToCancelASession()
    {
        // RequestCancel is the only route to ending a session early, and it belongs to the MQTT
        // command seam alone. A button wired to it would hand the keyboard the way out the feature
        // exists to refuse. Every window lands inside this sweep as it is built.
        var offenders = LocalSurfaces()
            .Where(path => File.ReadAllText(path).Contains("RequestCancel", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepoFiles.Root, path))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Nothing on the machine may ask to cancel a session:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheGuardIsLookingAtFilesThatExist() =>
        // Guards the guard: a sweep that found nothing would pass by reading nothing at all.
        Assert.NotEmpty(LocalSurfaces());

    [Fact]
    public void ASessionRefusesToEndBeforeTheWaitHasRun()
    {
        // The property stated as behaviour rather than as source text: one request never ends a
        // session, however many times it arrives.
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var screen = new FakeFocusLever();
        var cover = new FakeFocusLever();
        var engine = new FocusSessionEngine(
            screen, cover, new FakeFocusSessionRecord(), () => now, (_, _) => { });

        engine.Arm(120, dimsScreen: true, coversScreen: false, "a test");
        for (int i = 0; i < 20; i++) engine.RequestCancel("a test");

        Assert.Equal(FocusSessionStage.Ending, engine.Snapshot().Stage);
        Assert.Equal(0, screen.Lifts);
    }

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
