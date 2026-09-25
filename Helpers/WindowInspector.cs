// The application-window test adapts workspacer's Win32Helper.IsAltTabWindow (src/workspacer.Native/Win32Helper.cs):
// Copyright (c) 2018 Rick Button. Licensed under the MIT licence; see THIRD-PARTY-NOTICES.md.
// The Store-frame fallback adapts PowerToys' get_process_path (src/common/utils/process_path.h):
// Copyright (c) Microsoft Corporation. All rights reserved. Licensed under the MIT licence; see THIRD-PARTY-NOTICES.md.

using System.Runtime.InteropServices;

namespace FocusDesk.Helpers;

/// <summary>One process as the rules read it.</summary>
/// <param name="Path">The file it runs, or empty where it cannot be read.</param>
/// <param name="PackageFamily">The Store package family it carries, or empty.</param>
internal sealed record ProcessFacts(uint ProcessId, string Path, string PackageFamily)
{
    /// <summary>Whether nothing about the process could be read: no file and no package.</summary>
    public bool Unreadable => Path.Length == 0 && PackageFamily.Length == 0;
}

/// <summary>Everything the rules need about one window, read once.</summary>
/// <param name="Process">The process that owns the window.</param>
/// <param name="AppId">The window's own application identity, or empty.</param>
/// <param name="Hosted">For the shared Store frame, the Store app it draws, or null.</param>
/// <param name="Owner">The process owning this window's owner window, or null for an unowned
/// window.</param>
/// <param name="Ancestors">The process that started this one, and the one before it, while each
/// still runs and started before its child.</param>
internal sealed record WindowFacts(
    IntPtr Handle, ProcessFacts Process, string AppId, ProcessFacts? Hosted, ProcessFacts? Owner,
    bool Elevated, IReadOnlyList<ProcessFacts> Ancestors);

/// <summary>Lists the windows a person could work in and reads the facts about one. Behind an
/// interface so no test ever lists a real window.</summary>
internal interface IWindowInspector
{
    /// <summary>Every window on this desktop a person could work in.</summary>
    IReadOnlyList<IntPtr> ActionableWindows();

    /// <summary>Whether one window is a window a person could work in.</summary>
    bool IsActionable(IntPtr window);

    /// <summary>Whether one window is minimised already, so it is not minimised again.</summary>
    bool IsMinimised(IntPtr window);

    /// <summary>The facts about one window, or null where it has gone.</summary>
    WindowFacts? Facts(IntPtr window);
}

/// <summary>
/// The window inspector on the live desktop: which windows count, and who owns them.
/// </summary>
/// <remarks>
/// <para>A window counts only if a person could work in it: visible, a root window, unowned or asking
/// for its own taskbar button, not a tool window, titled, and not hidden by its own program. A window
/// the shell hides because it sits on another virtual desktop still counts — measured, those report
/// cloak value 2 and a window its program hides reports 1.</para>
/// <para>Measured unelevated on one machine: 14 windows met the rule out of 226 processes in the
/// signed-in session. Enumeration sees only the calling desktop, so the secure desktop, where the
/// consent prompt appears, is never listed.</para>
/// </remarks>
internal sealed class WindowInspector : IWindowInspector
{
    private const int GWL_STYLE   = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_CHILD         = 0x40000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW  = 0x00040000;
    private const uint GA_ROOT = 2;
    private const uint GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 14;
    private const int DWM_CLOAKED_SHELL = 0x2;

    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;
    private const int ProcessBasicInformation = 0;

    /// <summary>How far up the parent chain is walked. Measured: 19 processes in one session had a
    /// parent that had already exited, so a long chain is rarely there to walk.</summary>
    private const int MaxAncestors = 3;

    private const string FrameHost = "ApplicationFrameHost.exe";

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid interfaceId,
                                                          [MarshalAs(UnmanagedType.Interface)] out ShellInterop.IPropertyStore store);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass,
                                                        out PROCESS_BASIC_INFORMATION info, int length, out int returned);

    public IReadOnlyList<IntPtr> ActionableWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            if (IsActionable(window)) found.Add(window);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public bool IsActionable(IntPtr window)
    {
        try
        {
            if (window == IntPtr.Zero || !IsWindowVisible(window)) return false;
            if (GetAncestor(window, GA_ROOT) != window) return false;

            long style = GetWindowLongPtr(window, GWL_STYLE).ToInt64();
            long exStyle = GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64();
            if ((style & WS_CHILD) != 0 || (exStyle & WS_EX_TOOLWINDOW) != 0) return false;
            if (GetWindow(window, GW_OWNER) != IntPtr.Zero && (exStyle & WS_EX_APPWINDOW) == 0) return false;
            if (GetWindowTextLength(window) == 0) return false;

            // Hidden by the shell for another virtual desktop still counts; hidden by its own program,
            // or through its owner, does not.
            if (DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0 && cloaked != DWM_CLOAKED_SHELL)
                return false;

            return true;
        }
        catch { return false; }
    }

    public bool IsMinimised(IntPtr window)
    {
        try { return IsIconic(window); }
        catch { return false; }
    }

    public WindowFacts? Facts(IntPtr window)
    {
        try
        {
            if (!IsWindow(window)) return null;
            GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0) return null;

            var process = ProcessOf(processId, out bool elevated, out IntPtr handle);
            try
            {
                string appId = AppIdOf(window);

                ProcessFacts? hosted = null;
                if (Path.GetFileName(process.Path).Equals(FrameHost, StringComparison.OrdinalIgnoreCase))
                    hosted = HostedBy(window, processId, appId);

                ProcessFacts? owner = null;
                IntPtr ownerWindow = GetWindow(window, GW_OWNER);
                if (ownerWindow != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(ownerWindow, out uint ownerId);
                    if (ownerId != 0) owner = Describe(ownerId);
                }

                return new WindowFacts(window, process, appId, hosted, owner, elevated, Ancestors(handle));
            }
            finally { ProcessIdentity.Close(handle); }
        }
        catch { return null; }
    }

    /// <summary>A process's file, package and elevation, with its handle left open for the parent
    /// walk.</summary>
    private static ProcessFacts ProcessOf(uint processId, out bool elevated, out IntPtr handle)
    {
        handle = ProcessIdentity.Open(processId);
        elevated = IsElevated(handle);
        return new ProcessFacts(processId, ProcessIdentity.PathOf(handle), ProcessIdentity.FamilyOf(handle));
    }

    private static ProcessFacts Describe(uint processId)
    {
        var (path, family) = ProcessIdentity.Of(processId);
        return new ProcessFacts(processId, path, family);
    }

    /// <summary>The Store app drawn inside the shared frame: named by the frame's own identity, which
    /// carries the package family before its <c>!</c>, and otherwise by the first child window another
    /// process owns.</summary>
    private static ProcessFacts? HostedBy(IntPtr frame, uint frameProcess, string frameAppId)
    {
        uint child = 0;
        EnumChildWindows(frame, (window, _) =>
        {
            GetWindowThreadProcessId(window, out uint id);
            if (id == 0 || id == frameProcess) return true;
            child = id;
            return false;
        }, IntPtr.Zero);

        var described = child != 0 ? Describe(child) : null;
        int bang = frameAppId.IndexOf('!');
        if (bang > 0)
            return new ProcessFacts(described?.ProcessId ?? 0, described?.Path ?? "", frameAppId[..bang]);
        return described;
    }

    /// <summary>The window's own System.AppUserModel.ID: a web app's window carries its own, a
    /// browser window its browser's or a profile's.</summary>
    private static string AppIdOf(IntPtr window)
    {
        ShellInterop.IPropertyStore? store = null;
        try
        {
            Guid iid = ShellInterop.IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(window, ref iid, out store) != 0 || store is null) return "";
            return ShellInterop.ReadString(store, ShellInterop.PKEY_AppUserModel_ID).Trim();
        }
        catch { return ""; }
        finally { ShellInterop.Release(store); }
    }

    private static bool IsElevated(IntPtr process)
    {
        if (process == IntPtr.Zero || !OpenProcessToken(process, TOKEN_QUERY, out IntPtr token)) return false;
        try { return GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) && elevated != 0; }
        finally { CloseHandle(token); }
    }

    /// <summary>The processes that started this one, nearest first. A parent id can be reused once
    /// the parent exits, so a parent that started after its child is not the parent and ends the
    /// walk.</summary>
    private static IReadOnlyList<ProcessFacts> Ancestors(IntPtr process)
    {
        var chain = new List<ProcessFacts>();
        IntPtr current = process;
        bool ownsCurrent = false;
        try
        {
            for (int depth = 0; depth < MaxAncestors && current != IntPtr.Zero; depth++)
            {
                if (NtQueryInformationProcess(current, ProcessBasicInformation, out var info,
                                              Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0)
                    break;
                uint parentId = (uint)info.InheritedFromUniqueProcessId.ToInt64();
                if (parentId == 0) break;

                IntPtr parent = ProcessIdentity.Open(parentId);
                if (parent == IntPtr.Zero) break;
                if (!GetProcessTimes(current, out long childStart, out _, out _, out _)
                    || !GetProcessTimes(parent, out long parentStart, out _, out _, out _)
                    || parentStart > childStart)
                {
                    ProcessIdentity.Close(parent);
                    break;
                }

                chain.Add(new ProcessFacts(parentId, ProcessIdentity.PathOf(parent), ProcessIdentity.FamilyOf(parent)));
                if (ownsCurrent) ProcessIdentity.Close(current);
                current = parent;
                ownsCurrent = true;
            }
        }
        catch { /* the chain read so far stands */ }
        finally { if (ownsCurrent) ProcessIdentity.Close(current); }
        return chain;
    }
}
