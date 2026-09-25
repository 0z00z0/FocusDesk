namespace FocusDesk.Services;

/// <summary>
/// Which program files belong to Windows itself, and the few of them a session may limit anyway.
/// </summary>
/// <remarks>
/// <para>Anything under the Windows folder is part of Windows: the taskbar, the desktop, Start, File
/// Explorer, Task Manager, the consent and credential prompts, console windows. A session that limited
/// one of those could leave the machine unusable, so they are always usable and never limited.</para>
/// <para><see cref="Limitable"/> is the one exception, a fixed set in code. Widening it by mistake is
/// how the next Windows file stops being exempt, so a test pins it.</para>
/// </remarks>
internal static class WindowsPrograms
{
    /// <summary>The Windows files a session may limit, relative to the Windows folder. Remote Desktop
    /// Connection alone.</summary>
    public static readonly IReadOnlyList<string> Limitable = [@"System32\mstsc.exe"];

    /// <summary>The Windows folder on this machine, or empty where it cannot be read.</summary>
    public static string Folder
    {
        get
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); }
            catch { return ""; }
        }
    }

    /// <summary>Whether <paramref name="path"/> lies inside <paramref name="folder"/>, compared without
    /// case and on whole folder names, so <c>C:\Windows2</c> is not inside <c>C:\Windows</c>.</summary>
    public static bool IsInside(string? path, string? folder)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
        string root = folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether a program file is part of Windows.</summary>
    public static bool IsWindowsFile(string? path, string windowsFolder) => IsInside(path, windowsFolder);

    /// <summary>Whether a program file is one of the Windows files a session may limit.</summary>
    public static bool IsLimitable(string? path, string windowsFolder) =>
        !string.IsNullOrEmpty(path) && windowsFolder.Length > 0
        && Limitable.Any(file => string.Equals(
               path, Path.Combine(windowsFolder, file), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a program file is part of Windows and not one of the limitable few: always
    /// usable, and never limited.</summary>
    public static bool IsAlwaysUsable(string? path, string windowsFolder) =>
        IsWindowsFile(path, windowsFolder) && !IsLimitable(path, windowsFolder);
}
