using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>Why a window is left alone whatever the list says.</summary>
internal enum GateExemption
{
    /// <summary>Neither the file nor the package of its process can be read: nothing the rules can
    /// name.</summary>
    Unidentifiable,

    /// <summary>FocusDesk itself: the cover, the Settings window, the watchdog.</summary>
    FocusDesk,

    /// <summary>Part of Windows: under the Windows folder and not on the limitable list, or a package
    /// Windows signs as its own.</summary>
    Windows,

    /// <summary>A product registered with Windows Security Center.</summary>
    SecuritySoftware,
}

internal enum GateOutcome
{
    /// <summary>On the list with "can run" ticked, or a dialog or helper of one that is.</summary>
    Allowed,

    /// <summary>Left alone whatever the list says.</summary>
    Exempt,

    /// <summary>Not allowed to run: its window gets the action.</summary>
    Limited,
}

/// <summary>The decision for one window.</summary>
/// <param name="Identifier">What the log names the program by: the row's identifier, or the file,
/// package or identity the window was read as. Never a window title.</param>
/// <param name="Action">What a limited window gets, as stored. This version carries out Minimise
/// whatever it holds.</param>
internal sealed record GateVerdict(
    GateOutcome Outcome, string Identifier, GateExemption? Exemption = null,
    FocusProgramAction Action = FocusProgramAction.Minimise, FocusProgramEntry? Entry = null);

/// <summary>What the rules are applied against, read once when a session arms.</summary>
/// <param name="Entries">Every row on the list, ticked or not: a row with "can run" clear still
/// decides the action for its program.</param>
/// <param name="DefaultAction">The action for a program with no row.</param>
/// <param name="OwnPath">FocusDesk's own program file.</param>
/// <param name="SecurityFolders">The folders of every product registered with Windows Security
/// Center.</param>
/// <param name="SkipFolders">Folders never taken as a program's own folder: a Program Files root,
/// ProgramData, the profile root, the Programs root. The Windows folder and everything in it is
/// skipped as well.</param>
/// <param name="SignedByWindows">Whether a package family is one Windows signs as its own.</param>
internal sealed record GateContext(
    IReadOnlyList<FocusProgramEntry> Entries, FocusProgramAction DefaultAction, string WindowsFolder,
    string OwnPath, IReadOnlyList<string> SecurityFolders, IReadOnlyList<string> SkipFolders,
    Func<string, bool> SignedByWindows)
{
    /// <summary>The folders that are never a program's own folder on this machine.</summary>
    public static IReadOnlyList<string> MachineSkipFolders()
    {
        var folders = new List<string>();
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                     Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
                     Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
                 })
        {
            try
            {
                string path = Environment.GetFolderPath(folder);
                if (path.Length > 0) folders.Add(path);
            }
            catch { }
        }
        return folders;
    }
}

/// <summary>
/// Decides, for one window, whether it is allowed, left alone, or limited — and with which action.
/// Pure: every rule is exercised against facts shaped like the measured listings.
/// </summary>
/// <remarks>
/// <para>The allow-list over windows is FocusMe's and Cold Turkey Micromanager's; folder matching,
/// for launcher stubs and in-place updates, is Cold Turkey's; the Store frame and the web-app identity
/// follow PowerToys.</para>
/// <para>Order: the Store app behind a frame stands in for the frame; then the exemptions; then a
/// dialog owned by an allowed program's window; then the match by the window's own identity, by
/// package and by program file; then a helper started by an allowed program from its own folder or
/// package. Anything else gets the lever-wide action.</para>
/// <para><b>Elevated programs are not exempt.</b> Running a program as administrator is no way past
/// the list.</para>
/// </remarks>
internal static class ProgramGateRules
{
    public static GateVerdict Decide(WindowFacts window, GateContext context)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(context);

        // A Store app drawn in the shared frame is judged by the app, not by the frame, whose file
        // sits in the Windows folder and would exempt every Store app.
        var process = window.Hosted is { Unreadable: false } hosted ? hosted : window.Process;

        if (process.Unreadable)
            return Exempt(GateExemption.Unidentifiable, window.AppId);
        if (process.Path.Length > 0 && FocusAllowedPrograms.Same(process.Path, context.OwnPath))
            return Exempt(GateExemption.FocusDesk, process.Path);
        if (IsPartOfWindows(process, context))
            return Exempt(GateExemption.Windows, Name(process));
        if (context.SecurityFolders.Any(folder => WindowsPrograms.IsInside(process.Path, folder)))
            return Exempt(GateExemption.SecuritySoftware, process.Path);

        // A dialog owned by an allowed program's window, even from another process.
        if (window.Owner is { } owner && Match(owner, context) is { CanRun: true } ownerEntry)
            return new GateVerdict(GateOutcome.Allowed, ownerEntry.Id, Entry: ownerEntry);

        // A web app's window carries its own identity and belongs to its own row. One that matches no
        // row is limited even while its browser is allowed: it runs in the browser's processes, and
        // falling through to the browser's row would let every web app in.
        if (window.AppId.Length > 0)
        {
            if (context.Entries.FirstOrDefault(e => FocusAllowedPrograms.Names(e, FocusProgramKind.WebApp, window.AppId)) is { } webApp)
                return Resolve(webApp);
            if (IsWebAppIdentity(window.AppId))
                return new GateVerdict(GateOutcome.Limited, window.AppId, Action: context.DefaultAction);
        }

        if (Match(process, context) is { } entry)
            return Resolve(entry);

        // A helper started by an allowed program, from that program's own folder or carrying its
        // package. Anything else a program starts is judged on its own, so an allowed terminal does
        // not let through what it runs.
        foreach (var ancestor in window.Ancestors)
            if (Match(ancestor, context) is { CanRun: true } parent && IsHelperOf(process, ancestor, parent, context))
                return new GateVerdict(GateOutcome.Allowed, parent.Id, Entry: parent);

        return new GateVerdict(GateOutcome.Limited, Name(process), Action: context.DefaultAction);
    }

    /// <summary>Whether an application identity is a web app's. Compared as a marker inside the whole
    /// identity, never by prefix: Chromium shortens the rest.</summary>
    public static bool IsWebAppIdentity(string appId) =>
        appId.Contains(ProgramCatalogue.WebAppMarker, StringComparison.OrdinalIgnoreCase);

    private static bool IsPartOfWindows(ProcessFacts process, GateContext context) =>
        WindowsPrograms.IsAlwaysUsable(process.Path, context.WindowsFolder)
        || (process.PackageFamily.Length > 0 && context.SignedByWindows(process.PackageFamily));

    /// <summary>The row a process belongs to: by package, then by program file or the file's own
    /// folder. Null where no row names it.</summary>
    private static FocusProgramEntry? Match(ProcessFacts process, GateContext context)
    {
        if (process.PackageFamily.Length > 0
            && context.Entries.FirstOrDefault(e => FocusAllowedPrograms.Names(e, FocusProgramKind.StorePackage, process.PackageFamily)) is { } package)
            return package;

        if (process.Path.Length == 0) return null;

        return context.Entries.FirstOrDefault(e => e.Kind == FocusProgramKind.ProgramFile
                                                   && FocusAllowedPrograms.Same(e.Id, process.Path))
            ?? context.Entries.FirstOrDefault(e => e.Kind == FocusProgramKind.ProgramFile
                                                   && OwnFolder(e.Id, context) is { } folder
                                                   && WindowsPrograms.IsInside(process.Path, folder));
    }

    /// <summary>A program file's own folder, or null where that folder is one no program owns: the
    /// Windows folder and everything in it, a Program Files root, ProgramData, the profile root, the
    /// Programs root. Remote Desktop therefore matches on its file alone.</summary>
    private static string? OwnFolder(string programPath, GateContext context)
    {
        string? folder = Path.GetDirectoryName(programPath);
        if (string.IsNullOrEmpty(folder)) return null;
        if (FocusAllowedPrograms.Same(folder.TrimEnd('\\'), context.WindowsFolder.TrimEnd('\\'))
            || WindowsPrograms.IsInside(folder, context.WindowsFolder))
            return null;
        if (context.SkipFolders.Any(skip => FocusAllowedPrograms.Same(folder.TrimEnd('\\'), skip.TrimEnd('\\'))))
            return null;
        return folder;
    }

    private static bool IsHelperOf(ProcessFacts process, ProcessFacts ancestor, FocusProgramEntry parent,
                                   GateContext context)
    {
        if (process.PackageFamily.Length > 0 && FocusAllowedPrograms.Same(process.PackageFamily, ancestor.PackageFamily))
            return true;
        return parent.Kind == FocusProgramKind.ProgramFile
               && OwnFolder(parent.Id, context) is { } folder
               && WindowsPrograms.IsInside(process.Path, folder);
    }

    private static GateVerdict Resolve(FocusProgramEntry entry) =>
        entry.CanRun
            ? new GateVerdict(GateOutcome.Allowed, entry.Id, Entry: entry)
            : new GateVerdict(GateOutcome.Limited, entry.Id, Action: entry.WhenNotAllowed, Entry: entry);

    private static GateVerdict Exempt(GateExemption why, string identifier) =>
        new(GateOutcome.Exempt, identifier, why);

    private static string Name(ProcessFacts process) =>
        process.Path.Length > 0 ? process.Path : process.PackageFamily;
}
