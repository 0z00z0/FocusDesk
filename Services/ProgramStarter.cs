using System.Diagnostics;
using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>What became of one marked program at a start; codes only, worded by the starting window.</summary>
internal enum ProgramStartResult
{
    Started,
    AlreadyOpen,
    NotInStartMenu,
    FileMissing,
    DidNotStart,
}

/// <summary>Opens a session's marked programs in list order, leaving open ones alone. Nothing is ever
/// closed.</summary>
internal static class ProgramStarter
{
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Returns at once; the run goes on a thread of its own, in the apartment the shell needs.</summary>
    public static void Run(IReadOnlyList<FocusProgramEntry> marked)
    {
        if (marked.Count == 0) return;
        var thread = new Thread(() =>
        {
            try
            {
                var inspector = new WindowInspector();
                string windows = WindowsPrograms.Folder;
                var skip = GateContext.MachineSkipFolders();
                foreach (var entry in marked)
                    AppLog.Info($"Focus: starting {entry.Id} with the session: {StartOne(entry, inspector, windows, skip)}");
            }
            catch (Exception ex) { AppLog.Error("ProgramStarter.Run", ex); }
        })
        { IsBackground = true, Name = "FocusDesk program starter" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static ProgramStartResult StartOne(FocusProgramEntry entry, IWindowInspector inspector,
                                               string windows, IReadOnlyList<string> skip)
    {
        var context = new GateContext([entry], FocusProgramAction.Minimise, windows, "", [], skip, _ => false);
        if (IsOpen(entry, context, inspector)) return ProgramStartResult.AlreadyOpen;

        var (target, missing) = TargetOf(entry);
        if (target is null) return missing;

        try { ShellLaunch.Open(target); }
        catch (Exception ex)
        {
            AppLog.Error($"ProgramStarter: {entry.Id}", ex);
            return ProgramStartResult.DidNotStart;
        }

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < StartWait)
        {
            Thread.Sleep(PollInterval);
            if (IsOpen(entry, context, inspector)) return ProgramStartResult.Started;
        }
        return ProgramStartResult.DidNotStart;
    }

    /// <summary>The shortcut or Apps-folder entry to open, the file where a program file has none,
    /// or null with the reason.</summary>
    private static (string? Target, ProgramStartResult Missing) TargetOf(FocusProgramEntry entry)
    {
        bool file = entry.Kind == FocusProgramKind.ProgramFile;
        if (file && !File.Exists(entry.Id)) return (null, ProgramStartResult.FileMissing);

        if (entry.StartEntry is { Length: > 0 } start
            && (start.StartsWith(AppsFolderEntry.ShellPrefix, StringComparison.OrdinalIgnoreCase)
                    ? AppsFolderHas(start)
                    : File.Exists(start)))
            return (start, default);

        return file ? (entry.Id, default) : (null, ProgramStartResult.NotInStartMenu);
    }

    private static bool AppsFolderHas(string parsingName)
    {
        try
        {
            Guid iid = ShellInterop.IID_IShellItem;
            ShellInterop.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var item);
            ShellInterop.Release(item);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Any process of the program counts, a tray-only one included; a web app by its window.</summary>
    private static bool IsOpen(FocusProgramEntry entry, GateContext context, IWindowInspector inspector)
    {
        if (entry.Kind == FocusProgramKind.WebApp)
            return inspector.ActionableWindows().Any(w => inspector.Facts(w) is { } facts
                                                          && FocusAllowedPrograms.Same(facts.AppId, entry.Id));

        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(p =>
            {
                var (path, family) = ProcessIdentity.Of((uint)p.Id);
                return ProgramGateRules.EntryFor(new ProcessFacts((uint)p.Id, path, family), context) is not null;
            });
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }
}
