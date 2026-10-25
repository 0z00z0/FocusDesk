using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FocusDesk.Helpers;

/// <summary>Starts a program through Explorer's shell, so it runs unelevated as the signed-in person.
/// No process id or failure comes back. Call on a single-threaded-apartment thread.</summary>
internal static class ShellLaunch
{
    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    private const int CSIDL_DESKTOP = 0;
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_BACKGROUND = 0;
    private const int SW_SHOWNORMAL = 1;

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        // Slots before FindWindowSW, declared only to keep the table in order.
        void Count();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object location, ref object locationRoot, int windowClass, out int window, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid interfaceId, out IntPtr result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        [PreserveSig] int QueryActiveShellView(out IShellView view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
        void SelectItem();
        [PreserveSig] int GetItemObject(uint item, ref Guid interfaceId, [MarshalAs(UnmanagedType.IDispatch)] out object result);
    }

    /// <summary>Opens a shortcut, an Apps-folder entry or a program file as the desktop would.</summary>
    /// <exception cref="COMException">Explorer's desktop is not there to ask.</exception>
    public static void Open(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var windows = (IShellWindows)(Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: true)!)
                                      ?? throw new COMException("The shell windows list could not be created"));
        object location = CSIDL_DESKTOP;
        object root = null!;
        object desktop = windows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH)
                         ?? throw new COMException("Explorer's desktop window was not found");

        Guid service = SID_STopLevelBrowser, browserId = IID_IShellBrowser, dispatchId = IID_IDispatch;
        Marshal.ThrowExceptionForHR(((IServiceProvider)desktop).QueryService(ref service, ref browserId, out IntPtr pointer));
        var browser = ShellInterop.TakeObject<IShellBrowser>(pointer);
        Marshal.ThrowExceptionForHR(browser.QueryActiveShellView(out var view));
        Marshal.ThrowExceptionForHR(view.GetItemObject(SVGIO_BACKGROUND, ref dispatchId, out object folderView));

        object shell = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null,
                                                         CultureInfo.InvariantCulture)
                       ?? throw new COMException("Explorer's shell object was not available");
        shell.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, shell,
                                     [target, Missing.Value, Missing.Value, Missing.Value, SW_SHOWNORMAL],
                                     CultureInfo.InvariantCulture);
    }
}
