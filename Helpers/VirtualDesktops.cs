using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FocusDesk.Helpers;

/// <summary>Which virtual desktop a window sits on, as its 1-based place in the order Windows steps
/// through. Reads only; the order list in the registry is undocumented but measured to match.</summary>
internal sealed class VirtualDesktops
{
    private const string DesktopsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    private readonly IVirtualDesktopManager _manager =
        (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"), true)!)!;

    private readonly Guid[] _order = Order();

    /// <summary>The window's desktop number, or null where Windows gives no desktop or one not in the order.</summary>
    public int? NumberOf(IntPtr window)
    {
        if (_manager.GetWindowDesktopId(window, out Guid desktop) != 0 || desktop == Guid.Empty) return null;
        int index = Array.IndexOf(_order, desktop);
        return index < 0 ? null : index + 1;
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
}
