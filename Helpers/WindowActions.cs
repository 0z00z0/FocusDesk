using System.Runtime.InteropServices;

namespace FocusDesk.Helpers;

/// <summary>What can be done to another program's window. Minimise, and nothing else. Behind an
/// interface so no test ever touches a real window.</summary>
internal interface IWindowActions
{
    /// <summary>Asks for the window to be minimised, without waiting on its program. False where
    /// Windows refused the request.</summary>
    bool Minimise(IntPtr window);
}

/// <summary>
/// Minimises a window without waiting: the request is posted to the window's own thread, so a hung
/// program cannot stall the caller.
/// </summary>
/// <remarks>
/// <para>Nothing is closed and no process is ended: no work can be lost through this. A hung program's
/// frozen image is drawn by Windows and is minimised once the program answers again.</para>
/// <para>Windows lets a process act on windows of equal or lower rights only, so an elevated FocusDesk
/// reaches an elevated program's window and an unelevated one does not. Measured unelevated between
/// two ordinary processes: the minimise was honoured and reported 6 ms later. Never measured
/// elevated.</para>
/// </remarks>
internal sealed class WindowActions : IWindowActions
{
    private const int SW_MINIMIZE = 6;

    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);

    public bool Minimise(IntPtr window)
    {
        try { return window != IntPtr.Zero && ShowWindowAsync(window, SW_MINIMIZE); }
        catch { return false; }
    }
}
