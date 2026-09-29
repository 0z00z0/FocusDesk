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
/// its button arms exactly what was chosen, and the button never shows before the minute has run.
/// </summary>
/// <remarks>Behaviour where the start can be driven with a fake clock and a fake arm, source text for
/// the WinUI code-behind that wires it, which needs a display.</remarks>
public class FocusPointWindowTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

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
    public void ClosingTheWindow_DuringTheMinuteOrAfterIt_NeverArms(double secondsIn)
    {
        var elapsed = TimeSpan.FromSeconds(secondsIn);
        var start = new FocusPointStart(FocusSessionKind.ScreenBreak, 25, () => elapsed);
        var arms = new Arms();

        start.Cancel();
        elapsed = TimeSpan.FromMinutes(5);

        // A button pressed after the close, however late, arms nothing.
        Assert.False(start.CanBegin);
        Assert.Null(start.Begin(arms.Arm));
        Assert.Empty(arms.Calls);

        // Every way the window closes ends in its closed handler, which settles the start; the button's
        // handler is the only place that begins one.
        string window = Window;
        Assert.Matches(new Regex(@"Closed\s*\+=\s*OnClosed;"), window);
        Assert.Contains("_start.Cancel();", RepoFiles.Member(window, "OnClosed"));
        Assert.Single(Regex.Matches(window, @"_start\.Begin\("));
        Assert.Contains("_start.Begin(", RepoFiles.Member(window, "OnStartSession"));
    }

    [Theory]
    [InlineData(true, 25)]
    [InlineData(false, 90)]
    public void StartingTheSession_ArmsTheKindAndLengthChosenAtStart_Once(bool programFocus, int minutes)
    {
        var kind = programFocus ? FocusSessionKind.ProgramFocus : FocusSessionKind.ScreenBreak;
        var start = new FocusPointStart(kind, minutes, () => FocusPointStart.Length);
        var arms = new Arms();

        Assert.Equal(FocusArmOutcome.Armed, start.Begin(arms.Arm));
        Assert.Null(start.Begin(arms.Arm));
        Assert.Equal(new[] { (minutes, kind) }, arms.Calls);

        // The window hands both on to the same arm the pop-out's Start button used, under its own cause.
        Assert.Matches(new Regex(@"FocusSessionService\.Arm\(ActionCause\.FocusPointWindow\(\), minutes, kind\)"), Window);
    }

    [Fact]
    public void TheStartButton_DoesNotAppearBeforeTheMinuteHasRun()
    {
        // Exactly a minute, never read from anything drawn.
        Assert.Equal(TimeSpan.FromSeconds(60), FocusPointStart.Length);

        var elapsed = TimeSpan.FromSeconds(59.9);
        var start = new FocusPointStart(FocusSessionKind.ProgramFocus, 25, () => elapsed);
        var arms = new Arms();

        Assert.False(start.CanBegin);
        Assert.Null(start.Begin(arms.Arm));
        Assert.Empty(arms.Calls);

        elapsed = FocusPointStart.Length;
        Assert.True(start.CanBegin);

        // Hidden in the markup, and revealed only after the start says it may be pressed.
        var button = XDocument.Parse(RepoFiles.Read(Path.Combine("UI", "FocusPointWindow.xaml")))
            .Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == "StartSessionButton");
        Assert.Equal("Collapsed", (string?)button.Attribute("Visibility"));

        string reveal = RepoFiles.Member(Window, "OnMinuteUp");
        int check = reveal.IndexOf("if (!_start.CanBegin)", StringComparison.Ordinal);
        int shown = reveal.IndexOf("StartSessionButton.Visibility = Visibility.Visible", StringComparison.Ordinal);
        Assert.InRange(check, 0, shown - 1);
        Assert.Single(Regex.Matches(Window, @"StartSessionButton\.Visibility\s*="));
    }
}
