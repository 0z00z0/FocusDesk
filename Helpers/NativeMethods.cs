using System.Runtime.InteropServices;
using FocusDesk.Services;
using Windows.Graphics;

namespace FocusDesk.Helpers;

/// <summary>Thin wrappers around the Win32 calls the screen cover and the pop-out need: every
/// attached display, the window styles a cover over one carries, a close it refuses, the frame the
/// pop-out sheds, and how long since anybody touched the machine.</summary>
internal static class NativeMethods
{
    // ── How long since somebody was at the machine ───────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>
    /// How long since keyboard or mouse input last reached THIS session. Null when the query fails,
    /// which callers must not read as zero.
    /// </summary>
    /// <remarks>
    /// The tick is 32-bit and wraps at about 49.7 days of uptime, so the subtraction is unsigned and
    /// wraps with it. The reading sees only the calling session, so a small figure is proof that
    /// somebody was at the machine and a large one is weak evidence of the opposite: input on the
    /// secure desktop, in another session, or over some remote paths never reaches it. Nothing is
    /// hooked and no keystroke is seen — only that one arrived.
    /// </remarks>
    internal static TimeSpan? SinceLastInput()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return null;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        }
        catch { return null; }
    }

    // ── Every attached display, and the window styles a cover over them needs ────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback,
                                                   IntPtr data);

    /// <summary>Every attached display's full bounds in physical pixels, including the strip the
    /// taskbar sits on — a cover takes the whole panel, not the work area. Empty when the
    /// enumeration fails or no display is attached, which callers must not read as one display at
    /// the origin.</summary>
    internal static IReadOnlyList<RectInt32> AllDisplayBounds()
    {
        var found = new List<RectInt32>();
        try
        {
            // The rect the callback is handed is the virtual-screen rectangle already; asking for
            // the monitor info as well would add a call per display and answer the same.
            bool ok = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr _, IntPtr _, ref RECT r, IntPtr _) =>
                {
                    found.Add(new RectInt32(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                    return true;
                },
                IntPtr.Zero);
            return ok ? found : [];
        }
        catch (Exception ex)
        {
            AppLog.Error("NativeMethods.AllDisplayBounds", ex);
            return [];
        }
    }

    private const int GWL_EXSTYLE = -20;

    /// <summary>Never takes focus, so whatever was being typed into carries on receiving the
    /// keyboard.</summary>
    private const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>The mouse passes through to whatever is underneath: the cover takes the screen, not
    /// the input.</summary>
    private const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Keeps the cover out of Alt-Tab and off the taskbar.</summary>
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
                                            int x, int y, int cx, int cy, uint flags);

    /// <summary>Makes <paramref name="window"/> a cover: never activated, click-through, and absent
    /// from the switcher. Applied before the window is shown — an extended style set afterwards is
    /// not reliably picked up.</summary>
    internal static void MakeClickThroughAndUnfocusable(IntPtr window)
    {
        try
        {
            long style = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(window, GWL_EXSTYLE,
                             new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW));
        }
        catch (Exception ex) { AppLog.Error("NativeMethods.MakeClickThroughAndUnfocusable", ex); }
    }

    /// <summary>Puts <paramref name="window"/> back at the top of the topmost band without
    /// activating it. Re-asserted on a tick: a window created topmost after the cover sits above it
    /// until this runs again.</summary>
    internal static void RaiseToTopmost(IntPtr window)
    {
        try { SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); }
        catch (Exception ex) { AppLog.Error("NativeMethods.RaiseToTopmost", ex); }
    }

    // ── A frameless pop-out ──────────────────────────────────────────────────────────────────────

    private const int GWL_STYLE = -16;
    private const long WS_CAPTION    = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;

    private const uint SWP_NOZORDER     = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>Strips the caption and sizing frame from <paramref name="window"/>, leaving its
    /// client area as the whole window. The frame is recalculated at once, so the next size read
    /// already reflects it.</summary>
    internal static void RemoveFrame(IntPtr window)
    {
        try
        {
            long style = GetWindowLongPtr(window, GWL_STYLE).ToInt64();
            SetWindowLongPtr(window, GWL_STYLE, new IntPtr(style & ~(WS_CAPTION | WS_THICKFRAME)));
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                         SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch (Exception ex) { AppLog.Error("NativeMethods.RemoveFrame", ex); }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    /// <summary>Whether <paramref name="window"/> still exists. A cover destroyed under the service
    /// leaves a handle that answers no, which is how a missing cover is noticed.</summary>
    internal static bool WindowExists(IntPtr window)
    {
        try { return window != IntPtr.Zero && IsWindow(window); }
        catch (Exception ex) { AppLog.Error("NativeMethods.WindowExists", ex); return true; }
    }

    // ── Refusing a close ─────────────────────────────────────────────────────────────────────────

    private const int GWLP_WNDPROC = -4;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint SC_CLOSE = 0xF060;

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr window, uint message,
                                                IntPtr wParam, IntPtr lParam);

    /// <summary>A window whose close requests are dropped until <see cref="ClosingRefusal.Allow"/>
    /// is called. Holds the replacement procedure alive: a delegate collected while Windows still
    /// holds its address takes the process down at the next message.</summary>
    internal sealed class ClosingRefusal
    {
        private readonly WindowProc _proc;
        private readonly IntPtr _previous;

        internal ClosingRefusal(IntPtr window)
        {
            _proc = Filter;
            _previous = SetWindowLongPtr(window, GWLP_WNDPROC,
                                         Marshal.GetFunctionPointerForDelegate(_proc));
        }

        /// <summary>True once the window may close — the session's own teardown, and nothing
        /// else.</summary>
        internal bool Allowed { get; private set; }

        internal void Allow() => Allowed = true;

        private IntPtr Filter(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        {
            // Alt+F4, the switcher's close and an ordinary End task all arrive as one of these two.
            bool closing = message == WM_CLOSE
                        || (message == WM_SYSCOMMAND && ((long)wParam & 0xFFF0) == SC_CLOSE);
            if (closing && !Allowed) return IntPtr.Zero;
            return CallWindowProc(_previous, window, message, wParam, lParam);
        }
    }

    /// <summary>Makes <paramref name="window"/> refuse every close request. Returns the refusal, so
    /// the one teardown that is meant to work can lift it first.</summary>
    internal static ClosingRefusal? RefuseClose(IntPtr window)
    {
        try { return new ClosingRefusal(window); }
        catch (Exception ex) { AppLog.Error("NativeMethods.RefuseClose", ex); return null; }
    }
}
