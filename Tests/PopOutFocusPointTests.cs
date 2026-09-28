using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The pop-out's session-start focus point, read from the markup and the project that ship: the
/// countdown is what the window shows unless the page has been asked for, and the page reaches the
/// build output.
/// </summary>
/// <remarks>The embedded browser itself is not driven here. A WebView2 needs a window and a browser
/// runtime, which a unit test has neither of.</remarks>
public class PopOutFocusPointTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Named(XDocument markup, string name) =>
        markup.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);

    private static XDocument Markup => XDocument.Parse(RepoFiles.Read(Path.Combine("UI", "StatusWindow.xaml")));

    [Fact]
    public void TheCountdownIsWhatThePopOutShows_UnlessTheFocusPointIsAskedFor()
    {
        var markup = Markup;
        var session = Named(markup, "SessionView");
        var page = Named(markup, "FocusPointView");

        // The page starts hidden and the countdown starts shown, so a window that never takes the
        // offer reads exactly as it did before the page existed.
        Assert.Equal("Collapsed", (string?)page.Attribute("Visibility"));
        Assert.Null(session.Attribute("Visibility"));

        // Every part of the countdown sits inside the view that stays shown.
        foreach (string part in new[] { "StartButton", "RingFill", "StatusText", "LeverCard", "HistoryRows" })
            Assert.Contains(Named(markup, part), session.Descendants());

        // The way back is outside the page's browser and inside the page's view; the way to the page
        // is outside both views, so it is there whichever one shows.
        Assert.Contains(Named(markup, "SkipButton"), page.Descendants());
        var show = Named(markup, "FocusPointButton");
        Assert.DoesNotContain(show, page.Descendants());
        Assert.DoesNotContain(show, session.Descendants());
    }

    [Fact]
    public void ThePagesFrameHasAFixedHeight_SoTheWindowIsMeasuredFromIt()
    {
        // The window sizes itself by measuring its content, and a browser asks for no height of its
        // own: without one on its frame the page would measure as nothing and be cut off.
        var frame = Named(Markup, "FocusPointHost").Parent!;

        Assert.True(double.TryParse((string?)frame.Attribute("Height"), out double height));
        Assert.InRange(height, 220, 640);
    }

    [Fact]
    public void ThePageIsShippedBesideTheExecutable() =>
        // The browser is pointed at a folder on disk. Dropped from the build output, the page fails to
        // load and the pop-out falls back to the countdown with nothing on screen saying why.
        Assert.Matches(
            @"<Content (Include|Update)=""Assets\\FocusPoint\\session-start\.html"">\s*<CopyToOutputDirectory>PreserveNewest",
            RepoFiles.Read("FocusDesk.csproj"));
}
