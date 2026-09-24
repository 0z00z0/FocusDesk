using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
}
