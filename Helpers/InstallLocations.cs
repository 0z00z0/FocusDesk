namespace FocusDesk.Helpers;

/// <summary>Where a real install of FocusDesk lands, as opposed to a build output folder. The
/// watchdog task must never be registered against a path that stops existing the moment a
/// development build is rebuilt or deleted.</summary>
internal static class InstallLocations
{
    internal const string ExeName = "FocusDesk.exe";

    /// <summary>The folder a fresh install lands in — installer\FocusDesk.iss's
    /// <c>DefaultDirName</c>, under the per-user programs directory.</summary>
    internal const string ProductFolderName = "FocusDesk";

    /// <summary>True for the executable sitting in the installed folder. A build output, or a copy
    /// anywhere else, is not one.</summary>
    internal static bool IsInstalledExe(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return false;
        if (!string.Equals(Path.GetFileName(exe), ExeName, StringComparison.OrdinalIgnoreCase)) return false;

        string? dir = Path.GetDirectoryName(exe);
        return string.Equals(Path.GetFileName(dir), ProductFolderName, StringComparison.OrdinalIgnoreCase);
    }
}
