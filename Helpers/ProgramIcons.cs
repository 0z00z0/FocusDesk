using System.Runtime.InteropServices;

namespace FocusDesk.Helpers;

/// <summary>An icon's pixels, premultiplied BGRA, top row first.</summary>
internal sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// One icon for a shortcut, a program file or an Apps-folder entry, drawn by the shell the way the
/// Start menu draws it.
/// </summary>
/// <remarks>
/// <para>Measured on one machine, unelevated: the shell drew a 32-pixel icon for 160 of 160 shortcuts
/// in 3.3 s and for 196 of 196 Apps-folder entries in 2.4 s — too slow to draw on the thread that
/// draws the window, so the caller reads on a background single-threaded-apartment thread.</para>
/// <para>Pixels rather than a bitmap object, so this stays free of the interface framework and the
/// window turns them into an image on its own thread.</para>
/// </remarks>
internal static class ProgramIcons
{
    private const int SIIGBF_ICONONLY = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits,
                                        ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    /// <summary>The icon for <paramref name="location"/> — a file path or an Apps-folder parsing name
    /// with its <c>shell:AppsFolder\</c> prefix — at <paramref name="size"/> pixels, or null.</summary>
    internal static IconPixels? Load(string location, int size)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;

        ShellInterop.IShellItem? item = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            Guid itemId = ShellInterop.IID_IShellItem;
            ShellInterop.SHCreateItemFromParsingName(location, IntPtr.Zero, ref itemId, out item);
            if (item is not ShellInterop.IShellItemImageFactory factory) return null;

            if (factory.GetImage(new ShellInterop.Size(size, size), SIIGBF_ICONONLY, out bitmap) != 0
                || bitmap == IntPtr.Zero)
                return null;

            return Pixels(bitmap);
        }
        catch { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            ShellInterop.Release(item);
        }
    }

    private static IconPixels? Pixels(IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out var info) == 0) return null;
        int width = info.Width, height = Math.Abs(info.Height);
        if (width <= 0 || height <= 0) return null;

        var header = new BITMAPINFOHEADER
        {
            Size     = Marshal.SizeOf<BITMAPINFOHEADER>(),
            Width    = width,
            Height   = -height,   // negative: top row first, whichever way the bitmap is held
            Planes   = 1,
            BitCount = 32,
        };
        var bits = new byte[width * height * 4];

        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)height, bits, ref header, 0) == height
                ? new IconPixels(width, height, bits)
                : null;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }
}
