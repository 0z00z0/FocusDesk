using System.Globalization;
using Windows.UI;

namespace FocusDesk.Helpers;

/// <summary>The bridge between the packed colour values the palette works in and the WinUI colour
/// type. Packed bytes are what crosses the framework divide; a Color does not.</summary>
internal static class AppColors
{
    /// <summary>A packed 0xAARRGGBB value as a WinUI colour.</summary>
    internal static Color FromPacked(uint argb) => Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>An opaque colour from the <c>#rrggbb</c> form the studio palette is written in.
    /// Ordinal parsing with the invariant culture: this reads a stored value, never a typed one.</summary>
    /// <exception cref="FormatException">The text is not six hex digits behind a hash.</exception>
    internal static Color FromHex(string rrggbb)
    {
        ArgumentNullException.ThrowIfNull(rrggbb);

        string digits = rrggbb.StartsWith('#') ? rrggbb[1..] : rrggbb;
        if (digits.Length != 6 || !uint.TryParse(
                digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            throw new FormatException($"'{rrggbb}' is not a #rrggbb colour.");

        return FromPacked(0xFF000000u | rgb);
    }
}
