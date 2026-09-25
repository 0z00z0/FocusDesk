using System;
using System.Collections.Generic;
using System.Linq;
using FocusDesk.Helpers;

namespace FocusDesk.Tests;

/// <summary>A desktop made of composed windows. Nothing here lists, watches or touches a real
/// window.</summary>
internal sealed class FakeDesktop : IWindowInspector
{
    private readonly Dictionary<IntPtr, WindowFacts> _windows = [];
    private readonly HashSet<IntPtr> _minimised = [];

    public IntPtr Add(WindowFacts facts)
    {
        _windows[facts.Handle] = facts;
        return facts.Handle;
    }

    public void Restore(IntPtr window) => _minimised.Remove(window);

    public void Minimised(IntPtr window) => _minimised.Add(window);

    public IReadOnlyList<IntPtr> ActionableWindows() => [.. _windows.Keys];

    public bool IsActionable(IntPtr window) => _windows.ContainsKey(window);

    public bool IsMinimised(IntPtr window) => _minimised.Contains(window);

    public WindowFacts? Facts(IntPtr window) => _windows.GetValueOrDefault(window);
}

/// <summary>An event watch driven by the test. It can be made to die and to refuse a start.</summary>
internal sealed class FakeWindowWatch : IWindowEvents
{
    private Action<IntPtr>? _onWindow;

    public bool RefuseStart { get; set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public bool IsRunning { get; private set; }

    public bool Start(Action<IntPtr> onWindow)
    {
        Starts++;
        if (RefuseStart) return false;
        _onWindow = onWindow;
        IsRunning = true;
        return true;
    }

    public void Stop()
    {
        Stops++;
        IsRunning = false;
    }

    /// <summary>The watching thread ends without being told to.</summary>
    public void Die() => IsRunning = false;

    public void Raise(IntPtr window)
    {
        if (IsRunning) _onWindow?.Invoke(window);
    }
}

/// <summary>Records every minimise asked for, and marks the window minimised on the fake desktop, as
/// Windows would.</summary>
internal sealed class RecordedMinimises(FakeDesktop desktop) : IWindowActions
{
    public List<IntPtr> Minimised { get; } = [];

    public bool Minimise(IntPtr window)
    {
        Minimised.Add(window);
        desktop.Minimised(window);
        return true;
    }
}

/// <summary>Window facts shaped like the measured listings.</summary>
internal static class MeasuredWindows
{
    private static int _next = 0x1000;

    public const string WindowsFolder = @"C:\Windows";
    public const string FocusDeskPath = @"C:\Program Files\FocusDesk\FocusDesk.exe";

    public static ProcessFacts Process(string path, string family = "", uint id = 0) =>
        new(id == 0 ? (uint)System.Threading.Interlocked.Increment(ref _next) : id, path, family);

    public static WindowFacts Window(string path, string family = "", string appId = "",
                                     ProcessFacts? hosted = null, ProcessFacts? owner = null,
                                     bool elevated = false, params ProcessFacts[] ancestors) =>
        new(new IntPtr(System.Threading.Interlocked.Increment(ref _next)), Process(path, family), appId, hosted, owner, elevated, ancestors);
}
