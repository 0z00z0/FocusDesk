using FocusDesk.Services;

namespace FocusDesk.Helpers;

/// <summary>The name a row shows, read afresh from the Start entry it was picked from. No name is
/// stored: the Start menu's own name follows a rename, and a stored one would not.</summary>
internal static class ProgramNames
{
    /// <summary>The Start menu's name for a row, or the file's own name or the identifier where the
    /// Start entry is gone.</summary>
    internal static string For(FocusProgramEntry entry)
    {
        string? start = entry.StartEntry;
        if (string.IsNullOrWhiteSpace(start)) return FocusAllowedPrograms.DisplayName(entry);

        if (start.StartsWith(AppsFolderEntry.ShellPrefix, StringComparison.OrdinalIgnoreCase))
            return ShellName(start) is { Length: > 0 } name ? name : FocusAllowedPrograms.DisplayName(entry);

        return File.Exists(start) && Path.GetFileNameWithoutExtension(start) is { Length: > 0 } shortcut
            ? shortcut
            : FocusAllowedPrograms.DisplayName(entry);
    }

    private static string ShellName(string parsingName)
    {
        ShellInterop.IShellItem? item = null;
        try
        {
            Guid itemId = ShellInterop.IID_IShellItem;
            ShellInterop.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref itemId, out item);
            return ShellInterop.DisplayName(item, ShellInterop.SIGDN_NORMALDISPLAY);
        }
        catch { return ""; }
        finally { ShellInterop.Release(item); }
    }
}
