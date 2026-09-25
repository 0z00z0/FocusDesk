// Adapted from PowerToys Workspaces' IterateAppsFolder (src/modules/Workspaces/WorkspacesLib/AppUtils.cpp):
// Copyright (c) Microsoft Corporation. All rights reserved. Licensed under the MIT licence; see THIRD-PARTY-NOTICES.md.

using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace FocusDesk.Helpers;

/// <summary>One entry in the shell's Apps folder: the Start menu's own list of everything it can
/// start, Store apps and web apps included.</summary>
/// <param name="ParsingName">The entry's name inside the Apps folder, from which the shell item can be
/// made again for its icon and its name.</param>
/// <param name="Name">What the Start menu shows. Drawn and discarded; never stored.</param>
/// <param name="AppId">The entry's application identity, or empty.</param>
/// <param name="PackageFamily">The Store package family, or empty for anything not packaged.</param>
/// <param name="InstallPath">The package's install folder or the program's own path, or empty.</param>
/// <param name="SignedByWindows">Whether Windows signs the package as its own.</param>
internal sealed record AppsFolderEntry(
    string ParsingName, string Name, string AppId, string PackageFamily, string InstallPath,
    bool SignedByWindows)
{
    /// <summary>The prefix that turns a parsing name back into the shell item.</summary>
    public const string ShellPrefix = @"shell:AppsFolder\";

    /// <summary>What a row stores to find this entry again.</summary>
    public string StartEntry => ShellPrefix + ParsingName;
}

/// <summary>
/// Reads the shell's Apps folder: each entry's identity, package and install path.
/// </summary>
/// <remarks>
/// <para>Measured on one machine, unelevated: 196 entries in 0.55 s, 43 of them from 39 Store package
/// families, 4 of those families signed as part of Windows. Never measured inside the elevated
/// FocusDesk.</para>
/// <para>Properties are read by their canonical names, as PowerToys reads them, because the package
/// properties have no published key in the SDK headers.</para>
/// <para>Shell objects are apartment-threaded: read on a single-threaded-apartment thread.</para>
/// </remarks>
internal static class StartMenuApps
{
    private const string AppUserModelIdProp      = "System.AppUserModel.ID";
    private const string PackageFullNameProp     = "System.AppUserModel.PackageFullName";
    private const string PackageInstallPathProp  = "System.AppUserModel.PackageInstallPath";
    private const string TargetParsingPathProp   = "System.Link.TargetParsingPath";

    /// <summary>Every entry the Apps folder lists, or none where it cannot be read.</summary>
    /// <param name="failed">Told why the folder could not be read at all; one entry that cannot be
    /// read is skipped silently.</param>
    internal static IReadOnlyList<AppsFolderEntry> Read(Action<Exception>? failed = null)
    {
        var found = new List<AppsFolderEntry>();
        ShellInterop.IShellItem? folder = null;
        ShellInterop.IEnumShellItems? items = null;
        var signedByWindows = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Guid folderId = ShellInterop.FOLDERID_AppsFolder, itemId = ShellInterop.IID_IShellItem;
            ShellInterop.SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref itemId, out folder);

            Guid handler = ShellInterop.BHID_EnumItems, enumId = ShellInterop.IID_IEnumShellItems;
            folder.BindToHandler(IntPtr.Zero, ref handler, ref enumId, out IntPtr enumerator);
            items = ShellInterop.TakeObject<ShellInterop.IEnumShellItems>(enumerator);

            while (items.Next(1, out var item, out uint fetched) == 0 && fetched == 1)
            {
                try
                {
                    if (Entry(item, signedByWindows) is { } entry) found.Add(entry);
                }
                finally { ShellInterop.Release(item); }
            }
        }
        catch (Exception ex) { failed?.Invoke(ex); }
        finally
        {
            ShellInterop.Release(items);
            ShellInterop.Release(folder);
        }
        return found;
    }

    private static AppsFolderEntry? Entry(ShellInterop.IShellItem item, Dictionary<string, bool> signedByWindows)
    {
        string parsing = ShellInterop.DisplayName(item, ShellInterop.SIGDN_PARENTRELATIVEPARSING);
        string name = ShellInterop.DisplayName(item, ShellInterop.SIGDN_NORMALDISPLAY);
        if (parsing.Length == 0 || name.Length == 0) return null;

        string appId = "", fullName = "", installPath = "";
        var store = ShellInterop.PropertiesOf(item);
        if (store is not null)
        {
            try
            {
                store.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    store.GetAt(i, out var key);
                    string prop;
                    try
                    {
                        ShellInterop.PSGetNameFromPropertyKey(ref key, out IntPtr keyName);
                        prop = ShellInterop.TakeString(keyName);
                    }
                    catch { continue; }

                    switch (prop)
                    {
                        case AppUserModelIdProp:     appId = ShellInterop.ReadString(store, key); break;
                        case PackageFullNameProp:    fullName = ShellInterop.ReadString(store, key); break;
                        case PackageInstallPathProp:
                        case TargetParsingPathProp:
                            if (installPath.Length == 0) installPath = ShellInterop.ReadString(store, key);
                            break;
                    }
                }
            }
            catch { /* the entry keeps whatever was read before the store stopped answering */ }
            finally { ShellInterop.Release(store); }
        }

        string family = fullName.Length > 0 ? ProcessIdentity.FamilyFromFullName(fullName) : "";
        bool windows = false;
        if (family.Length > 0 && !signedByWindows.TryGetValue(family, out windows))
        {
            windows = IsSignedByWindows(fullName);
            signedByWindows[family] = windows;
        }

        return new AppsFolderEntry(parsing, name, appId.Trim(), family, installPath, windows);
    }

    /// <summary>Whether Windows signs a package as part of itself. Such a package is always usable and
    /// is never offered. A package that cannot be looked up is not assumed to be Windows'.</summary>
    internal static bool IsSignedByWindows(string packageFullName)
    {
        try
        {
            Package? package = new PackageManager().FindPackageForUser("", packageFullName);
            return package?.SignatureKind == PackageSignatureKind.System;
        }
        catch { return false; }
    }

    /// <summary>Whether Windows signs the installed package of this family as its own. False where no
    /// package of the family can be looked up.</summary>
    internal static bool IsFamilySignedByWindows(string packageFamily)
    {
        try
        {
            return new PackageManager().FindPackagesForUser("", packageFamily)
                                       .Any(p => p.SignatureKind == PackageSignatureKind.System);
        }
        catch { return false; }
    }

    /// <summary>Whether any package of this family is installed for the signed-in user.</summary>
    internal static bool IsInstalled(string packageFamily)
    {
        try { return new PackageManager().FindPackagesForUser("", packageFamily).Any(); }
        catch { return false; }
    }
}
