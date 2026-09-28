using System;
using System.IO;
using System.Text.RegularExpressions;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The session-start focus point is offered at the start of every session, of either kind, and the
/// kind has no say in it: the offer is keyed on a session arming and on nothing else.
/// </summary>
/// <remarks>Behaviour where the engine can be driven, source text where it cannot: the pop-out is
/// WinUI code-behind that needs a display, and what must never appear in it is a reference, not a
/// value.</remarks>
public class FocusPointKindIndependenceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOfferIsMadeAfterASessionOfEitherKindArms(bool programFocus)
    {
        var engine = new FocusSessionEngine(
            new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(), new FakeFocusLever(),
            new FakeFocusSessionRecord(), () => Noon, (_, _) => { }, programs: new FakeFocusLever());
        var kind = programFocus ? FocusSessionKind.ProgramFocus : FocusSessionKind.ScreenBreak;

        Assert.Equal(FocusArmOutcome.Armed,
                     engine.Arm(25, FocusSessionPlan.For(kind, blocksNetwork: false, dimsScreen: false,
                                                         blocksInput: false), "a test"));

        Assert.True(engine.TakeOpeningPage());
    }

    /// <summary>A reference to the kind: its type, its word helpers, a snapshot's or a record's
    /// <c>Kind</c>, or a local named for it. A platform name that merely ends in "Kind", such as the
    /// browser's resource access kind, is not one.</summary>
    private static readonly Regex KindReference =
        new(@"\bFocusSessionKinds?\b|\.Kind\b|\bkind\b", RegexOptions.CultureInvariant);

    /// <summary>The pop-out's members that decide whether the page appears and that show, run and take
    /// it down. Named, so a rename fails here rather than leaving the guard reading nothing.</summary>
    private static readonly string[] PopOutFocusPointMembers =
    [
        "OfferFocusPoint", "ShowFocusPoint", "ShowCountdown", "DropPage", "StartPage",
        "FallBackToCountdown", "OnShowFocusPoint", "OnSkipFocusPoint",
    ];

    [Fact]
    public void NoCodeDecidingOrShowingTheOfferReadsTheKind()
    {
        string popOut = RepoFiles.Read(Path.Combine("UI", "StatusWindow.xaml.cs"));
        string engine = RepoFiles.Read(Path.Combine("Services", "FocusSession.cs"));
        string service = RepoFiles.Read(Path.Combine("Services", "FocusSessionService.cs"));

        foreach (string member in PopOutFocusPointMembers)
            AssertNoKind(Member(popOut, member), $"StatusWindow.{member}");

        AssertNoKind(Member(engine, "TakeOpeningPage"), "FocusSessionEngine.TakeOpeningPage");

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

    /// <summary>One method's whole text, from its return type to its closing brace or, for an
    /// expression-bodied member, to its semicolon.</summary>
    private static string Member(string source, string name)
    {
        var signature = Regex.Match(source, $@"\b(?:void|bool)\s+{name}\s*\(");
        Assert.True(signature.Success, $"{name} was not found");

        int close = source.IndexOf(')', signature.Index);
        int brace = source.IndexOf('{', close);
        int arrow = source.IndexOf("=>", close, StringComparison.Ordinal);
        if (arrow >= 0 && (brace < 0 || arrow < brace))
            return source[signature.Index..(source.IndexOf(';', arrow) + 1)];

        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[signature.Index..(i + 1)];
        }
        throw new InvalidOperationException($"{name} has no closing brace");
    }
}
