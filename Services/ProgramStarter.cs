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

/// <summary>One start's programs and what became of each, read by the starting window. Results
/// arrive on the starter's thread.</summary>
internal sealed class ProgramStartRun
{
    private readonly Lock _gate = new();
    private readonly ProgramStartResult?[] _results;

    internal ProgramStartRun(IReadOnlyList<FocusProgramEntry> programs)
    {
        Programs = programs;
        _results = new ProgramStartResult?[programs.Count];
    }

    public IReadOnlyList<FocusProgramEntry> Programs { get; }

    /// <summary>Raised on the starter's thread after each program is tried.</summary>
    public event Action? Changed;

    /// <summary>One result per program in list order, null while it waits.</summary>
    public ProgramStartResult?[] Results()
    {
        lock (_gate) return (ProgramStartResult?[])_results.Clone();
    }

    internal void Record(int index, ProgramStartResult result)
    {
        lock (_gate) _results[index] = result;
        try { Changed?.Invoke(); }
        catch (Exception ex) { AppLog.Error("ProgramStartRun.Changed", ex); }
    }

    /// <summary>An open program left alone is not a failure.</summary>
    public static bool Failed(ProgramStartResult result) =>
        result is ProgramStartResult.NotInStartMenu or ProgramStartResult.FileMissing
                  or ProgramStartResult.DidNotStart;
}

/// <summary>Opens a session's marked programs in list order and places the windows it opened, leaving
/// open ones alone. Nothing is ever closed.</summary>
internal static class ProgramStarter
{
    public static readonly TimeSpan StartWait = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Returns at once with the run, or null when nothing is marked; the run goes on a thread
    /// of its own, in the apartment the shell needs.</summary>
    public static ProgramStartRun? Run(IReadOnlyList<FocusProgramEntry> marked)
    {
        if (marked.Count == 0) return null;
        var run = new ProgramStartRun(marked);
        var thread = new Thread(() =>
        {
            int next = 0;
            try
            {
                var inspector = new WindowInspector();
                string windows = WindowsPrograms.Folder;
                var skip = GateContext.MachineSkipFolders();
                var layout = marked.Any(p => p.Positions is { Count: > 0 }) ? DisplayLayout.Read() : null;
                for (; next < marked.Count; next++)
                {
                    var result = StartOne(marked[next], inspector, windows, skip, layout);
                    AppLog.Info($"Focus: starting {marked[next].Id} with the session: {result}");
                    run.Record(next, result);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("ProgramStarter.Run", ex);
                // The starting window waits for every program, so a run that broke off still ends.
                for (; next < marked.Count; next++) run.Record(next, ProgramStartResult.DidNotStart);
            }
        })
        { IsBackground = true, Name = "FocusDesk program starter" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return run;
    }

    private static ProgramStartResult StartOne(FocusProgramEntry entry, IWindowInspector inspector,
                                               string windows, IReadOnlyList<string> skip, DisplayLayoutReading? layout)
    {
        var context = new GateContext([entry], FocusProgramAction.Minimise, windows, "", [], skip, _ => false);
        if (IsOpen(entry, context, inspector)) return ProgramStartResult.AlreadyOpen;

        var (target, missing) = TargetOf(entry);
        if (target is null) return missing;

        // The limit covers the shell call too: Explorer holds the call while it shows a box.
        var clock = Stopwatch.StartNew();
        if (!OpenWithin(target, entry.Id, StartWait)) return ProgramStartResult.DidNotStart;

        while (clock.Elapsed < StartWait)
        {
            Thread.Sleep(PollInterval);
            if (!IsOpen(entry, context, inspector)) continue;
            Arrange(entry, context, inspector, layout);
            return ProgramStartResult.Started;
        }
        return ProgramStartResult.DidNotStart;
    }

    /// <summary>Puts a started program's first window at its saved place for the displays connected
    /// now; at a layout with no saved place it stays where Windows put it.</summary>
    private static void Arrange(FocusProgramEntry entry, GateContext context, IWindowInspector inspector,
                                DisplayLayoutReading? layout)
    {
        if (layout is null || entry.Positions?.FirstOrDefault(p => p.Layout == layout.LayoutKey) is not { } saved) return;

        var clock = Stopwatch.StartNew();
        while (true)
        {
            var window = inspector.ActionableWindows().FirstOrDefault(w =>
                inspector.Facts(w) is { } facts && ProgramPositions.RowFor(facts, context) is not null);
            if (window != IntPtr.Zero)
            {
                var spot = new WindowSpot(saved.Display, saved.Left, saved.Top, saved.Width, saved.Height, saved.Maximised);
                bool placed = WindowPlacement.Place(window, spot, layout);
                AppLog.Info($"Focus: {entry.Id} {(placed ? "placed at its saved position" : "window gone before placing")}");
                return;
            }
            if (clock.Elapsed >= StartWait) break;
            Thread.Sleep(PollInterval);
        }
        AppLog.Info($"Focus: no window of {entry.Id} within {StartWait.TotalSeconds:0} seconds; left unplaced");
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
        {
            // Explorer answers a shortcut whose target is gone with a box that holds the call and
            // offers to repoint the shortcut, so such a shortcut is never opened.
            if (!ShortcutTargetGone(start)) return (start, default);
            return file ? (entry.Id, default) : (null, ProgramStartResult.FileMissing);
        }

        return file ? (entry.Id, default) : (null, ProgramStartResult.NotInStartMenu);
    }

    /// <summary>True for a shortcut naming a file or folder that does not exist. A shortcut naming no
    /// file, such as an installer-advertised one, is not judged. Read without resolving, so no search
    /// and no box.</summary>
    internal static bool ShortcutTargetGone(string shortcut)
    {
        if (!shortcut.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
        string target = ShellLink.Target(shortcut);
        return target.Length > 0 && !File.Exists(target) && !Directory.Exists(target);
    }

    /// <summary>Makes the shell call on a thread of its own and waits at most the limit. A call still
    /// held is left to return by itself; nothing is closed.</summary>
    private static bool OpenWithin(string target, string id, TimeSpan limit)
    {
        bool opened = false;
        var call = new Thread(() =>
        {
            try
            {
                ShellLaunch.Open(target);
                opened = true;
            }
            catch (Exception ex) { AppLog.Error($"ProgramStarter: {id}", ex); }
        })
        { IsBackground = true, Name = "FocusDesk shell call" };
        call.SetApartmentState(ApartmentState.STA);
        call.Start();

        if (call.Join(limit)) return opened;
        AppLog.Info($"Focus: the shell call for {id} had not returned within the limit; left to finish");
        return false;
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
