using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DropMe;

/// <summary>Gets the same thumbnail/icon Explorer shows for a file or folder.</summary>
internal static class ShellThumbnail
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfield0, dsBitfield1, dsBitfield2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out DIBSECTION lpvObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static ImageSource? Get(string path, int size)
    {
        IntPtr hbmp = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory.GetImage(new SIZE { cx = size, cy = size }, 0, out hbmp) != 0 || hbmp == IntPtr.Zero)
                return null;

            // Copy the pixels by hand: Imaging.CreateBitmapSourceFromHBitmap drops the alpha channel.
            if (GetObject(hbmp, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0
                || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
                return null;

            int width = ds.dsBm.bmWidth, height = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
            var pixels = new byte[stride * height];
            Marshal.Copy(ds.dsBm.bmBits, pixels, 0, pixels.Length);

            if (ds.dsBmih.biHeight > 0)
                FlipRows(pixels, stride, height);
            if (IsFullyTransparent(pixels))
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] = 255;

            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbmp != IntPtr.Zero)
                DeleteObject(hbmp);
            if (factory != null)
                Marshal.ReleaseComObject(factory);
        }
    }

    private static void FlipRows(byte[] pixels, int stride, int height)
    {
        var row = new byte[stride];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            Buffer.BlockCopy(pixels, top * stride, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom * stride, pixels, top * stride, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom * stride, stride);
        }
    }

    // Photo thumbnails sometimes come back with an all-zero alpha channel.
    private static bool IsFullyTransparent(byte[] pixels)
    {
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0)
                return false;
        return true;
    }
}
