using System.ComponentModel;
using System.Runtime.InteropServices;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Windows.Graphics;

namespace FocusDesk.UI;

/// <summary>One display's see-through cover behind the starting window: a plain layered window, black
/// at 60 %, taking clicks so nothing behind it is clicked while programs start. It never activates,
/// so a click cannot lift it above the starting window.</summary>
internal sealed class StartingCoverWindow
{
    private const string ClassName = "FocusDeskStartingCover";

    /// <summary>60 % of 255.</summary>
    private const byte Alpha = 153;

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const int BLACK_BRUSH = 4;
    private const int IDC_ARROW = 32512;

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    // Held for the life of the process: the class keeps a pointer to it.
    private static readonly WindowProc _proc = Proc;
    private static bool _registered;

    private readonly IntPtr _hwnd;

    internal StartingCoverWindow(RectInt32 bounds)
    {
        Register();
        _hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
                               ClassName, "FocusDesk", WS_POPUP,
                               bounds.X, bounds.Y, bounds.Width, bounds.Height,
                               IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());

        SetLayeredWindowAttributes(_hwnd, 0, Alpha, LWA_ALPHA);
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    }

    internal bool IsGone => !NativeMethods.WindowExists(_hwnd);

    internal void KeepOnTop() => NativeMethods.RaiseToTopmost(_hwnd);

    internal void Dismiss()
    {
        if (!IsGone) DestroyWindow(_hwnd);
    }

    private static void Register()
    {
        if (_registered) return;
        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance     = GetModuleHandle(null),
            hCursor       = LoadCursor(IntPtr.Zero, IDC_ARROW),
            hbrBackground = GetStockObject(BLACK_BRUSH),
            lpszClassName = ClassName,
        };
        if (RegisterClassEx(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _registered = true;
    }

    private static IntPtr Proc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) =>
        message == WM_MOUSEACTIVATE ? MA_NOACTIVATE : DefWindowProc(window, message, wParam, lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
                                                int x, int y, int width, int height, IntPtr parent,
                                                IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colourKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern IntPtr LoadCursor(IntPtr instance, int name);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int kind);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}

/// <summary>The see-through cover over every display while the starting window is open.</summary>
internal static class StartingCover
{
    private static readonly PerDisplayWindows<StartingCoverWindow> _covers = new(
        "StartingCover", bounds => new StartingCoverWindow(bounds), cover => cover.Dismiss(), cover => cover.IsGone);

    internal static void Show()
    {
        try
        {
            _covers.Rebuild();
            AppLog.Info($"Focus: the see-through cover is over {_covers.Windows.Count} display(s).");
        }
        catch (Exception ex) { AppLog.Error("StartingCover.Show", ex); }
    }

    /// <summary>Called once a second: follows a display change and puts the covers back at the top
    /// of the topmost band, so the caller can raise itself above them.</summary>
    internal static void KeepUp()
    {
        try
        {
            if (_covers.DisplaysChanged()) _covers.Rebuild();
            foreach (var cover in _covers.Windows) cover.KeepOnTop();
        }
        catch (Exception ex) { AppLog.Error("StartingCover.KeepUp", ex); }
    }

    internal static void Hide() => _covers.Clear("StartingCover.Hide");
}
