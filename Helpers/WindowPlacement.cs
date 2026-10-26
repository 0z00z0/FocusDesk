using System.Runtime.InteropServices;

namespace FocusDesk.Helpers;

/// <summary>Where one window sits: its display, its normal rectangle in physical pixels from that
/// display's top-left corner, and whether it is maximised.</summary>
internal sealed record WindowSpot(string Display, int Left, int Top, int Width, int Height, bool Maximised);

/// <summary>Reads a window's place. FocusDesk is per-monitor aware, so every rectangle is in physical pixels.</summary>
internal static class WindowPlacement
{
    /// <summary>The window's place, or null where it has gone or its display has no key.</summary>
    public static WindowSpot? Read(IntPtr window, DisplayLayoutReading layout)
    {
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(window, ref placement)) return null;

        bool minimised = IsIconic(window);
        bool maximised = minimised ? (placement.flags & WPF_RESTORETOMAXIMIZED) != 0 : IsZoomed(window);

        // A normal window's own rectangle; otherwise the placement's, which is in work-area coordinates.
        RECT rect;
        IntPtr monitor;
        if (!minimised && !maximised)
        {
            if (!GetWindowRect(window, out rect)) return null;
            monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            rect = placement.normal;
            monitor = minimised ? MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST) : MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        }

        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(monitor, ref info) || layout.KeyFor(info.szDevice) is not { } display) return null;

        if (minimised || maximised)
        {
            int dx = info.rcWork.Left - info.rcMonitor.Left, dy = info.rcWork.Top - info.rcMonitor.Top;
            rect = new RECT { Left = rect.Left + dx, Top = rect.Top + dy, Right = rect.Right + dx, Bottom = rect.Bottom + dy };
        }

        return new WindowSpot(display, rect.Left - info.rcMonitor.Left, rect.Top - info.rcMonitor.Top,
                              rect.Right - rect.Left, rect.Bottom - rect.Top, maximised);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int WPF_RESTORETOMAXIMIZED = 2;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT { public int length; public int flags; public int showCmd; public POINT min; public POINT max; public RECT normal; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
}
