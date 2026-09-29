using System;
using System.IO;
using System.Text.Json;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The cover's two visuals: which one a session draws, how brightly it draws, what the dial reads,
/// and the message the bundled page is fed. Not what any of it looks like — nothing here renders.
/// </summary>
public class CoverVisualTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static FocusSnapshot Session(int minutes, bool dims) =>
        new(FocusSessionStage.Active, Noon, Noon.AddMinutes(minutes), dims, CoversScreen: true);

    private static string Message(FocusCoverReading reading, bool screenIsDimmed) =>
        CoverSessionMessage.Compose(reading,
                                    CoverAppearance.For(CoverVisual.FocusPoint, screenIsDimmed),
                                    "hint line", "done line");

    // ── Which visual ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("focus-point", true)]
    [InlineData("FOCUS-POINT", true)]
    [InlineData("  focus-point  ", true)]
    [InlineData("ring", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a visual this build does not have", false)]
    public void AStoredNameChoosesAVisual_AndAnythingUnknownDrawsTheDial(string? stored,
                                                                         bool focusPoint) =>
        // The dial needs nothing installed, so it is what an unreadable choice falls back to.
        Assert.Equal(focusPoint ? CoverVisual.FocusPoint : CoverVisual.Ring,
                     CoverAppearance.ParseVisual(stored));

    [Fact]
    public void TheStoredNamesNeverChange()
    {
        // Written into every settings document. A renamed one silently resets the choice to the dial
        // on every installation that made it.
        Assert.Equal("ring", CoverAppearance.NameOf(CoverVisual.Ring));
        Assert.Equal("focus-point", CoverAppearance.NameOf(CoverVisual.FocusPoint));
    }

    [Fact]
    public void TheChoiceReachesTheDocumentAsAName()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"focusdesk-visual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "settings.json");
            Assert.True(SettingsService.WriteTo(
                new AppSettings { FocusCoverVisual = CoverVisual.FocusPoint }, file));

            Assert.Contains("\"focus-point\"", File.ReadAllText(file), StringComparison.Ordinal);
            Assert.Equal(CoverVisual.FocusPoint, SettingsService.ReadFrom(file)!.FocusCoverVisual);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ } }
    }

    // ── How brightly ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ACoverOverADimmedScreenDrawsAtFullStrength()
    {
        var appearance = CoverAppearance.For(CoverVisual.Ring, screenIsDimmed: true);

        Assert.Equal(CoverAppearance.FullIntensity, appearance.Intensity);
        Assert.False(appearance.IsHeldBack);
    }

    [Fact]
    public void ACoverOverAScreenNobodyDimmedHoldsItselfBack()
    {
        // The whole point of the decision: the cover must not be the brightest thing on a panel the
        // session was never asked to change.
        var appearance = CoverAppearance.For(CoverVisual.Ring, screenIsDimmed: false);

        Assert.True(appearance.IsHeldBack);
        Assert.InRange(appearance.Intensity, 0.2, 0.9);
    }

    // ── What the dial reads ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, GaugeTicks.Count)]
    [InlineData(0.5, GaugeTicks.Count / 2)]
    [InlineData(2.0, GaugeTicks.Count)]
    [InlineData(-1.0, 0)]
    public void TheDialLightsOneMarkPerSixtiethOfWhatIsLeft(double fraction, int expected) =>
        Assert.Equal(expected, GaugeTicks.LitCount(fraction));

    [Fact]
    public void AnHourLongSessionEntersItsFinalStretchWithAMinuteLeft()
    {
        var session = Session(60, dims: true);

        Assert.False(FocusCoverCountdown.For(session, Noon.AddMinutes(58.5))!.Value.IsFinalStretch);
        Assert.True(FocusCoverCountdown.For(session, Noon.AddMinutes(59.5))!.Value.IsFinalStretch);
    }

    [Fact]
    public void TheDialRescalesInTheFinalStretchSoOneMarkIsOneSecond()
    {
        // Thirty seconds left of an hour is half a percent of the session and half of the last
        // minute. The dial reads the second of those, which is the whole point of the rescale.
        var reading = FocusCoverCountdown.For(Session(60, dims: true), Noon.AddSeconds(3570))!.Value;

        Assert.Equal(0.5, reading.DialFraction, 3);
        Assert.Equal(GaugeTicks.Count / 2, GaugeTicks.LitCount(reading.DialFraction));
        Assert.InRange(reading.FractionLeft, 0.008, 0.009);
    }

    // ── What the page is told ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ThePageIsToldTheSessionsLengthWhatIsLeftAndTheTextToShow()
    {
        var reading = FocusCoverCountdown.For(Session(60, dims: true), Noon.AddMinutes(15))!.Value;

        using var message = JsonDocument.Parse(Message(reading, screenIsDimmed: true));
        var root = message.RootElement;

        Assert.Equal(CoverSessionMessage.Type, root.GetProperty("type").GetString());
        Assert.Equal(3600, root.GetProperty("totalSeconds").GetDouble());
        Assert.Equal(2700, root.GetProperty("remainingSeconds").GetDouble());
        Assert.False(root.GetProperty("dim").GetBoolean());
        // The page's own text is Norwegian; inside the cover it shows the interface language's.
        Assert.Equal("hint line", root.GetProperty("hintText").GetString());
        Assert.Equal("done line", root.GetProperty("doneText").GetString());
    }

    [Fact]
    public void ThePageIsAskedForItsQuieterPaletteWhenTheScreenIsNotDimmed()
    {
        var reading = FocusCoverCountdown.For(Session(60, dims: false), Noon)!.Value;

        using var message = JsonDocument.Parse(Message(reading, screenIsDimmed: false));

        Assert.True(message.RootElement.GetProperty("dim").GetBoolean());
    }

    [Fact]
    public void ARecordWithNoLengthStillGivesThePageSomethingToDivideBy()
    {
        // A session record written before the start time was kept reads as no length at all. Sent as
        // it stands the page would divide by zero; it is given a length equal to what is left.
        var session = new FocusSnapshot(FocusSessionStage.Active, null, Noon.AddMinutes(20),
                                        DimsScreen: true, CoversScreen: true);
        var reading = FocusCoverCountdown.For(session, Noon)!.Value;

        using var message = JsonDocument.Parse(Message(reading, screenIsDimmed: true));
        var root = message.RootElement;

        Assert.Equal(1200, root.GetProperty("totalSeconds").GetDouble());
        Assert.Equal(1200, root.GetProperty("remainingSeconds").GetDouble());
    }

    // ── The two ends of the contract ────────────────────────────────────────────────────────────

    private static string Page => RepoFiles.Read(Path.Combine("Assets", "FocusPoint", "focus-point.html"));

    [Theory]
    [InlineData("'focus-session'")]
    [InlineData("data.totalSeconds")]
    [InlineData("data.remainingSeconds")]
    [InlineData("data.dim")]
    [InlineData("data.hintText")]
    [InlineData("data.doneText")]
    public void ThePageReadsEveryNameTheCoverWrites(string read) =>
        // The contract is names spread across a C# file and an HTML file, with nothing in the
        // compiler to tie them together. Renaming one alone leaves a page that counts down to
        // nothing and says why nowhere.
        Assert.Contains(read, Page, StringComparison.Ordinal);

    [Fact]
    public void ThePageStillRunsOnItsOwnWithNoHostDrivingIt() =>
        // Opened straight from the repository it is a sixty-second breathing exercise, and that is
        // how it is read and changed. A bridge that replaced the loop instead of adding to it would
        // leave a file nobody can look at without building the application.
        Assert.Contains("const DURATION = 60;", Page, StringComparison.Ordinal);

    [Fact]
    public void ThePageTakesTheSessionFromEitherRoute()
    {
        // A browser posts to the window; the embedded browser delivers on its own channel. The page
        // has to listen on both or it works in exactly one of the two places it is used.
        Assert.Contains("window.addEventListener('message'", Page, StringComparison.Ordinal);
        Assert.Contains("window.chrome.webview.addEventListener('message'", Page,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageIsShippedBesideTheExecutable() =>
        // The embedded browser is pointed at a folder on disk. Dropped from the build output, the
        // focus-point visual becomes the dial and nothing on screen says the file is missing.
        Assert.Matches(
            @"<Content (Include|Update)=""Assets\\FocusPoint\\focus-point\.html"">\s*<CopyToOutputDirectory>PreserveNewest",
            RepoFiles.Read("FocusDesk.csproj"));
}
