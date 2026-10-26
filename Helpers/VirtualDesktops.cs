using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FocusDesk.Helpers;

/// <summary>A window's virtual desktop as its 1-based place in Windows' order (registry list, undocumented,
/// measured to match), and the switch a start makes by moving FocusDesk's own windows there and
/// activating one. Use an instance on a single-threaded apartment.</summary>
internal sealed class VirtualDesktops
{
    private const string DesktopsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    // Measured: a switch shows 69 to 104 ms after the activation.
    private static readonly TimeSpan FirstTry = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SwitchWait = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan HandOffPause = TimeSpan.FromMilliseconds(200);

    private static readonly Lock TravellersGate = new();
    private static readonly List<(IntPtr Window, bool HoldsForegroundLock)> Travellers = [];

    private readonly IVirtualDesktopManager _manager =
        (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"), true)!)!;

    private readonly Guid[] _order = Order();

    /// <summary>How many desktops the order holds; 0 where it cannot be read.</summary>
    public int Count => _order.Length;

    /// <summary>The window's desktop number, or null where Windows gives no desktop or one not in the order.</summary>
    public int? NumberOf(IntPtr window)
    {
        if (_manager.GetWindowDesktopId(window, out Guid desktop) != 0 || desktop == Guid.Empty) return null;
        int index = Array.IndexOf(_order, desktop);
        return index < 0 ? null : index + 1;
    }

    /// <summary>The current desktop's number, or null where the registry names none in the order.</summary>
    public int? CurrentNumber()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopsKey);
        if (key?.GetValue("CurrentVirtualDesktop") is not byte[] { Length: 16 } id) return null;
        int index = Array.IndexOf(_order, new Guid(id));
        return index < 0 ? null : index + 1;
    }

    /// <summary>A FocusDesk window shown during a start, moved along when the start switches desktop;
    /// the newest is the one activated. The see-through cover is a tool window, which Windows shows on
    /// every desktop, so it is never one of these.</summary>
    public static void Travels(IntPtr window, bool holdsForegroundLock = false)
    {
        lock (TravellersGate) Travellers.Insert(0, (window, holdsForegroundLock));
    }

    public static void Stays(IntPtr window)
    {
        lock (TravellersGate) Travellers.RemoveAll(t => t.Window == window);
    }

    public static bool AnyTraveller()
    {
        lock (TravellersGate) return Travellers.Count > 0;
    }

    /// <summary>Makes desktop <paramref name="number"/> current by taking the travelling windows there
    /// and activating the newest. True once it is current; otherwise the windows go back where they
    /// were, so they stay in sight.</summary>
    public bool Travel(int number)
    {
        (IntPtr Window, bool HoldsForegroundLock)[] windows;
        lock (TravellersGate) windows = [.. Travellers];
        if (windows.Length == 0 || number < 1 || number > _order.Length) return false;

        var from = windows.Select(t => _manager.GetWindowDesktopId(t.Window, out Guid g) == 0 ? g : Guid.Empty).ToArray();
        Guid target = _order[number - 1];
        foreach (var t in windows) _manager.MoveWindowToDesktop(t.Window, ref target);

        IntPtr lead = windows[0].Window;
        bool relock = windows.Any(t => t.HoldsForegroundLock);
        // The focus point window's foreground lock would refuse FocusDesk's own activation too.
        if (relock) LockSetForegroundWindow(LSFW_UNLOCK);
        try
        {
            // Measured: a window already in front is not activated again, so the desktop would not switch;
            // the foreground goes to the shell first, and an activation straight after that does not switch either.
            if (GetForegroundWindow() == lead && SetForegroundWindow(GetShellWindow())) Thread.Sleep(HandOffPause);
            if (SetForegroundWindow(lead) && CurrentWithin(lead, FirstTry)) return true;
            // Refused while another program holds the foreground, as at a start over MQTT. Measured: a
            // zero-distance mouse move just before the call makes Windows allow it.
            var move = new INPUT[] { new() { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_MOVE } } };
            SendInput(1, move, Marshal.SizeOf<INPUT>());
            SetForegroundWindow(lead);
            if (CurrentWithin(lead, SwitchWait)) return true;
        }
        finally
        {
            if (relock) LockSetForegroundWindow(LSFW_LOCK);
        }

        for (int i = 0; i < windows.Length; i++)
        {
            if (from[i] == Guid.Empty) continue;
            Guid back = from[i];
            _manager.MoveWindowToDesktop(windows[i].Window, ref back);
        }
        return false;
    }

    private bool CurrentWithin(IntPtr window, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (_manager.IsWindowOnCurrentVirtualDesktop(window, out int on) == 0 && on != 0) return true;
            if (clock.Elapsed >= limit) return false;
            Thread.Sleep(10);
        }
    }

    private static Guid[] Order()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DesktopsKey);
        if (key?.GetValue("VirtualDesktopIDs") is not byte[] ids) return [];
        return [.. Enumerable.Range(0, ids.Length / 16).Select(i => new Guid(ids.AsSpan(i * 16, 16)))];
    }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    private const uint LSFW_LOCK = 1;
    private const uint LSFW_UNLOCK = 2;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // MOUSEINPUT is the union's largest member, so this matches the native size.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockSetForegroundWindow(uint code);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
