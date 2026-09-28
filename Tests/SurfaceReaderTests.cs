using System;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The brightness reading feeds every publish cycle, roughly every 30 seconds, for as long as the
/// application runs. A machine with no display Windows can read this from must never pay for — or
/// log against — that query: fixed against a fake, never the real WMI classes.
/// </summary>
public class SurfaceReaderTests
{
    [Fact]
    public void UnsupportedBrightness_NeverReadsTheLiveValue()
    {
        bool currentCalled = false;

        int? brightness = SurfaceReader.ReadBrightness(
            isSupported: () => false,
            current: () => { currentCalled = true; return 42; });

        Assert.Null(brightness);
        Assert.False(currentCalled);
    }

    [Fact]
    public void SupportedBrightness_ReadsTheLiveValue()
    {
        int? brightness = SurfaceReader.ReadBrightness(
            isSupported: () => true,
            current: () => 42);

        Assert.Equal(42, brightness);
    }
}
