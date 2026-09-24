using FocusDesk.Helpers;
using Xunit;
using ZeroZero.Win32;

namespace FocusDesk.Tests;

/// <summary>
/// Where the tray pop-out lands. The anchoring is what makes a pop-out read as a pop-out rather than
/// as a window that opened itself, and a window placed off the work area or behind the taskbar is
/// not something a build reports.
/// </summary>
public class PopOutPlacementTests
{
    /// <summary>A 1920×1080 screen with a 40-pixel taskbar along the bottom, at 100 %.</summary>
    private static readonly NativeRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void TheWindowSitsInTheBottomRightCornerBehindItsMargin()
    {
        var rect = PopOutPlacement.BottomRight(Work, 1.0, 400);

        Assert.Equal(360, rect.Width);
        Assert.Equal(400, rect.Height);
        Assert.Equal(Work.Right - rect.Width - PopOutPlacement.EdgeMarginInUnits, rect.X);
        Assert.Equal(Work.Bottom - rect.Height - PopOutPlacement.EdgeMarginInUnits, rect.Y);
    }

    [Fact]
    public void TheWindowFollowsTheWorkAreasOwnOriginOnASecondMonitor()
    {
        var right = new NativeRect(1920, 0, 4480, 1440);

        var rect = PopOutPlacement.BottomRight(right, 1.0, 400);

        Assert.Equal(right.Right - rect.Width - PopOutPlacement.EdgeMarginInUnits, rect.X);
        Assert.Equal(right.Bottom - rect.Height - PopOutPlacement.EdgeMarginInUnits, rect.Y);
    }

    [Fact]
    public void EveryMeasurementScalesWithTheMonitor()
    {
        var rect = PopOutPlacement.BottomRight(Work, 1.5, 400);

        Assert.Equal(540, rect.Width);
        Assert.Equal(600, rect.Height);
        Assert.Equal(Work.Right - 540 - 18, rect.X);
    }

    [Theory]
    [InlineData(40, PopOutPlacement.MinHeightInUnits)]
    [InlineData(4000, PopOutPlacement.MaxHeightInUnits)]
    public void TheHeightIsHeldBetweenItsFloorAndItsCeiling(double measured, int expected) =>
        Assert.Equal(expected, PopOutPlacement.BottomRight(Work, 1.0, measured).Height);

    /// <summary>A work area shorter than the window is a small screen at a high scale. The margin is
    /// what gives way; the window stays on the work area rather than hanging off the top of it.</summary>
    [Fact]
    public void AWorkAreaSmallerThanTheWindowKeepsTheWindowOnIt()
    {
        var tiny = new NativeRect(0, 0, 300, 180);

        var rect = PopOutPlacement.BottomRight(tiny, 1.0, 400);

        Assert.Equal(tiny.Left, rect.X);
        Assert.Equal(tiny.Top, rect.Y);
    }
}
