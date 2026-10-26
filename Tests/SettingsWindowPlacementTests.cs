using FocusDesk.Helpers;
using Xunit;
using ZeroZero.Win32;

namespace FocusDesk.Tests;

/// <summary>A Settings window opened off every display cannot be reached to move it back.</summary>
public class SettingsWindowPlacementTests
{
    [Fact]
    public void ASpotOffEveryDisplayOpensAtTheDefaultInsideTheTrayDisplay()
    {
        var primary = new DisplayArea(new NativeRect(0, 0, 3440, 1440), new NativeRect(0, 0, 3440, 1392), 1.0);
        var right   = new DisplayArea(new NativeRect(3440, 0, 7280, 2400), new NativeRect(3440, 0, 7280, 2304), 2.0);

        var rect = SettingsWindowPlacement.Opening(new SettingsWindowSpot(9000, 3000, 1080, 1160), [primary, right], right);

        Assert.Equal(SettingsWindowPlacement.Default(right), rect);
        Assert.True(rect.Left >= 3440 && rect.Right <= 7280 && rect.Top >= 0 && rect.Bottom <= 2304);
        Assert.Equal(2160, rect.Width);
    }
}
