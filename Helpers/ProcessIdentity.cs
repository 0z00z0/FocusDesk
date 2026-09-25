using System.Runtime.InteropServices;
using System.Text;

namespace FocusDesk.Helpers;

/// <summary>
/// What a running process is, by identifiers that do not depend on its name: the file it runs and the
/// Store package family it carries, if any.
/// </summary>
/// <remarks>Opened with the least access Windows grants for a query, which reaches most processes
/// of the same session without administrator rights; a protected process still refuses and answers
/// empty.</remarks>
internal static class ProcessIdentity
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name,
                                                          ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder? name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int PackageFamilyNameFromFullName(string fullName, ref uint length, StringBuilder? name);

    /// <summary>A process handle opened for a query, or zero where the process refuses one.</summary>
    internal static IntPtr Open(uint processId) =>
        OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

    internal static void Close(IntPtr process)
    {
        if (process != IntPtr.Zero) CloseHandle(process);
    }

    /// <summary>The full path of the file a process runs, or empty.</summary>
    internal static string PathOf(IntPtr process)
    {
        if (process == IntPtr.Zero) return "";
        var name = new StringBuilder(1024);
        uint size = (uint)name.Capacity;
        return QueryFullProcessImageNameW(process, 0, name, ref size) ? name.ToString(0, (int)size) : "";
    }

    /// <summary>The Store package family a process carries, or empty for one that carries none.</summary>
    internal static string FamilyOf(IntPtr process)
    {
        if (process == IntPtr.Zero) return "";
        uint length = 0;
        int result = GetPackageFamilyName(process, ref length, null);
        if (result == APPMODEL_ERROR_NO_PACKAGE || result != ERROR_INSUFFICIENT_BUFFER) return "";

        var name = new StringBuilder((int)length);
        return GetPackageFamilyName(process, ref length, name) == 0 ? name.ToString() : "";
    }

    /// <summary>The package family a package's full name belongs to, or empty.</summary>
    internal static string FamilyFromFullName(string fullName)
    {
        uint length = 0;
        if (PackageFamilyNameFromFullName(fullName, ref length, null) != ERROR_INSUFFICIENT_BUFFER) return "";

        var name = new StringBuilder((int)length);
        return PackageFamilyNameFromFullName(fullName, ref length, name) == 0 ? name.ToString() : "";
    }

    /// <summary>The file and package of one process by its id, both empty where it cannot be
    /// opened.</summary>
    internal static (string Path, string Family) Of(uint processId)
    {
        IntPtr process = Open(processId);
        try { return (PathOf(process), FamilyOf(process)); }
        finally { Close(process); }
    }
}
