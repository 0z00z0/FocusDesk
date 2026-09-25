using Microsoft.UI.Xaml.Media;

namespace FocusDesk.Helpers;

/// <summary>
/// The cover's dial: the arc <see cref="RingGeometry"/> describes, cut into discrete marks so the
/// countdown reads as an instrument rather than as a bar.
/// </summary>
/// <remarks>Sixty marks, because sixty is what the two scales the dial carries both divide into — a
/// session's minutes, and the seconds of its final minute. Every fifth mark is longer, so the eye
/// counts in fives without reading a number.</remarks>
internal static class GaugeTicks
{
    internal const int Count = 60;

    internal const int MajorEvery = 5;

    /// <summary>How many marks a fraction lights, counted from the start of the arc. Rounded away
    /// from zero at the half, so a dial that is a hair over half lights the thirty-first mark.</summary>
    internal static int LitCount(double fraction) =>
        Math.Clamp((int)Math.Round(fraction * Count, MidpointRounding.AwayFromZero), 0, Count);

    /// <summary>The clock-face angle mark <paramref name="index"/> sits at. The first is at the arc's
    /// start and the last at its end.</summary>
    internal static double AngleOf(int index) =>
        RingGeometry.StartAngle + RingGeometry.Sweep * index / (Count - 1.0);

    /// <summary>The marks with index in <c>[from, to)</c> as one geometry, each a radial line drawn
    /// inward from <paramref name="outerRadius"/>. Null where the range is empty, which a Path takes
    /// as nothing to draw.</summary>
    internal static Geometry? Range(double cx, double cy, double outerRadius, int from, int to,
                                    double minorLength, double majorLength)
    {
        from = Math.Clamp(from, 0, Count);
        to   = Math.Clamp(to,   0, Count);
        if (to <= from) return null;

        var geometry = new PathGeometry();
        for (int i = from; i < to; i++)
        {
            double angle  = AngleOf(i);
            double length = i % MajorEvery == 0 ? majorLength : minorLength;

            var figure = new PathFigure
            {
                StartPoint = RingGeometry.At(cx, cy, outerRadius, angle),
                IsClosed   = false,
            };
            figure.Segments.Add(new LineSegment
            {
                Point = RingGeometry.At(cx, cy, outerRadius - length, angle),
            });
            geometry.Figures.Add(figure);
        }
        return geometry;
    }
}
