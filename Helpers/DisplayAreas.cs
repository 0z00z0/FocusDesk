using System.Runtime.InteropServices;
using ZeroZero.Win32;

namespace FocusDesk.Helpers;

/// <summary>Every connected display's rectangles and scale, and which one a window from the tray belongs on.</summary>
internal static class DisplayAreas
{
    /// <summary>Every display, in physical pixels; empty only where Windows answers nothing.</summary>
    public static IReadOnlyList<DisplayArea> All()
    {
        var found = new List<(DisplayArea Area, bool Primary)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            if (Read(monitor) is { } display) found.Add(display);
            return true;
        }, IntPtr.Zero);
        return found.OrderByDescending(d => d.Primary).Select(d => d.Area).ToList();
    }

    /// <summary>The display holding the tray icon; failing that the cursor's, then the primary.</summary>
    public static DisplayArea Home(IReadOnlyList<DisplayArea> displays, Guid? trayIcon)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Count == 0)
        {
            var (work, scale) = MonitorMetrics.ForCursor();
            return new DisplayArea(work, work, scale);
        }

        if (trayIcon is { } icon && IconCentre(icon) is { X: var x, Y: var y } && Holding(displays, x, y) is { } tray) return tray;
        if (GetCursorPos(out var cursor) && Holding(displays, cursor.X, cursor.Y) is { } underCursor) return underCursor;
        return displays[0];
    }

    private static DisplayArea? Holding(IReadOnlyList<DisplayArea> displays, int x, int y) =>
        displays.Where(d => x >= d.Bounds.Left && x < d.Bounds.Right && y >= d.Bounds.Top && y < d.Bounds.Bottom)
                .Select(d => (DisplayArea?)d).FirstOrDefault();

    private static (int X, int Y)? IconCentre(Guid icon)
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), guidItem = icon };
        return Shell_NotifyIconGetRect(ref id, out var rect) == 0
            ? ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)
            : null;
    }

    private static (DisplayArea, bool)? Read(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        return (new DisplayArea(ToNative(info.rcMonitor), ToNative(info.rcWork), scale), (info.dwFlags & 1) != 0);
    }

    private static NativeRect ToNative(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);
}
