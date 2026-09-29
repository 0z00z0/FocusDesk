using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The pop-out's start box: the two kind buttons only choose, the one Start button opens the
/// focus-point window and arms nothing itself, and nothing in it changes the stored default kind,
/// which is what the session switch in Home Assistant starts.
/// </summary>
/// <remarks>Source text: the window is WinUI code-behind that needs a display, and what must never
/// appear in it is a call or a write, not a value.</remarks>
public class PopOutStartBoxTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string PopOut => RepoFiles.Read(Path.Combine("UI", "StatusWindow.xaml.cs"));

    private static string? ClickOf(string name) =>
        (string?)XDocument.Parse(RepoFiles.Read(Path.Combine("UI", "StatusWindow.xaml")))
            .Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name)
            .Attribute("Click");

    [Fact]
    public void ChoosingAKind_NeverStartsASessionOrStoresAnything()
    {
        string popOut = PopOut;

        Assert.Equal("OnChooseProgramFocus", ClickOf("ProgramFocusChoice"));
        Assert.Equal("OnChooseScreenBreak", ClickOf("ScreenBreakChoice"));
        Assert.Equal("OnStart", ClickOf("StartButton"));

        // A choice that armed would start a session the moment a kind is pressed; one that stored
        // would change what Home Assistant starts next.
        foreach (string member in new[] { "OnChooseProgramFocus", "OnChooseScreenBreak", "Choose" })
        {
            string text = RepoFiles.Member(popOut, member);
            Assert.DoesNotContain("Arm(", text);
            Assert.DoesNotContain("OnStart(", text);
            Assert.DoesNotContain("FocusPointWindow.Open(", text);
            Assert.DoesNotContain("SettingsService.Update", text);
        }

        // The pop-out arms nothing: its Start button opens the focus-point window, and the session
        // starts from that window's own button.
        Assert.DoesNotContain("FocusSessionService.Arm(", popOut);
        Assert.Contains("FocusPointWindow.Open(", RepoFiles.Member(popOut, "OnStart"));
    }

    [Fact]
    public void StartingFromThePopOut_NeverWritesTheStoredDefaultKind()
    {
        string popOut = PopOut;
        string focusPoint = RepoFiles.Read(Path.Combine("UI", "FocusPointWindow.xaml.cs"));

        // A write here would make the next session started from Home Assistant run as whichever kind
        // was last chosen in the pop-out.
        Assert.DoesNotMatch(new Regex(@"\.FocusSessionKind\s*="), popOut);
        Assert.DoesNotMatch(new Regex(@"\.FocusSessionKind\s*="), focusPoint);
        // The chosen kind goes to the focus-point window as an argument, for that one start.
        Assert.Matches(new Regex(@"FocusPointWindow\.Open\(_chosenKind, minutes\)"), popOut);
    }
}
