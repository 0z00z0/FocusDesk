using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FocusDesk.Services;

namespace FocusDesk.Helpers;

/// <summary>One connected display: its key and its GDI device name, as a monitor's information names it.</summary>
internal sealed record DisplayIdentity(string Key, string DeviceName);

/// <summary>The connected displays and the layout key derived from them.</summary>
internal sealed record DisplayLayoutReading(string LayoutKey, IReadOnlyList<DisplayIdentity> Displays)
{
    public string? KeyFor(string deviceName) =>
        Displays.FirstOrDefault(d => string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))?.Key;
}

/// <summary>Which displays are connected, as keys from EDID codes and the WMI serial, never names.
/// The derivation is the step 11 probe's; changing it orphans every saved position.</summary>
internal static class DisplayLayout
{
    /// <summary>One display's key before occurrence numbers: maker, product, serial and internal flag.</summary>
    public static string BaseKey(string maker, string product, string serial, bool isInternal) =>
        $"{maker}-{product}-{serial}-{(isInternal ? "internal" : "external")}";

    /// <summary>The display keys in the order given, which is left to right: where several share a
    /// base key, each gets "#" and its occurrence number in that order.</summary>
    public static IReadOnlyList<string> Keys(IReadOnlyList<string> baseKeysLeftToRight)
    {
        var counts = baseKeysLeftToRight.GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new List<string>(baseKeysLeftToRight.Count);
        foreach (string key in baseKeysLeftToRight)
        {
            seen[key] = seen.GetValueOrDefault(key) + 1;
            keys.Add(counts[key] > 1 ? $"{key}#{seen[key]}" : key);
        }
        return keys;
    }

    /// <summary>The layout key: the SHA-256 of the sorted display keys joined by line feeds, in
    /// lowercase hexadecimal.</summary>
    public static string LayoutKey(IEnumerable<string> displayKeys) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', displayKeys.Order(StringComparer.Ordinal)))));

    /// <summary>The displays connected now, or null where the display configuration cannot be read.</summary>
    public static DisplayLayoutReading? Read()
    {
        try
        {
            var targets = Targets();
            if (targets.Count == 0) return null;

            var serials = Serials();
            var ordered = targets.OrderBy(t => t.X).ThenBy(t => t.Y).ToList();
            var keys = Keys([.. ordered.Select(t => BaseKey(t.Maker, t.Product,
                serials.GetValueOrDefault(InstanceOf(t.DevicePath), ""), t.Internal))]);
            var displays = ordered.Select((t, i) => new DisplayIdentity(keys[i], t.DeviceName)).ToList();
            return new DisplayLayoutReading(LayoutKey(keys), displays);
        }
        catch (Exception ex)
        {
            AppLog.Error("DisplayLayout.Read", ex);
            return null;
        }
    }

    private sealed record Target(string DeviceName, string Maker, string Product, bool Internal, string DevicePath, int X, int Y);

    /// <summary>The monitor's instance as <c>WmiMonitorID</c> names it: the device path between its
    /// prefix and its interface class, with backslashes for the hashes.</summary>
    private static string InstanceOf(string devicePath)
    {
        const string prefix = @"\\?\";
        string path = devicePath.StartsWith(prefix, StringComparison.Ordinal) ? devicePath[prefix.Length..] : devicePath;
        int end = path.IndexOf("#{", StringComparison.Ordinal);
        return (end >= 0 ? path[..end] : path).Replace('#', '\\');
    }

    /// <summary>Each monitor's EDID serial by instance, the trailing "_0" taken off. Empty where WMI
    /// cannot be read, so the keys still form.</summary>
    private static Dictionary<string, string> Serials()
    {
        var serials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, SerialNumberID FROM WmiMonitorID");
            foreach (var monitor in searcher.Get())
                using (monitor)
                {
                    string instance = monitor["InstanceName"] as string ?? "";
                    int underscore = instance.LastIndexOf('_');
                    if (underscore > 0 && instance[(underscore + 1)..].All(char.IsAsciiDigit)) instance = instance[..underscore];
                    string serial = monitor["SerialNumberID"] is ushort[] codes
                        ? new string([.. codes.Where(c => c != 0).Select(c => (char)c)])
                        : "";
                    serials.TryAdd(instance, serial);
                }
        }
        catch (Exception ex) { AppLog.Error("DisplayLayout.Serials", ex); }
        return serials;
    }

    private static List<Target> Targets()
    {
        int result = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
        if (result != 0) throw new System.ComponentModel.Win32Exception(result);
        var paths = new PATH_INFO[pathCount];
        var modes = new MODE_INFO[modeCount];
        result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (result != 0) throw new System.ComponentModel.Win32Exception(result);

        var targets = new List<Target>((int)pathCount);
        for (int i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            var name = new TARGET_NAME
            {
                header = new HEADER { type = GET_TARGET_NAME, size = (uint)Marshal.SizeOf<TARGET_NAME>(), adapterId = path.target.adapterId, id = path.target.id },
            };
            if (DisplayConfigGetDeviceInfo(ref name) != 0) continue;
            var source = new SOURCE_NAME
            {
                header = new HEADER { type = GET_SOURCE_NAME, size = (uint)Marshal.SizeOf<SOURCE_NAME>(), adapterId = path.source.adapterId, id = path.source.id },
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

            uint technology = path.target.outputTechnology;
            bool edid = (name.flags & EDID_IDS_VALID) != 0;
            uint mode = path.source.modeInfoIdx;
            bool positioned = mode < modeCount && modes[mode].infoType == MODE_INFO_TYPE_SOURCE;
            targets.Add(new Target(
                source.gdi ?? "",
                edid ? Maker(name.edidManufactureId) : "",
                edid ? name.edidProductCodeId.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) : "",
                technology is TECHNOLOGY_INTERNAL or TECHNOLOGY_DISPLAYPORT_EMBEDDED or TECHNOLOGY_UDI_EMBEDDED,
                name.path ?? "",
                positioned ? modes[mode].x : 0,
                positioned ? modes[mode].y : 0));
        }
        return targets;
    }

    /// <summary>The three-letter maker code packed big-endian into five-bit letters.</summary>
    private static string Maker(ushort id)
    {
        int v = ((id & 0xFF) << 8) | (id >> 8);
        return new string([(char)('A' - 1 + ((v >> 10) & 0x1F)), (char)('A' - 1 + ((v >> 5) & 0x1F)), (char)('A' - 1 + (v & 0x1F))]);
    }

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint GET_SOURCE_NAME = 1;
    private const uint GET_TARGET_NAME = 2;
    private const uint EDID_IDS_VALID = 4;
    private const uint MODE_INFO_TYPE_SOURCE = 1;
    private const uint TECHNOLOGY_INTERNAL = 0x80000000;
    private const uint TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
    private const uint TECHNOLOGY_UDI_EMBEDDED = 13;

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct PATH_SOURCE { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation;
        public uint scaling; public uint refreshNum; public uint refreshDen; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)] private struct PATH_INFO { public PATH_SOURCE source; public PATH_TARGET target; public uint flags; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(28)] public int x;
        [FieldOffset(32)] public int y;
    }

    [StructLayout(LayoutKind.Sequential)] private struct HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_NAME
    {
        public HEADER header; public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string friendly;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SOURCE_NAME { public HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdi; }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes,
                                                 [Out] MODE_INFO[] modes, IntPtr topology);

    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME request);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME request);
}
