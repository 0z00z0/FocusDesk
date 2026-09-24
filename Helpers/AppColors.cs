using Windows.UI;

namespace FocusDesk.Helpers;

/// <summary>The bridge between the packed colour values the palette works in and the WinUI colour
/// type. Packed bytes are what crosses the framework divide; a Color does not.</summary>
internal static class AppColors
{
    /// <summary>A packed 0xAARRGGBB value as a WinUI colour.</summary>
    internal static Color FromPacked(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
