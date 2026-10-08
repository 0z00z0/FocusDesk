using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The focus-point window between the pop-out's Start button and the session: closing it never arms,
/// its button or its own end arms exactly what was chosen and only once, and neither comes before the
/// time has run.
/// </summary>
/// <remarks>Behaviour where the start can be driven with a fake clock and a fake arm, source text for
/// the WinUI code-behind that wires it, which needs a display.</remarks>
public class FocusPointWindowTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(FocusPointStart.DefaultSeconds);

    private static string Window => RepoFiles.Read(Path.Combine("UI", "FocusPointWindow.xaml.cs"));

    /// <summary>Records every arm asked for, and arms each one.</summary>
    private sealed class Arms
    {
        public List<(int Minutes, FocusSessionKind Kind)> Calls { get; } = [];

        public FocusArmOutcome Arm(int minutes, FocusSessionKind kind)
        {
            Calls.Add((minutes, kind));
            return FocusArmOutcome.Armed;
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(75)]
    public void ClosingTheWindow_DuringTheTimeOrAfterIt_NeverArms_EvenWhereItStartsItself(double secondsIn)
    {
        var elapsed = TimeSpan.FromSeconds(secondsIn);
        var start = new FocusPointStart(FocusSessionKind.ScreenBreak, 25, Minute, startsItself: true, () => elapsed);
        var arms = new Arms();

        start.Cancel();
        elapsed = TimeSpan.FromMinutes(5);

        // Neither the end of the time nor a button pressed after the close, however late, arms anything.
        Assert.False(start.CanBegin);
        Assert.Null(start.Finish(arms.Arm));
        Assert.Null(start.Begin(arms.Arm));
        Assert.Empty(arms.Calls);

        // Every way the window closes ends in its closed handler, which settles the start; the button's
        // handler and the end of the time are the only places that begin one.
        string window = Window;
        Assert.Matches(new Regex(@"Closed\s*\+=\s*OnClosed;"), window);
        Assert.Contains("_start.Cancel();", RepoFiles.Member(window, "OnClosed"));
        Assert.Single(Regex.Matches(window, @"_start\.Begin\b"));
        Assert.Contains("StartSession(_start.Begin)", RepoFiles.Member(window, "OnStartSession"));
        Assert.Single(Regex.Matches(window, @"_start\.Finish\b"));
        Assert.Contains("StartSession(_start.Finish)", RepoFiles.Member(window, "OnMinuteUp"));
    }

    /// <summary>A click anywhere neither closes nor settles the window; Escape and Alt+F4 are the only
    /// ways out, wired on the root that holds keyboard focus.</summary>
    [Fact]
    public void AClick_NeverClosesTheWindow_OnlyEscapeAndAltF4Do()
    {
        string[] pointerEvents = ["Tapped", "DoubleTapped", "RightTapped", "Holding", "PointerPressed", "PointerReleased"];
        var markup = XDocument.Parse(RepoFiles.Read(Path.Combine("UI", "FocusPointWindow.xaml")));
        Assert.Empty(markup.Descendants().Attributes().Where(a => pointerEvents.Contains(a.Name.LocalName)));

        string window = Window;
        Assert.DoesNotMatch(new Regex(@"\b(" + string.Join("|", pointerEvents) + @")\s*\+="), window);
        Assert.Equal(2, Regex.Matches(window, @"(?<![\w.?])Close\(\)").Count);
        Assert.Contains("Close();", RepoFiles.Member(window, "OnCloseKeyInvoked"));
        Assert.Contains("Close();", RepoFiles.Member(window, "StartSession"));
        Assert.Single(Regex.Matches(window, @"_start\.Cancel\b"));

        var root = markup.Root!.Elements().Single();
        Assert.Equal("True", (string?)root.Attribute("IsTabStop"));
        var keys = root.Descendants().Where(e => e.Name.LocalName == "KeyboardAccelerator")
            .Select(e => ((string?)e.Attribute("Modifiers"), (string?)e.Attribute("Key"), (string?)e.Attribute("Invoked")));
        Assert.Equal(new (string?, string?, string?)[] { (null, "Escape", "OnCloseKeyInvoked"), ("Menu", "F4", "OnCloseKeyInvoked") }, keys);
    }

    [Theory]
    [InlineData(true, 25)]
    [InlineData(false, 90)]
    public void StartingTheSession_ArmsTheKindAndLengthChosenAtStart_Once(bool programFocus, int minutes)
    {
        var kind = programFocus ? FocusSessionKind.ProgramFocus : FocusSessionKind.ScreenBreak;
        var start = new FocusPointStart(kind, minutes, Minute, startsItself: false, () => Minute);
        var arms = new Arms();

        Assert.Equal(FocusArmOutcome.Armed, start.Begin(arms.Arm));
        Assert.Null(start.Begin(arms.Arm));
        Assert.Equal(new[] { (minutes, kind) }, arms.Calls);

        // The window hands both on to the same arm the pop-out's Start button used, under its own cause.
        Assert.Matches(new Regex(@"FocusSessionService\.Arm\(ActionCause\.FocusPointWindow\(\), minutes, kind, _goal\)"), Window);
    }

    /// <summary>Where the window starts the session itself, the end of the time arms once and only
    /// once; where it does not, the end arms nothing and leaves it to the button.</summary>
    [Fact]
    public void StartingItself_ArmsExactlyOnceAtTheEnd()
    {
        var elapsed = TimeSpan.FromSeconds(29.9);
        var start = new FocusPointStart(FocusSessionKind.ScreenBreak, 25, TimeSpan.FromSeconds(30), startsItself: true, () => elapsed);
        var arms = new Arms();

        Assert.Null(start.Finish(arms.Arm));
        Assert.Empty(arms.Calls);

        elapsed = TimeSpan.FromSeconds(30);
        Assert.Equal(FocusArmOutcome.Armed, start.Finish(arms.Arm));
        Assert.Null(start.Finish(arms.Arm));
        Assert.Null(start.Begin(arms.Arm));
        Assert.Equal(new[] { (25, FocusSessionKind.ScreenBreak) }, arms.Calls);

        var waits = new FocusPointStart(FocusSessionKind.ScreenBreak, 25, Minute, startsItself: false, () => Minute);
        Assert.Null(waits.Finish(arms.Arm));
        Assert.True(waits.CanBegin);
        Assert.Single(arms.Calls);
    }

    [Fact]
    public void TheStartButton_DoesNotAppearBeforeTheTimeHasRun()
    {
        // A minute unless the Focus page sets another, never read from anything drawn.
        Assert.Equal(60, FocusPointStart.DefaultSeconds);
        Assert.Equal(FocusPointStart.DefaultSeconds, new AppSettings().FocusPointSeconds);

        var elapsed = TimeSpan.FromSeconds(59.9);
        var start = new FocusPointStart(FocusSessionKind.ProgramFocus, 25, Minute, startsItself: false, () => elapsed);
        var arms = new Arms();

        Assert.False(start.CanBegin);
        Assert.Null(start.Begin(arms.Arm));
        Assert.Empty(arms.Calls);

        elapsed = Minute;
        Assert.True(start.CanBegin);

        // Hidden in the markup, and revealed only after the start says it may be pressed.
        var button = XDocument.Parse(RepoFiles.Read(Path.Combine("UI", "FocusPointWindow.xaml")))
            .Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == "StartSessionButton");
        Assert.Equal("Collapsed", (string?)button.Attribute("Visibility"));

        string reveal = RepoFiles.Member(Window, "OnMinuteUp");
        int check = reveal.IndexOf("if (!_start.CanBegin)", StringComparison.Ordinal);
        int finish = reveal.IndexOf("StartSession(_start.Finish)", StringComparison.Ordinal);
        int shown = reveal.IndexOf("StartSessionButton.Visibility = Visibility.Visible", StringComparison.Ordinal);
        Assert.InRange(check, 0, finish - 1);
        Assert.InRange(check, 0, shown - 1);
        Assert.Single(Regex.Matches(Window, @"StartSessionButton\.Visibility\s*="));
    }
}
