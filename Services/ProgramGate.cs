using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>
/// The focus-app lever while it runs: a sweep when it arms, a check on every window event, a sweep on
/// every tick, and a limit on how often one window is minimised again.
/// </summary>
/// <remarks>
/// <para>This version minimises and does nothing else, whatever action a row stores: a stored
/// AskToClose or ForceClose — from a later version, arriving through the roaming settings file — is
/// carried out as Minimise and logged once per session. The gentlest action is the safe reading of a
/// value this version cannot carry out.</para>
/// <para>Each program is logged by its identifier, never by a window title: a title can carry the
/// name of a document or a message.</para>
/// <para>Nothing is restored when the lever lifts: nothing on the machine was changed, and a minimised
/// program stays minimised until somebody restores it.</para>
/// </remarks>
internal sealed class ProgramGate(
    IWindowInspector inspector, IWindowEvents events, IWindowActions actions,
    Func<DateTimeOffset> now, Action<string> log)
{
    /// <summary>How soon one window may be minimised again. A window that keeps restoring itself is
    /// minimised again each time, but no faster than this.</summary>
    internal static readonly TimeSpan ReminimiseInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How often a program that keeps restoring itself is written to the log.</summary>
    internal static readonly TimeSpan RepeatLogInterval = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private GateContext? _context;

    private readonly Dictionary<IntPtr, DateTimeOffset> _lastMinimised = [];
    private readonly Dictionary<string, DateTimeOffset> _repeatLogged = new(StringComparer.OrdinalIgnoreCase);
    private bool _unbuiltActionLogged;

    /// <summary>Whether the lever is armed.</summary>
    public bool IsArmed
    {
        get { lock (_gate) return _context is not null; }
    }

    /// <summary>Arms against <paramref name="context"/>: every window already open is checked at once,
    /// then window events are watched. False where the watch could not be started, which leaves
    /// nothing armed.</summary>
    public bool Arm(GateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            _context = context;
            _lastMinimised.Clear();
            _repeatLogged.Clear();
            _unbuiltActionLogged = false;
        }

        if (!events.Start(OnWindow))
        {
            Lift();
            return false;
        }

        Sweep();
        return true;
    }

    /// <summary>Called on the session's tick: starts the watch again if its thread died, then checks
    /// every window, catching a missed event or a title that arrived late. False where the watch is
    /// gone and would not start again.</summary>
    public bool Hold()
    {
        if (!IsArmed) return false;
        if (!events.IsRunning && !events.Start(OnWindow)) return false;
        Sweep();
        return true;
    }

    /// <summary>Stops the watch. Nothing is restored.</summary>
    public void Lift()
    {
        events.Stop();
        lock (_gate)
        {
            _context = null;
            _lastMinimised.Clear();
            _repeatLogged.Clear();
        }
    }

    private void OnWindow(IntPtr window)
    {
        lock (_gate)
        {
            if (_context is null || !inspector.IsActionable(window)) return;
            Check(window, _context);
        }
    }

    private void Sweep()
    {
        IReadOnlyList<IntPtr> windows;
        try { windows = inspector.ActionableWindows(); }
        catch (Exception ex) { log($"Focus: the windows could not be listed: {ex.Message}"); return; }

        lock (_gate)
        {
            if (_context is null) return;

            // A window that has gone is forgotten, so the record stays the size of what is open.
            foreach (var gone in _lastMinimised.Keys.Where(w => !windows.Contains(w)).ToList())
                _lastMinimised.Remove(gone);

            foreach (var window in windows) Check(window, _context);
        }
    }

    /// <summary>Checks one window and minimises it where the rules limit it. Called with the lock
    /// held.</summary>
    private void Check(IntPtr window, GateContext context)
    {
        if (inspector.IsMinimised(window)) return;
        if (inspector.Facts(window) is not { } facts) return;

        var verdict = ProgramGateRules.Decide(facts, context);
        if (verdict.Outcome != GateOutcome.Limited) return;

        var at = now();
        bool again = _lastMinimised.TryGetValue(window, out var last);
        if (again && at - last < ReminimiseInterval) return;

        if (verdict.Action != FocusProgramAction.Minimise && !_unbuiltActionLogged)
        {
            _unbuiltActionLogged = true;
            log($"Focus: a stored action of {verdict.Action} is carried out as Minimise, the only action "
              + "this version has");
        }

        if (!actions.Minimise(window)) return;
        _lastMinimised[window] = at;

        if (!again)
            log($"Focus: minimised a window of {verdict.Identifier}, which the session does not let run");
        else if (!_repeatLogged.TryGetValue(verdict.Identifier, out var logged) || at - logged >= RepeatLogInterval)
        {
            _repeatLogged[verdict.Identifier] = at;
            log($"Focus: {verdict.Identifier} keeps restoring its window, and it is minimised again each time");
        }
    }
}
