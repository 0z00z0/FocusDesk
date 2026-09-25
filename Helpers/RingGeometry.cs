using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FocusDesk.Helpers;

/// <summary>
/// The one arc the application draws rings from, so a ring drawn anywhere reads as the same
/// instrument.
/// </summary>
/// <remarks>Angles follow the clock face — 0° is 12 o'clock and a positive sweep runs
/// clockwise.</remarks>
internal static class RingGeometry
{
    /// <summary>Where a gauge arc starts and how far round it can run.</summary>
    internal const double StartAngle = 135;

    internal const double Sweep = 270;

    /// <summary>A point on the circle at a clock-face angle.</summary>
    internal static Point At(double cx, double cy, double r, double angleDeg)
    {
        // Rotate reference frame: clock-face 0° maps to math 270° (i.e. subtract 90°).
        double rad = (angleDeg - 90) * Math.PI / 180;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    /// <summary>Circular-arc geometry, or null for a sweep of nothing — a zero-length arc still
    /// paints its round line caps as a dot.</summary>
    internal static Geometry? Arc(double cx, double cy, double r, double startDeg, double sweepDeg)
    {
        if (sweepDeg <= 0) return null;

        // A full 360° arc is degenerate in SVG/XAML — cap slightly below.
        sweepDeg = Math.Min(sweepDeg, 359.99);

        var figure = new PathFigure { StartPoint = At(cx, cy, r, startDeg), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point          = At(cx, cy, r, startDeg + sweepDeg),
            Size           = new Size(r, r),
            IsLargeArc     = sweepDeg > 180,
            SweepDirection = SweepDirection.Clockwise,
            RotationAngle  = 0,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
