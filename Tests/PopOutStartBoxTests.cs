using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The pop-out's start box offers both kinds every time and never changes the stored default kind,
/// which is what the session switch in Home Assistant starts.
/// </summary>
/// <remarks>Source text: the window is WinUI code-behind that needs a display, and what must never
/// appear in it is a write, not a value.</remarks>
public class PopOutStartBoxTests
{
    [Fact]
    public void StartingAKindFromThePopOut_NeverWritesTheStoredDefaultKind()
    {
        string popOut = RepoFiles.Read(Path.Combine("UI", "StatusWindow.xaml.cs"));

        // A write here would make the next session started from Home Assistant run as whichever kind
        // was last pressed in the pop-out.
        Assert.DoesNotMatch(new Regex(@"\.FocusSessionKind\s*="), popOut);
        // The kind goes to the session as an argument, for that one start.
        Assert.Matches(new Regex(@"FocusSessionService\.Arm\([^;]*\bkind\)"), popOut);
    }
}
