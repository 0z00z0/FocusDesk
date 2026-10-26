using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>What a press of "Save program positions" came to.</summary>
internal enum PositionsSaveOutcome { Saved, NoOpenWindow, LayoutUnreadable, SessionMissing }

/// <summary>Saves where a named session's programs sit, for the displays connected now. Never locked:
/// it changes no restriction.</summary>
internal static class ProgramPositions
{
    /// <summary>Reads every open window of the session's programs, on every virtual desktop, and
    /// stores each program's desktop and its position at this layout. Runs on a thread of its own.</summary>
    public static Task<(PositionsSaveOutcome Outcome, int Saved)> SaveAsync(string? sessionKey)
    {
        var done = new TaskCompletionSource<(PositionsSaveOutcome, int)>();
        // The desktop manager is a shell object, so the reads go on a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try { done.SetResult(Save(sessionKey)); }
            catch (Exception ex)
            {
                AppLog.Error("ProgramPositions.Save", ex);
                done.SetResult((PositionsSaveOutcome.LayoutUnreadable, 0));
            }
        })
        { IsBackground = true, Name = "FocusDesk position save" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static (PositionsSaveOutcome, int) Save(string? sessionKey)
    {
        if (SettingsService.Read(s => NamedSessions.Find(s, sessionKey)?.Programs.ToList()) is not { } programs)
            return (PositionsSaveOutcome.SessionMissing, 0);
        if (DisplayLayout.Read() is not { } layout) return (PositionsSaveOutcome.LayoutUnreadable, 0);

        var found = Find(programs, layout);
        if (found.Count == 0) return (PositionsSaveOutcome.NoOpenWindow, 0);

        bool stored = false;
        SettingsService.Update(s =>
        {
            if (NamedSessions.Find(s, sessionKey) is not { } session) return;
            session.Programs = Apply(session.Programs, layout.LayoutKey, found);
            stored = true;
        });
        if (!stored) return (PositionsSaveOutcome.SessionMissing, 0);

        AppLog.Info($"Focus: positions saved for {found.Count} programs of session {sessionKey} at layout {layout.LayoutKey}");
        return (PositionsSaveOutcome.Saved, found.Count);
    }

    /// <summary>The first window of each program in window order, with its place and desktop.</summary>
    private static List<(FocusProgramKind Kind, string Id, WindowSpot Spot, int? Desktop)> Find(
        IReadOnlyList<FocusProgramEntry> programs, DisplayLayoutReading layout)
    {
        var context = new GateContext(programs, FocusProgramAction.Minimise, WindowsPrograms.Folder, "", [],
                                      GateContext.MachineSkipFolders(), _ => false);
        var inspector = new WindowInspector();
        VirtualDesktops? desktops = null;
        try { desktops = new VirtualDesktops(); }
        catch (Exception ex) { AppLog.Error("ProgramPositions.VirtualDesktops", ex); }

        var found = new List<(FocusProgramKind, string, WindowSpot, int?)>();
        foreach (var window in inspector.ActionableWindows())
        {
            if (inspector.Facts(window) is not { } facts || RowFor(facts, context) is not { } entry) continue;
            if (found.Any(f => FocusAllowedPrograms.Names(entry, f.Item1, f.Item2))) continue;
            if (WindowPlacement.Read(window, layout) is not { } spot) continue;
            found.Add((entry.Kind, entry.Id, spot, desktops?.NumberOf(window)));
        }
        return found;
    }

    /// <summary>The session row a window belongs to: a web app by its window's identity, otherwise by
    /// the process, or the Store app behind the shared frame.</summary>
    private static FocusProgramEntry? RowFor(WindowFacts facts, GateContext context)
    {
        if (facts.AppId.Length > 0)
        {
            if (context.Entries.FirstOrDefault(e => FocusAllowedPrograms.Names(e, FocusProgramKind.WebApp, facts.AppId)) is { } webApp)
                return webApp;
            // A web app's window runs in its browser's processes and is never the browser's row.
            if (ProgramGateRules.IsWebAppIdentity(facts.AppId)) return null;
        }
        var process = facts.Hosted is { Unreadable: false } hosted ? hosted : facts.Process;
        return ProgramGateRules.EntryFor(process, context);
    }

    /// <summary>The rows with this layout's positions replaced: a found program gets its new position
    /// and desktop, and every other row loses its position at this layout and keeps its desktop.</summary>
    internal static List<FocusProgramEntry> Apply(IEnumerable<FocusProgramEntry> rows, string layoutKey,
                                                  IReadOnlyList<(FocusProgramKind Kind, string Id, WindowSpot Spot, int? Desktop)> found) =>
        [.. rows.Select(row =>
        {
            var positions = (row.Positions ?? []).Where(p => p.Layout != layoutKey).ToList();
            int at = found.ToList().FindIndex(f => FocusAllowedPrograms.Names(row, f.Kind, f.Id));
            if (at < 0) return row with { Positions = positions };
            var (_, _, spot, desktop) = found[at];
            positions.Add(new ProgramPosition
            {
                Layout = layoutKey, Display = spot.Display, Left = spot.Left, Top = spot.Top,
                Width = spot.Width, Height = spot.Height, Maximised = spot.Maximised,
            });
            return row with { Positions = positions, Desktop = desktop ?? row.Desktop };
        })];
}
