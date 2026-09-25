using Windows.Graphics;
using ZeroZero.Win32;

namespace FocusDesk.Helpers;

/// <summary>Where a pop-out goes and how big it is, in physical pixels.</summary>
internal readonly record struct PopOutRect(int X, int Y, int Width, int Height);

/// <summary>
/// The arithmetic behind a tray pop-out's placement: a fixed width, a height taken from what the
/// content asked for and held between a floor and a ceiling, anchored to the bottom-right of a work
/// area with an even margin.
/// </summary>
/// <remarks>Separate from the window that uses it so the placement is exercised without a display.
/// Every measurement in and out is a physical pixel except the three in device-independent units,
/// which are named as such and scaled here.</remarks>
internal static class PopOutPlacement
{
    /// <summary>The gap between the window's edge and the work area's, in device-independent
    /// units.</summary>
    internal const int EdgeMarginInUnits = 12;

    /// <summary>The width, in device-independent units. Fixed: a pop-out that changed width with its
    /// content would move under the pointer between one open and the next.</summary>
    internal const int WidthInUnits = 360;

    /// <summary>What the measured height is held between, in device-independent units. The floor
    /// keeps a window with nothing to say from collapsing to a strip; the ceiling keeps the window
    /// on a laptop's work area. The pop-out does not scroll, so its content is kept under the
    /// ceiling by the rows it caps.</summary>
    internal const int MinHeightInUnits = 200;

    internal const int MaxHeightInUnits = 640;

    /// <summary>The rectangle for a pop-out on <paramref name="workArea"/>, whose content measured
    /// <paramref name="contentHeightInUnits"/> tall at the fixed width.</summary>
    /// <param name="scale">The monitor's scale factor, where 1 is 100 %.</param>
    /// <param name="frameInPixels">The window's outer size less its client size. Added to both
    /// dimensions, so the client area is the size the content was measured at.</param>
    internal static PopOutRect BottomRight(
        NativeRect workArea, double scale, double contentHeightInUnits,
        SizeInt32 frameInPixels = default)
    {
        double heightInUnits = Math.Clamp(contentHeightInUnits, MinHeightInUnits, MaxHeightInUnits);

        int width  = (int)Math.Ceiling(WidthInUnits      * scale) + frameInPixels.Width;
        int height = (int)Math.Ceiling(heightInUnits     * scale) + frameInPixels.Height;
        int margin = (int)Math.Ceiling(EdgeMarginInUnits * scale);

        // A work area narrower or shorter than the window puts the window at the work area's own
        // corner rather than off the screen: the margin is what gives way first.
        return new PopOutRect(
            Math.Max(workArea.Left, workArea.Right  - width  - margin),
            Math.Max(workArea.Top,  workArea.Bottom - height - margin),
            width, height);
    }
}
