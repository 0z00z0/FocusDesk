// The web-app test on a shortcut's target and arguments is adapted from PowerToys Command Palette's
// Win32Program.IsWebApplication (src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.Apps/Programs/Win32Program.cs):
// Copyright (c) Microsoft Corporation. Licensed under the MIT licence.

using System.Diagnostics;
using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>Which of a row's two checkboxes do anything for a program.</summary>
internal enum ProgramOffer
{
    /// <summary>"Can run" and "can use the network" both work: an ordinary program file, or Remote
    /// Desktop Connection.</summary>
    BothBoxes,

    /// <summary>"Can run" only: a Store app or a web app, which no firewall rule by path can
    /// name.</summary>
    RunOnly,

    /// <summary>"Can use the network" only: a Windows program, always usable and never
    /// limited.</summary>
    NetworkOnly,
}

/// <summary>One program the picker offers.</summary>
/// <param name="Kind">How <paramref name="Id"/> is read.</param>
/// <param name="Id">The full path, package family or web-app identity: the only thing stored.</param>
/// <param name="Name">What the row shows. Never stored anywhere.</param>
/// <param name="BrowserPath">For a web app, the browser file its shortcut starts.</param>
/// <param name="StartEntry">The shortcut or Apps-folder entry it came from, for its icon.</param>
/// <param name="Bare">Whether it came from a shortcut with no arguments, which names a program best
/// when several shortcuts reach one file.</param>
internal sealed record ProgramChoice(
    FocusProgramKind Kind, string Id, string Name, string? BrowserPath, string? StartEntry,
    ProgramOffer Offer, bool Running = false, bool Bare = true)
{
    /// <summary>The line beneath the name: the program file, the package, or the browser a web app
    /// runs in.</summary>
    public string Detail => Kind == FocusProgramKind.WebApp && BrowserPath is { Length: > 0 } browser
        ? browser
        : Id;

    /// <summary>What the row shows to mark a program that is open, or nothing. A string rather than a
    /// visibility so the row binds straight to it.</summary>
    public string OpenMark => Running ? AppText.Get("PickerOpenMark") : "";

    /// <summary>The row this choice becomes on the list: both checkboxes ticked, as every added
    /// program starts. The name is not carried.</summary>
    public FocusProgramEntry ToEntry() => new()
    {
        Kind          = Kind,
        Id            = Id,
        BrowserPath   = BrowserPath,
        StartEntry    = StartEntry,
        CanRun        = true,
        CanUseNetwork = true,
    };
}

/// <summary>One Start menu shortcut as read from disk.</summary>
internal sealed record StartShortcut(string ShortcutPath, string Target, string Arguments, string AppId,
                                     bool TargetExists);

/// <summary>
/// What the picker offers: the Start menu's shortcuts, and the Store apps and web apps in the shell's
/// Apps folder, sorted into what both checkboxes work for, what only one does, and what is hidden.
/// </summary>
/// <remarks>
/// <para>Measured on one machine: 160 shortcuts, 196 Apps-folder entries, 51 shortcuts carrying an
/// identity of their own, one web app with a Start entry. Programs open now but not in the Start menu
/// are not offered.</para>
/// <para>Everything above the machine-reading section is pure, so the classes are exercised against
/// composed entries rather than against a Start menu.</para>
/// </remarks>
internal static class ProgramCatalogue
{
    /// <summary>The folder name every packaged application's executable sits under, whether it is
    /// the installed package or the execution alias that stands in for it.</summary>
    private const string PackagedFolder = "WindowsApps";

    private const string ProxyWebApp   = "_proxy.exe";
    private const string AppIdArgument = "--app-id";

    /// <summary>What every Chromium web app's identity carries, and nothing else's.</summary>
    internal const string WebAppMarker = "_crx_";

    /// <summary>Whether a path belongs to a packaged application's own folder.</summary>
    public static bool IsPackaged(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        foreach (string segment in path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            if (segment.Equals(PackagedFolder, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>Whether a shortcut starts a web app: a Chromium proxy with an app id, or a shortcut
    /// whose own identity is a web app's.</summary>
    public static bool IsWebApp(string target, string arguments, string appId) =>
        (target.EndsWith(ProxyWebApp, StringComparison.OrdinalIgnoreCase)
         && arguments.Contains(AppIdArgument, StringComparison.OrdinalIgnoreCase))
        || appId.Contains(WebAppMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a program file is an uninstaller or a Windows Installer command, which nobody
    /// chooses to keep usable.</summary>
    public static bool IsUninstaller(string path)
    {
        string file = Path.GetFileName(path);
        return file.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>File Explorer and the management console: always usable, and a shortcut to either
    /// opens a folder or a snap-in rather than a program of its own.</summary>
    private static bool StartsShellOrConsole(string path) =>
        Path.GetFileName(path) is var file
        && (file.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            || file.Equals("mmc.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>What one shortcut offers, or null where it is hidden.</summary>
    public static ProgramChoice? FromShortcut(StartShortcut shortcut, string windowsFolder)
    {
        string name = Path.GetFileNameWithoutExtension(shortcut.ShortcutPath);
        string target = FocusAllowedPrograms.Normalise(shortcut.Target);

        // No file target (File Explorer, Control Panel, Run), or one that is not a program: a folder,
        // a help file, a web link, a document.
        if (!FocusAllowedPrograms.IsProgram(target)) return null;
        if (!shortcut.TargetExists) return null;

        if (IsWebApp(target, shortcut.Arguments, shortcut.AppId))
        {
            // Keyed on the shortcut's own identity; one without an identity cannot be matched to a
            // window and is not offered.
            string identity = FocusAllowedPrograms.NormaliseId(FocusProgramKind.WebApp, shortcut.AppId);
            return identity.Length == 0
                ? null
                : new ProgramChoice(FocusProgramKind.WebApp, identity, name, target, shortcut.ShortcutPath,
                                    ProgramOffer.RunOnly, Bare: shortcut.Arguments.Length == 0);
        }

        if (IsUninstaller(target) || StartsShellOrConsole(target)) return null;

        // An advertised Windows Installer shortcut resolves into the installer's cache, and the file
        // it returns there is the icon, not the program.
        if (WindowsPrograms.IsInside(target, Path.Combine(windowsFolder, "Installer"))) return null;

        // A packaged program is offered from the Apps folder, by its package.
        if (IsPackaged(target)) return null;

        var offer = WindowsPrograms.IsAlwaysUsable(target, windowsFolder)
            ? ProgramOffer.NetworkOnly
            : ProgramOffer.BothBoxes;
        return new ProgramChoice(FocusProgramKind.ProgramFile, target, name, null, shortcut.ShortcutPath,
                                 offer, Bare: shortcut.Arguments.Length == 0);
    }

    /// <summary>What one Apps-folder entry offers, or null. Only Store apps and web apps come from
    /// here: an ordinary program's entry repeats a Start menu shortcut.</summary>
    public static ProgramChoice? FromAppsFolder(AppsFolderEntry entry)
    {
        if (entry.AppId.Contains(WebAppMarker, StringComparison.OrdinalIgnoreCase))
        {
            string identity = FocusAllowedPrograms.NormaliseId(FocusProgramKind.WebApp, entry.AppId);
            string browser = FocusAllowedPrograms.Normalise(entry.InstallPath);
            return identity.Length == 0
                ? null
                : new ProgramChoice(FocusProgramKind.WebApp, identity, entry.Name,
                                    FocusAllowedPrograms.IsProgram(browser) ? browser : null,
                                    entry.StartEntry, ProgramOffer.RunOnly);
        }

        // A package Windows signs as its own is always usable, and neither checkbox would do anything.
        if (entry.PackageFamily.Length == 0 || entry.SignedByWindows) return null;

        return new ProgramChoice(FocusProgramKind.StorePackage, entry.PackageFamily, entry.Name, null,
                                 entry.StartEntry, ProgramOffer.RunOnly);
    }

    /// <summary>Which checkboxes work for a row already on the list.</summary>
    public static ProgramOffer OfferFor(FocusProgramEntry entry, string windowsFolder) => entry.Kind switch
    {
        FocusProgramKind.ProgramFile when WindowsPrograms.IsAlwaysUsable(entry.Id, windowsFolder)
            => ProgramOffer.NetworkOnly,
        FocusProgramKind.ProgramFile => ProgramOffer.BothBoxes,
        _                            => ProgramOffer.RunOnly,
    };

    /// <summary>Every choice as one list, one row per program: several shortcuts to one file, several
    /// entries of one package and a web app listed twice each become one row. Open programs first,
    /// then by name.</summary>
    /// <param name="runningPaths">Program files open now.</param>
    /// <param name="runningFamilies">Store package families open now.</param>
    public static IReadOnlyList<ProgramChoice> Merge(
        IEnumerable<ProgramChoice?> choices,
        IEnumerable<string>? runningPaths = null, IEnumerable<string>? runningFamilies = null)
    {
        var paths    = new HashSet<string>(runningPaths ?? [], StringComparer.OrdinalIgnoreCase);
        var families = new HashSet<string>(runningFamilies ?? [], StringComparer.OrdinalIgnoreCase);

        var merged = new List<ProgramChoice>();
        foreach (var group in choices.OfType<ProgramChoice>()
                                     .GroupBy(c => (c.Kind, Id: c.Id.ToUpperInvariant())))
        {
            // Named after a shortcut with no arguments where there is one, else the shortest name:
            // Vivaldi's profile shortcuts become one row called after the plain one.
            var named = group.OrderByDescending(c => c.Bare)
                             .ThenBy(c => c.Name.Length)
                             .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                             .First();

            bool running = group.Key.Kind switch
            {
                FocusProgramKind.ProgramFile  => paths.Contains(named.Id),
                FocusProgramKind.StorePackage => families.Contains(named.Id),
                _                             => false,
            };

            merged.Add(named with
            {
                BrowserPath = group.Select(c => c.BrowserPath).FirstOrDefault(b => b is { Length: > 0 }),
                StartEntry  = named.StartEntry ?? group.Select(c => c.StartEntry).FirstOrDefault(s => s is not null),
                Running     = running,
            });
        }

        return [.. merged.OrderByDescending(e => e.Running)
                         .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>What typing narrows the list to: every entry whose name or detail carries all of the
    /// words typed, in any order. An empty query matches everything.</summary>
    public static IReadOnlyList<ProgramChoice> Match(IReadOnlyList<ProgramChoice>? all, string? query)
    {
        if (all is null || all.Count == 0) return [];

        string[] words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries
                                                | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return all;

        return [.. all.Where(e => words.All(w =>
            e.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
            || e.Detail.Contains(w, StringComparison.CurrentCultureIgnoreCase)))];
    }

    // ── Reading the machine ─────────────────────────────────────────────────────────────────────

    /// <summary>The program files and package families of every process with a window a person would
    /// recognise, for the "open" mark. A process that cannot be read is skipped.</summary>
    public static (IReadOnlyList<string> Paths, IReadOnlyList<string> Families) Running()
    {
        var paths = new List<string>();
        var families = new List<string>();

        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception ex) { AppLog.Error("ProgramCatalogue.Running", ex); return (paths, families); }

        foreach (var process in processes)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero) continue;
                var (path, family) = ProcessIdentity.Of((uint)process.Id);
                if (path.Length > 0) paths.Add(path);
                if (family.Length > 0) families.Add(family);
            }
            catch
            {
                // A protected process refuses: one mark missing from a list, not worth a log line.
            }
            finally { process.Dispose(); }
        }

        return (paths, families);
    }

    /// <summary>Every shortcut in the two Start Menu folders, read.</summary>
    public static IReadOnlyList<StartShortcut> Shortcuts()
    {
        var found = new List<StartShortcut>();
        foreach (string root in StartMenuRoots())
            foreach (string shortcut in ShortcutFiles(root))
            {
                if (ShortcutIdentity.Read(shortcut) is not { } facts) continue;
                string target = FocusAllowedPrograms.Normalise(facts.Target);
                found.Add(new StartShortcut(shortcut, facts.Target, facts.Arguments, facts.AppId,
                                            target.Length > 0 && File.Exists(target)));
            }
        return found;
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Programs,
                                       Environment.SpecialFolder.CommonPrograms })
        {
            string root = "";
            try { root = Environment.GetFolderPath(folder); } catch { }
            if (root.Length > 0 && Directory.Exists(root)) yield return root;
        }
    }

    private static IEnumerable<string> ShortcutFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible    = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error($"ProgramCatalogue.ShortcutFiles({root})", ex);
            return [];
        }
    }

    /// <summary>Whether a row's program is on this machine. The list roams between machines, so a
    /// row can name a program installed only on another.</summary>
    public static bool IsPresent(FocusProgramEntry entry) => entry.Kind switch
    {
        FocusProgramKind.ProgramFile  => File.Exists(entry.Id),
        FocusProgramKind.StorePackage => StartMenuApps.IsInstalled(entry.Id),
        _                             => entry.StartEntry is { Length: > 0 } start
                                         && (start.StartsWith(AppsFolderEntry.ShellPrefix, StringComparison.OrdinalIgnoreCase)
                                             || File.Exists(start)),
    };

    /// <summary>Every source as one list, read on a single-threaded-apartment thread of its own, so
    /// the shell calls run in their own apartment and nothing blocks the thread drawing the
    /// window.</summary>
    public static Task<IReadOnlyList<ProgramChoice>> BuildAsync()
    {
        var done = new TaskCompletionSource<IReadOnlyList<ProgramChoice>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var clock = Stopwatch.StartNew();
            try
            {
                string windows = WindowsPrograms.Folder;
                var shortcuts = Shortcuts();
                var apps = StartMenuApps.Read(ex => AppLog.Error("StartMenuApps.Read", ex));
                var (paths, families) = Running();

                var merged = Merge(
                    shortcuts.Select(s => FromShortcut(s, windows)).Concat(apps.Select(FromAppsFolder)),
                    paths, families);
                AppLog.Info($"ProgramCatalogue built {merged.Count} programs from {shortcuts.Count} "
                          + $"shortcuts and {apps.Count} Apps-folder entries in {clock.ElapsedMilliseconds} ms");
                done.SetResult(merged);
            }
            catch (Exception ex)
            {
                AppLog.Error("ProgramCatalogue.BuildAsync", ex);
                done.SetResult([]);
            }
        })
        { IsBackground = true, Name = "FocusDesk program list" };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return done.Task;
    }
}
