using System;
using System.IO;
using System.Text.RegularExpressions;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The session-start focus point is seen in exactly one place per session: on the cover for a screen
/// break, in the pop-out for a program focus. The offer is owed by every arm alike, and the kind
/// enters only through the one rule that says whether the cover opens on it.
/// </summary>
/// <remarks>Behaviour where the engine can be driven, source text where it cannot: the pop-out is
/// WinUI code-behind that needs a display, and what must never appear in it is a reference, not a
/// value.</remarks>
public class FocusPointPlacementTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFocusPointOpensEitherTheCoverOrThePopOut_NeverBoth(bool programFocus)
    {
        var engine = new FocusSessionEngine(
            new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(),
            new FakeFocusSessionRecord(), () => Noon, (_, _) => { }, programs: new FakeFocusLever());
        var kind = programFocus ? FocusSessionKind.ProgramFocus : FocusSessionKind.ScreenBreak;

        Assert.Equal(FocusArmOutcome.Armed,
                     engine.Arm(25, FocusSessionPlan.For(kind, blocksNetwork: false, dimsScreen: false,
                                                         blocksInput: false), "a test"));

        bool coverOpens = CoverSequence.SceneAt(FocusCoverCountdown.For(engine.Snapshot(), Noon),
                                                CoverVisual.Ring, kind) == CoverScene.Opening;
        bool popOutOpens = engine.TakeOpeningPage();

        // A screen break sits under its own cover, which opens on the exercise; a program focus has
        // no cover, so the pop-out is the only place it can be offered.
        Assert.Equal(!programFocus, coverOpens);
        Assert.Equal(programFocus, popOutOpens);
        Assert.False(engine.TakeOpeningPage());
    }

    /// <summary>A reference to the kind: its type, its word helpers, a snapshot's or a record's
    /// <c>Kind</c>, or a local named for it. A platform name that merely ends in "Kind", such as the
    /// browser's resource access kind, is not one.</summary>
    private static readonly Regex KindReference =
        new(@"\bFocusSessionKinds?\b|\.Kind\b|\bkind\b", RegexOptions.CultureInvariant);

    /// <summary>The one place the kind may enter the offer.</summary>
    private const string TheRule = "CoverSequence.HasOpening(session.Kind)";

    /// <summary>The pop-out's members that decide whether the page appears and that show, run and take
    /// it down. Named, so a rename fails here rather than leaving the guard reading nothing.</summary>
    private static readonly string[] PopOutFocusPointMembers =
    [
        "OfferFocusPoint", "ShowFocusPoint", "ShowCountdown", "DropPage", "StartPage",
        "FallBackToCountdown", "OnShowFocusPoint", "OnSkipFocusPoint",
    ];

    [Fact]
    public void OnlyTheCoverRuleReadsTheKind_NothingThatOwesOrShowsTheOffer()
    {
        string popOut = RepoFiles.Read(Path.Combine("UI", "StatusWindow.xaml.cs"));
        string engine = RepoFiles.Read(Path.Combine("Services", "FocusSession.cs"));
        string service = RepoFiles.Read(Path.Combine("Services", "FocusSessionService.cs"));

        foreach (string member in PopOutFocusPointMembers)
            AssertNoKind(RepoFiles.Member(popOut, member), $"StatusWindow.{member}");

        string take = RepoFiles.Member(engine, "TakeOpeningPage");
        Assert.Contains(TheRule, take);
        AssertNoKind(take.Replace(TheRule, "", StringComparison.Ordinal),
                     "FocusSessionEngine.TakeOpeningPage outside the cover rule");

        // Every line that owes the offer, takes it or asks for it, wherever it sits.
        AssertEveryLineFreeOfKind(engine, "_openingPageOwed", minimum: 3);
        AssertEveryLineFreeOfKind(popOut, "TakeOpeningPage", minimum: 2);
        AssertEveryLineFreeOfKind(service, "TakeOpeningPage", minimum: 1);
    }

    private static void AssertNoKind(string text, string where) =>
        Assert.False(KindReference.IsMatch(text), $"{where} reads the kind of session");

    private static void AssertEveryLineFreeOfKind(string source, string marker, int minimum)
    {
        int found = 0;
        foreach (string line in source.Split('\n'))
        {
            if (!line.Contains(marker, StringComparison.Ordinal)) continue;
            found++;
            AssertNoKind(line, $"a line naming {marker}");
        }
        // Guards the guard: a marker renamed away would otherwise pass by matching nothing.
        Assert.True(found >= minimum, $"expected at least {minimum} lines naming {marker}, found {found}");
    }
}
