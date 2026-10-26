using ZeroZero.Win32;

namespace FocusDesk.Helpers;

/// <summary>One display: whole rectangle and work area in physical pixels, scale where 1 is 100 %.</summary>
internal readonly record struct DisplayArea(NativeRect Bounds, NativeRect WorkArea, double Scale);

/// <summary>The Settings window as kept: top-left in physical pixels, size in device-independent units.</summary>
internal readonly record struct SettingsWindowSpot(int X, int Y, int WidthInUnits, int HeightInUnits);

/// <summary>Where the Settings window opens: the kept spot in pixels for the display it lands on, or
/// the default size at the bottom-right of the tray icon's display. Plain numbers, testable without a window.</summary>
internal static class SettingsWindowPlacement
{
    internal const int DefaultWidthInUnits = 1080;
    internal const int DefaultHeightInUnits = 1160;
    internal const int MinWidthInUnits = 600;
    internal const int MinHeightInUnits = 400;

    /// <summary>Some work area must hold this much of the title bar for the window to be dragged.</summary>
    internal const int TitleBarHeightInUnits = 32;
    internal const int GrabWidthInUnits = 120;

    /// <summary>The outer rectangle to open at, in physical pixels, always inside one work area.</summary>
    internal static NativeRect Opening(SettingsWindowSpot? kept, IReadOnlyList<DisplayArea> displays, DisplayArea home)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (kept is { } spot && Restored(spot, displays) is { } restored) return restored;
        return Default(home);
    }

    /// <summary>The default size at the bottom-right of the work area, behind the pop-out's margin.</summary>
    internal static NativeRect Default(DisplayArea home)
    {
        var work = home.WorkArea;
        int margin = Pixels(PopOutPlacement.EdgeMarginInUnits, home.Scale);
        int width  = Math.Min(Pixels(DefaultWidthInUnits, home.Scale),  Math.Max(1, work.Width  - 2 * margin));
        int height = Math.Min(Pixels(DefaultHeightInUnits, home.Scale), Math.Max(1, work.Height - 2 * margin));
        int left = Math.Max(work.Left, work.Right  - margin - width);
        int top  = Math.Max(work.Top,  work.Bottom - margin - height);
        return new NativeRect(left, top, left + width, top + height);
    }

    /// <summary>What is kept for a window rectangle, at the scale of the display holding most of it,
    /// which is the scale Windows draws it at.</summary>
    internal static SettingsWindowSpot Keep(NativeRect window, IReadOnlyList<DisplayArea> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        double scale = displays.Count == 0 ? 1.0 : displays.MaxBy(d => Overlap(window, d.Bounds)).Scale;
        return new SettingsWindowSpot(window.Left, window.Top,
            (int)Math.Round(window.Width / scale), (int)Math.Round(window.Height / scale));
    }

    /// <summary>Null where the size does not suit the display or the window could not be reached.</summary>
    private static NativeRect? Restored(SettingsWindowSpot spot, IReadOnlyList<DisplayArea> displays)
    {
        if (spot.WidthInUnits < MinWidthInUnits || spot.HeightInUnits < MinHeightInUnits) return null;

        NativeRect At(DisplayArea d) => new(spot.X, spot.Y,
            spot.X + Pixels(spot.WidthInUnits, d.Scale), spot.Y + Pixels(spot.HeightInUnits, d.Scale));

        // By share rather than area: at a higher scale the same spot is larger and would win on area alone.
        double Share(DisplayArea d) => (double)Overlap(At(d), d.Bounds) / ((long)At(d).Width * At(d).Height);
        var candidates = displays.Where(d => Share(d) > 0).ToList();
        if (candidates.Count == 0) return null;
        var display = candidates.MaxBy(Share);
        var rect = At(display);
        if (rect.Width > display.Bounds.Width || rect.Height > display.Bounds.Height) return null;

        var titleBar = new NativeRect(rect.Left, rect.Top, rect.Right, rect.Top + Pixels(TitleBarHeightInUnits, display.Scale));
        bool grabbable = displays.Any(d =>
            Intersect(titleBar, d.WorkArea) is { } held
            && held.Width >= Pixels(GrabWidthInUnits, display.Scale) && held.Height * 2 >= titleBar.Height);
        long onScreen = displays.Sum(d => Overlap(rect, d.WorkArea));
        if (!grabbable || onScreen * 2 < (long)rect.Width * rect.Height) return null;

        var work = display.WorkArea;
        return new NativeRect(rect.Left, rect.Top,
            rect.Left + Math.Min(rect.Width, work.Width), rect.Top + Math.Min(rect.Height, work.Height)).ClampInto(work);
    }

    private static int Pixels(int units, double scale) => (int)Math.Round(units * (scale > 0 ? scale : 1.0));

    private static NativeRect? Intersect(NativeRect a, NativeRect b)
    {
        int left = Math.Max(a.Left, b.Left), top = Math.Max(a.Top, b.Top);
        int right = Math.Min(a.Right, b.Right), bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top ? new NativeRect(left, top, right, bottom) : null;
    }

    private static long Overlap(NativeRect a, NativeRect b) =>
        Intersect(a, b) is { } i ? (long)i.Width * i.Height : 0;
}
