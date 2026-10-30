using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace DropMe;

/// <summary>
/// Reads "virtual" files (FileGroupDescriptorW + FileContents), which is how browsers
/// and some apps hand over an image or attachment that doesn't exist on disk yet.
/// </summary>
internal static class VirtualFiles
{
    public const string DescriptorFormat = "FileGroupDescriptorW";
    private const string ContentsFormat = "FileContents";

    // Offsets inside FILEDESCRIPTORW: 72 bytes of metadata, then WCHAR cFileName[260].
    private const int DescriptorSize = 592;
    private const int FileNameOffset = 72;
    private const int FileNameBytes = 520;

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM pmedium);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    /// <summary>Writes every virtual file to <paramref name="folder"/> and returns the paths written.</summary>
    public static List<string> Save(IDataObject data, string folder, Func<string, string> uniquePath)
    {
        var saved = new List<string>();
        if (!data.GetDataPresent(DescriptorFormat)
            || data.GetData(DescriptorFormat) is not MemoryStream descriptor
            || data is not ComTypes.IDataObject com)
            return saved;

        var names = ReadNames(descriptor.ToArray());
        for (int i = 0; i < names.Count; i++)
        {
            var bytes = ReadContents(com, i);
            if (bytes == null)
                continue;
            string path = uniquePath(Path.Combine(folder, SanitizeFileName(names[i])));
            File.WriteAllBytes(path, bytes);
            saved.Add(path);
        }
        return saved;
    }

    private static List<string> ReadNames(byte[] buffer)
    {
        var names = new List<string>();
        int count = BitConverter.ToInt32(buffer, 0);
        for (int i = 0; i < count; i++)
        {
            int offset = 4 + i * DescriptorSize + FileNameOffset;
            if (offset + FileNameBytes > buffer.Length)
                break;
            string name = Encoding.Unicode.GetString(buffer, offset, FileNameBytes);
            int end = name.IndexOf('\0');
            names.Add(end >= 0 ? name[..end] : name);
        }
        return names;
    }

    private static byte[]? ReadContents(ComTypes.IDataObject com, int index)
    {
        var format = new ComTypes.FORMATETC
        {
            cfFormat = (short)DataFormats.GetDataFormat(ContentsFormat).Id,
            dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            ptd = IntPtr.Zero,
            tymed = ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL,
        };

        ComTypes.STGMEDIUM medium;
        try
        {
            com.GetData(ref format, out medium);
        }
        catch (COMException)
        {
            return null;
        }

        try
        {
            if (medium.tymed == ComTypes.TYMED.TYMED_ISTREAM)
            {
                var stream = (ComTypes.IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                try
                {
                    return ReadStream(stream);
                }
                finally
                {
                    Marshal.ReleaseComObject(stream);
                }
            }
            if (medium.tymed == ComTypes.TYMED.TYMED_HGLOBAL)
            {
                IntPtr ptr = GlobalLock(medium.unionmember);
                try
                {
                    var bytes = new byte[(int)GlobalSize(medium.unionmember)];
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);
                    return bytes;
                }
                finally
                {
                    GlobalUnlock(medium.unionmember);
                }
            }
            return null;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static byte[] ReadStream(ComTypes.IStream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        IntPtr bytesRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            while (true)
            {
                stream.Read(buffer, buffer.Length, bytesRead);
                int n = Marshal.ReadInt32(bytesRead);
                if (n <= 0)
                    break;
                output.Write(buffer, 0, n);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bytesRead);
        }
        return output.ToArray();
    }

    public static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "File" : name;
    }
}
