using System.Runtime.InteropServices;

namespace FocusDesk.Helpers;

/// <summary>The shell and property-system declarations the Start-menu readers and the icon reader
/// share: shell items, their enumeration, property stores and the variant a property comes back
/// in.</summary>
/// <remarks>Nothing here logs or reaches the application: each reader decides what a failure means,
/// so the readers can be lifted into another program whole.</remarks>
internal static class ShellInterop
{
    public static readonly Guid BHID_EnumItems     = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid BHID_PropertyStore = new("0384e1a4-1523-439c-a4c8-ab911052f586");
    public static readonly Guid FOLDERID_AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    public static readonly Guid IID_IShellItem        = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IEnumShellItems   = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid IID_IPropertyStore    = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    public static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    /// <summary>The item's parsing name inside its parent: for an Apps-folder entry, the name the
    /// entry is found by again under <c>shell:AppsFolder\</c>.</summary>
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    /// <summary>System.AppUserModel.ID, from propkey.h.</summary>
    public static readonly PropertyKey PKEY_AppUserModel_ID =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    /// <summary>A PROPVARIANT: a type tag and two pointer-sized words, which is its size on both
    /// architectures. Read only through the property-system conversions, never field by field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant
    {
        public ushort Type;
        private ushort _reserved1, _reserved2, _reserved3;
        private IntPtr _word1, _word2;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    public interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    public interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, out IShellItem item, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems copy);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    public interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Size(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", PreserveSig = false)]
    public static extern void SHGetKnownFolderItem(ref Guid folderId, uint flags, IntPtr token,
                                                   ref Guid interfaceId, out IShellItem item);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
                                                          ref Guid interfaceId, out IShellItem item);

    [DllImport("propsys.dll", PreserveSig = false)]
    public static extern void PSGetNameFromPropertyKey(ref PropertyKey key, out IntPtr name);

    [DllImport("propsys.dll")]
    public static extern int PropVariantToStringAlloc(ref PropVariant value, out IntPtr text);

    [DllImport("propsys.dll")]
    public static extern int PropVariantToInt32(ref PropVariant value, out int number);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant value);

    /// <summary>A string the shell allocated, freed once read. Empty where none came back.</summary>
    public static string TakeString(IntPtr allocated)
    {
        if (allocated == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUni(allocated) ?? ""; }
        finally { Marshal.FreeCoTaskMem(allocated); }
    }

    /// <summary>One property as text, or empty where the store lacks it or it is not text.</summary>
    public static string ReadString(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0) return "";
        try
        {
            return PropVariantToStringAlloc(ref value, out IntPtr text) == 0 ? TakeString(text) : "";
        }
        finally { PropVariantClear(ref value); }
    }

    /// <summary>A shell item's name in one of its forms, or empty.</summary>
    public static string DisplayName(IShellItem item, uint form)
    {
        try
        {
            item.GetDisplayName(form, out IntPtr name);
            return TakeString(name);
        }
        catch { return ""; }
    }

    /// <summary>The property store behind a shell item, or null.</summary>
    public static IPropertyStore? PropertiesOf(IShellItem item)
    {
        try
        {
            Guid handler = BHID_PropertyStore, iid = IID_IPropertyStore;
            item.BindToHandler(IntPtr.Zero, ref handler, ref iid, out IntPtr store);
            return store == IntPtr.Zero ? null : TakeObject<IPropertyStore>(store);
        }
        catch { return null; }
    }

    /// <summary>Wraps an interface pointer the shell handed back and drops the raw reference.</summary>
    public static T TakeObject<T>(IntPtr pointer)
    {
        try { return (T)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
    }

    /// <summary>Releases a runtime-callable wrapper now rather than at the next collection, so a
    /// long enumeration does not hold hundreds of shell objects open.</summary>
    public static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
    }
}
