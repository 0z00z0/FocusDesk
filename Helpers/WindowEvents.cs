// The event set follows workspacer's WindowsManager (src/workspacer.Native/Native/WindowsManager.cs):
// Copyright (c) 2018 Rick Button. Licensed under the MIT licence; see THIRD-PARTY-NOTICES.md.

using System.Runtime.InteropServices;

namespace FocusDesk.Helpers;

/// <summary>Watches the desktop for a window a person might start working in. Behind an interface so
/// no test ever watches a real desktop.</summary>
internal interface IWindowEvents
{
    /// <summary>Starts watching; <paramref name="onWindow"/> is called on the watching thread for each
    /// window shown, brought to the front, restored or uncloaked. False where the watch could not be
    /// set up.</summary>
    bool Start(Action<IntPtr> onWindow);

    /// <summary>Whether the watching thread is alive and its hooks are in place.</summary>
    bool IsRunning { get; }

    /// <summary>Removes the watch and ends its thread.</summary>
    void Stop();
}

/// <summary>
/// The window-event watch on a thread of FocusDesk's own, out of context, desktop-wide.
/// </summary>
/// <remarks>
/// <para>Measured between two ordinary processes, unelevated: a window appearing was reported 27 ms
/// after it was shown, and a restore 9 to 27 ms after. Out-of-context hooks need no administrator
/// rights and inject nothing into another process.</para>
/// <para>FocusDesk's own windows are skipped at the source: they are always exempt, and a watch that
/// reported them would only cost a lookup per event.</para>
/// <para>The hooks belong to the thread that set them and are delivered through its message loop, so
/// the thread sets them, pumps messages, and removes them itself when told to stop.</para>
/// </remarks>
internal sealed class WindowEvents(Action<string, Exception>? failed = null) : IWindowEvents
{
    private const uint EVENT_SYSTEM_FOREGROUND   = 0x0003;
    private const uint EVENT_SYSTEM_MINIMIZEEND  = 0x0017;
    private const uint EVENT_OBJECT_SHOW         = 0x8002;
    private const uint EVENT_OBJECT_UNCLOAKED    = 0x8018;

    private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;
    private const uint WM_QUIT = 0x0012;

    /// <summary>How long a start waits for the thread to report whether its hooks took.</summary>
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(2);

    private static readonly uint[] Watched =
        [EVENT_OBJECT_SHOW, EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MINIMIZEEND, EVENT_OBJECT_UNCLOAKED];

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
                                       uint threadId, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
                                                 uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll")] private static extern int GetMessage(out MSG message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private readonly Lock _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _hooked;

    // Held for the life of the hooks: a delegate collected while Windows still holds its address
    // takes the process down at the next event.
    private WinEventProc? _proc;
    private Action<IntPtr>? _onWindow;

    public bool IsRunning
    {
        get { lock (_gate) return _thread is { IsAlive: true } && _hooked; }
    }

    public bool Start(Action<IntPtr> onWindow)
    {
        ArgumentNullException.ThrowIfNull(onWindow);
        lock (_gate)
        {
            if (_thread is { IsAlive: true } && _hooked) return true;

            _onWindow = onWindow;
            _proc = OnEvent;
            _hooked = false;

            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "FocusDesk window events" };
            _thread.Start();
            ready.Wait(StartWait);
            return _hooked;
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;
        lock (_gate)
        {
            thread = _thread;
            threadId = _threadId;
            _thread = null;
        }
        if (thread is null) return;

        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(StartWait);
    }

    private void Run(ManualResetEventSlim ready)
    {
        var hooks = new List<IntPtr>();
        try
        {
            _threadId = GetCurrentThreadId();
            foreach (uint eventType in Watched)
            {
                IntPtr hook = SetWinEventHook(eventType, eventType, IntPtr.Zero, _proc!, 0, 0,
                                              WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (hook != IntPtr.Zero) hooks.Add(hook);
            }
            _hooked = hooks.Count == Watched.Length;
            ready.Set();
            if (!_hooked) return;

            // GetMessage returns 0 on WM_QUIT and -1 on failure; either ends the watch.
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception ex) { failed?.Invoke("WindowEvents.Run", ex); }
        finally
        {
            foreach (IntPtr hook in hooks) UnhookWinEvent(hook);
            _hooked = false;
            ready.Set();
        }
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
                         uint threadId, uint time)
    {
        if (window == IntPtr.Zero || objectId != OBJID_WINDOW || childId != CHILDID_SELF) return;
        try { _onWindow?.Invoke(window); }
        catch (Exception ex) { failed?.Invoke("WindowEvents.OnEvent", ex); }
    }
}
