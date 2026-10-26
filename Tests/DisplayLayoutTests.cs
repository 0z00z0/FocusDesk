using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>Saved positions are found by the layout key, so a derivation that moves orphans every
/// saved position. The value is the step 11 probe's for the same displays.</summary>
public class DisplayLayoutTests
{
    [Fact]
    public void TheLayoutKeyForAGivenSetOfDisplaysNeverChanges()
    {
        var keys = DisplayLayout.Keys([
            DisplayLayout.BaseKey("ABC", "1234", "SN1", isInternal: false),
            DisplayLayout.BaseKey("ABC", "1234", "SN1", isInternal: false),
            DisplayLayout.BaseKey("LEN", "4147", "0", isInternal: true),
        ]);

        Assert.Equal(["ABC-1234-SN1-external#1", "ABC-1234-SN1-external#2", "LEN-4147-0-internal"], keys);
        Assert.Equal("980889c00f216d493bff7d1dd8a96d81a877a090290b148843bbcb70658ae813",
                     DisplayLayout.LayoutKey(keys.Reverse()));
    }
}
