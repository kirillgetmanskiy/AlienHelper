using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AlienHelper.Hardware;

/// <summary>
/// Drive temperatures via IOCTL_STORAGE_QUERY_PROPERTY (StorageDeviceTemperatureProperty).
/// Works for NVMe/SATA drives without admin rights; USB enclosures usually don't answer and are skipped.
/// </summary>
public static class DiskTemps
{
    const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
    const int StorageDeviceProperty = 0, StorageDeviceTemperatureProperty = 52;

    /// <summary>Drives that report a temperature: physical drive number, product name, °C.</summary>
    public static List<(int index, string name, int temp)> Read()
    {
        var res = new List<(int, string, int)>();
        for (int i = 0; i < 8; i++)
        {
            using var h = CreateFile($@"\\.\PhysicalDrive{i}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) continue;
            var o = Query(h, StorageDeviceTemperatureProperty);
            if (o == null || BitConverter.ToUInt16(o, 12) == 0) continue;
            int t = BitConverter.ToInt16(o, 26);
            if (t is > 0 and < 120) res.Add((i, Name(h) ?? $"Disk {i}", t));
        }
        return res;
    }

    static byte[]? Query(SafeFileHandle h, int property)
    {
        var q = new byte[12];
        BitConverter.GetBytes(property).CopyTo(q, 0);   // PropertyId, QueryType = PropertyStandardQuery (0)
        var o = new byte[1024];
        return DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, q, q.Length, o, o.Length, out _, IntPtr.Zero) ? o : null;
    }

    static string? Name(SafeFileHandle h)
    {
        var o = Query(h, StorageDeviceProperty);
        if (o == null) return null;
        int off = (int)BitConverter.ToUInt32(o, 16);   // STORAGE_DEVICE_DESCRIPTOR.ProductIdOffset
        if (off <= 0 || off >= o.Length) return null;
        int end = Array.IndexOf(o, (byte)0, off);
        return Encoding.ASCII.GetString(o, off, (end < 0 ? o.Length : end) - off).Trim();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int returned, IntPtr overlapped);
}
