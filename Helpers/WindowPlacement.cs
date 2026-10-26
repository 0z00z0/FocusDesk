using System.Runtime.InteropServices;
using ZeroZero.Win32;

namespace FocusDesk.Helpers;

/// <summary>Where one window sits: its display, its normal rectangle in physical pixels from that
/// display's top-left corner, and whether it is maximised.</summary>
internal sealed record WindowSpot(string Display, int Left, int Top, int Width, int Height, bool Maximised);

/// <summary>Reads and sets a window's place. FocusDesk is per-monitor aware, so every rectangle is in physical pixels.</summary>
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

    /// <summary>Puts a window at a place <see cref="Read"/> gave, so the invisible frame cancels out; a
    /// display not connected gives the one the window opened on. Blocks up to about 3 seconds.</summary>
    public static bool Place(IntPtr window, WindowSpot spot, DisplayLayoutReading layout)
    {
        // Measured: placing a maximised window keeps its old restore rectangle, so it is restored first.
        if (IsZoomed(window) || IsIconic(window))
        {
            ShowWindow(window, SW_RESTORE);
            Thread.Sleep(RestorePause);
        }
        if (!GetWindowRect(window, out var opened)) return false;

        var target = TargetFor(spot, layout, opened);
        if (!SetWindowPos(window, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, SWP_NOZORDER | SWP_NOACTIVATE))
            return false;

        // Measured: a program crossing to another scale resizes itself; a second placement 2 s later
        // lands, and must come before maximising or the restore size stays wrong.
        if (!Landed(window, target) || ScaleAt(opened) != ScaleAt(ToRect(target)))
        {
            Thread.Sleep(SecondPlacementDelay);
            if (!Landed(window, target))
                SetWindowPos(window, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        if (spot.Maximised) ShowWindow(window, SW_MAXIMIZE);
        return true;
    }

    private static readonly TimeSpan RestorePause = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SecondPlacementDelay = TimeSpan.FromSeconds(2);

    /// <summary>The saved rectangle moved to its display's current origin.</summary>
    private static NativeRect TargetFor(WindowSpot spot, DisplayLayoutReading layout, RECT opened)
    {
        string? device = layout.Displays.FirstOrDefault(d => d.Key == spot.Display)?.DeviceName;
        if (device is not null && MonitorRect(device) is { } monitor)
            return new NativeRect(monitor.Left + spot.Left, monitor.Top + spot.Top,
                                  monitor.Left + spot.Left + spot.Width, monitor.Top + spot.Top + spot.Height);

        var (work, _) = MonitorMetrics.ForPoint((opened.Left + opened.Right) / 2, (opened.Top + opened.Bottom) / 2);
        return WindowFit.Fit(new NativeRect(work.Left + spot.Left, work.Top + spot.Top,
                                            work.Left + spot.Left + spot.Width, work.Top + spot.Top + spot.Height), 0, work);
    }

    private static bool Landed(IntPtr window, NativeRect target) =>
        GetWindowRect(window, out var now)
        && now.Left == target.Left && now.Top == target.Top && now.Right == target.Right && now.Bottom == target.Bottom;

    private static double ScaleAt(RECT rect) => MonitorMetrics.ForPoint((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2).Scale;

    private static RECT ToRect(NativeRect rect) => new() { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };

    private static RECT? MonitorRect(string deviceName)
    {
        RECT? found = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(monitor, ref info) || !string.Equals(info.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                return true;
            found = info.rcMonitor;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int WPF_RESTORETOMAXIMIZED = 2;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

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
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
