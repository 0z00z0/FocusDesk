using ZeroZero.Brand.Core;

namespace FocusDesk.Helpers;

/// <summary>One anchor on a colour scale: the position along the scale, as a whole percentage, and
/// the colour read exactly there.</summary>
internal readonly record struct ColourStop(int Percent, uint Argb);

/// <summary>
/// The colour a draining countdown reads as, from whole at the start to spent at the end. Packed
/// 0xAARRGGBB, so the value crosses the WinUI and GDI+ divide, neither of which shares a Color type.
/// </summary>
/// <remarks>Interpolated in Oklab: a straight sRGB blend of two muted tones drags the midpoint
/// towards grey.</remarks>
internal static class CountdownPalette
{
    /// <summary>An opaque packed 0xAARRGGBB value from a studio palette constant such as
    /// "#7fa8b8".</summary>
    internal static uint FromHex(string hex) =>
        0xFF000000u | Convert.ToUInt32(hex.TrimStart('#'), 16);

    internal const uint Ember     = 0xFFC2593F;   // spent, below the scale
    internal const uint SageGreen = 0xFF7AB88F;
    internal const uint Lavender  = 0xFF9C8FBD;   // whole

    internal static readonly uint Terracotta = FromHex(Brand.ColorTerracotta);

    /// <summary>Running down. Deep ember at the bottom through terracotta and sage to lavender at a
    /// countdown barely started.</summary>
    internal static IReadOnlyList<ColourStop> Draining { get; } =
    [
        new(10, Ember),
        new(30, Terracotta),
        new(75, SageGreen),
        new(92, Lavender),
    ];

    /// <summary>Samples <paramref name="scale"/> at <paramref name="percent"/>: the anchor's own
    /// colour exactly at an anchor, an Oklab blend between the two it falls between, and flat below
    /// the first anchor and above the last.</summary>
    internal static uint Sample(IReadOnlyList<ColourStop> scale, int percent)
    {
        ArgumentNullException.ThrowIfNull(scale);
        if (scale.Count == 0)
            throw new ArgumentException("A colour scale carries at least one anchor.", nameof(scale));

        if (percent <= scale[0].Percent)  return scale[0].Argb;
        if (percent >= scale[^1].Percent) return scale[^1].Argb;

        for (int i = 0; i < scale.Count - 1; i++)
        {
            var (from, to) = (scale[i], scale[i + 1]);
            if (percent > to.Percent) continue;

            double t = (percent - from.Percent) / (double)(to.Percent - from.Percent);
            return Oklab.Mix(from.Argb, to.Argb, t);
        }

        return scale[^1].Argb;
    }
}
