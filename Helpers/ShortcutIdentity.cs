using System.Runtime.InteropServices;
using System.Text;

namespace FocusDesk.Helpers;

/// <summary>What one Start menu shortcut says about itself.</summary>
/// <param name="Target">The file it starts, or empty where it names none.</param>
/// <param name="Arguments">Its command-line arguments, or empty.</param>
/// <param name="AppId">Its own application identity, or empty where it carries none. A web app's
/// shortcut carries the identity its windows carry.</param>
internal sealed record ShortcutFacts(string Target, string Arguments, string AppId);

/// <summary>
/// Reads a shortcut's target, arguments and own application identity in one load, beside
/// <see cref="ShellLink"/>.
/// </summary>
/// <remarks>
/// <para>Measured on one machine: 51 of 160 Start menu shortcuts carry an identity of their own, a
/// Brave web app among them. The identity is the shortcut's property, not something derived from the
/// arguments: Chromium shortens the app id, so the <c>--app-id</c> value cannot predict it.</para>
/// <para>Read on a single-threaded-apartment thread, like <see cref="ShellLink"/>.</para>
/// </remarks>
internal static class ShortcutIdentity
{
    private const int MaxArguments = 4096;

    /// <summary>The facts of one shortcut, or null where it cannot be read at all.</summary>
    internal static ShortcutFacts? Read(string shortcutPath)
    {
        ShellLink.IShellLinkW? link = null;
        try
        {
            link = (ShellLink.IShellLinkW)(object)new ShellLink.ShellLinkObject();
            ((ShellLink.IPersistFile)link).Load(shortcutPath, 0);

            var target = new StringBuilder(ShellLink.MaxTarget);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

            var arguments = new StringBuilder(MaxArguments);
            try { link.GetArguments(arguments, arguments.Capacity); }
            catch { arguments.Clear(); }

            return new ShortcutFacts(target.ToString(), arguments.ToString(), AppIdOf(link));
        }
        catch
        {
            // One unreadable shortcut out of a folder of them is not worth a log line per open.
            return null;
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>The link's own System.AppUserModel.ID, read through the property store the shell
    /// link object also implements.</summary>
    private static string AppIdOf(object link)
    {
        try
        {
            if (link is not ShellInterop.IPropertyStore store) return "";
            return ShellInterop.ReadString(store, ShellInterop.PKEY_AppUserModel_ID).Trim();
        }
        catch { return ""; }
    }
}
