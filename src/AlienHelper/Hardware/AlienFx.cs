using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AlienHelper.Hardware;

/// <summary>
/// AlienFX USB HID lighting. Only the two protocol versions found on the Area-51 16 are implemented:
///  - v4: Alienware 187C:xxxx with 34-byte output reports (chassis zones)
///  - v5: Darfon 0D62:xxxx, usage 0xCC, 64-byte feature reports (per-key keyboard)
/// Protocol reference: T-Troll/alienfx-tools AlienFX_SDK.
/// </summary>
public sealed class AlienFxDevice : IDisposable
{
    public enum Api { V4 = 4, V5 = 5 }

    public ushort Vid { get; }
    public ushort Pid { get; }
    public Api Version { get; }
    public string Path { get; }
    public string Name => Version == Api.V5 ? "Keyboard" : "Chassis";

    readonly SafeFileHandle _h;
    readonly int _len;

    AlienFxDevice(SafeFileHandle h, string path, ushort vid, ushort pid, Api v, int len)
    { _h = h; Path = path; Vid = vid; Pid = pid; Version = v; _len = len; }

    public static List<AlienFxDevice> Enumerate()
    {
        var list = new List<AlienFxDevice>();
        foreach (var path in Native.HidPaths())
        {
            var h = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) continue;
            var attr = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>() };
            Api? ver = null; int len = 0;
            if (Native.HidD_GetAttributes(h, ref attr) && Native.HidD_GetPreparsedData(h, out var pp))
            {
                Native.HidP_GetCaps(pp, out var caps);
                Native.HidD_FreePreparsedData(pp);
                if (attr.VendorID == 0x0D62 && caps.Usage == 0xCC && caps.OutputReportByteLength == 0 && caps.FeatureReportByteLength > 0)
                { ver = Api.V5; len = caps.FeatureReportByteLength; }
                else if (attr.VendorID == 0x187C && caps.OutputReportByteLength == 34)
                { ver = Api.V4; len = 34; }
            }
            if (ver is { } v) list.Add(new AlienFxDevice(h, path, attr.VendorID, attr.ProductID, v, len));
            else h.Dispose();
        }
        return list;
    }

    // ---------------- low level ----------------
    bool Send(byte[] cmd, params (int pos, byte[] data)[] mods)
    {
        var buf = new byte[_len];
        Array.Copy(cmd, 0, buf, 1, Math.Min(cmd.Length, _len - 1));
        buf[0] = Version == Api.V5 ? (byte)0xCC : (byte)0;
        foreach (var (pos, data) in mods)
            Array.Copy(data, 0, buf, pos, Math.Min(data.Length, _len - pos));
        return Version == Api.V5
            ? Native.HidD_SetFeature(_h, buf, buf.Length)
            : Native.HidD_SetOutputReport(_h, buf, buf.Length);
    }

    public int Status()
    {
        var buf = new byte[_len];
        if (Version == Api.V5)
        {
            Send(new byte[] { 0x93 });
            buf[0] = 0xCC;
            return Native.HidD_GetFeature(_h, buf, buf.Length) ? buf[2] : -1;
        }
        return Native.HidD_GetInputReport(_h, buf, buf.Length) ? buf[2] : -1;
    }

    // v4 control: [4] = 1 start, 2 finish+save, 3 finish+play, 4 remove, 5 play, 6 set default
    bool V4Control(byte type, byte idHi = 0xFF, byte idLo = 0xFF) =>
        Send(new byte[] { 0x03, 0x21, 0x00, type, idHi, idLo });

    void WaitV4Ready()
    {
        // 0x551 firmware never reports busy; just make sure it answers.
        for (int i = 0; i < 25; i++)
        {
            int s = Status();
            if (s != 34) return;   // 34 = ALIENFX_V4_BUSY
            Thread.Sleep(20);
        }
    }

    /// <summary>Static color for light ids [0, count).</summary>
    /// <summary>Per-key colors (v5 keyboards only): every id in [0, count) gets its own color.</summary>
    /// <param name="fast">Animation frames: skip stopping firmware effects (done once by the first full call).</param>
    /// <summary>Frame mode for animations: false = reset (0x94) before every frame, true = no reset between frames.</summary>
    public bool FramesWithoutReset { get; set; } = true;

    /// <param name="fast">Animation frames: skip stopping firmware effects (done once by the first full call).</param>
    /// <param name="only">Animation frames: send just these ids (the keys that exist) to keep frames short.</param>
    public bool SetKeyColors(Func<int, (byte r, byte g, byte b)> colorOf, int count, bool fast = false, IReadOnlyList<int>? only = null)
    {
        if (Version != Api.V5) return false;
        if (!fast)
        {
            Send(new byte[] { 0x94 });
            Status();
            Send(new byte[] { 0x80, 0x02, 0x07, 0x00, 0x00, 0x01, 0x01, 0x01 }, (2, new byte[] { 0x01, 0xFE }));
            Send(new byte[] { 0x8B, 0x01, 0xFF });
        }
        if (!fast || !FramesWithoutReset)
        {
            Send(new byte[] { 0x94 });
            Status();
        }
        var ids = only ?? Enumerable.Range(0, count).ToList();
        for (int n = 0; n < ids.Count;)
        {
            var mods = new List<(int, byte[])>();
            for (int pos = 4; pos + 4 <= _len && n < ids.Count; pos += 4, n++)
            {
                int id = ids[n];
                var (r, g, b) = colorOf(id);
                mods.Add((pos, new[] { (byte)(id + 1), r, g, b }));
            }
            Send(new byte[] { 0x8C, 0x02 }, mods.ToArray());
        }
        Send(new byte[] { 0x8C, 0x13 });
        return Send(new byte[] { 0x8B, 0x01, 0xFF });
    }

    public bool SetColor(byte r, byte g, byte b, int count)
    {
        if (Version == Api.V5)
        {
            // Stop any firmware effect (wave, breathing...) so static colors are shown.
            Send(new byte[] { 0x94 });
            Status();
            Send(new byte[] { 0x80, 0x02, 0x07, 0x00, 0x00, 0x01, 0x01, 0x01 }, (2, new byte[] { 0x01, 0xFE }));
            Send(new byte[] { 0x8B, 0x01, 0xFF });
            Send(new byte[] { 0x94 });
            Status();
            int id = 0;
            while (id < count)
            {
                var mods = new List<(int, byte[])>();
                for (int pos = 4; pos + 4 <= _len && id < count; pos += 4, id++)
                    mods.Add((pos, new[] { (byte)(id + 1), r, g, b }));
                Send(new byte[] { 0x8C, 0x02 }, mods.ToArray());
            }
            Send(new byte[] { 0x8C, 0x13 });
            return Send(new byte[] { 0x8B, 0x01, 0xFF });
        }

        WaitV4Ready();
        V4Control(4);
        V4Control(1);
        bool ok = true;
        for (int start = 0; start < count; start += 26)
        {
            int n = Math.Min(26, count - start);
            var ids = new byte[n];
            for (int i = 0; i < n; i++) ids[i] = (byte)(start + i);
            ok &= Send(new byte[] { 0x03, 0x27 }, (3, new byte[] { r, g, b, 0, (byte)n }), (8, ids));
        }
        V4Control(3);
        return ok;
    }

    /// <summary>Brightness 0..100.</summary>
    public bool SetBrightness(int percent, int count)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (Version == Api.V5)
        {
            Send(new byte[] { 0x94 });
            Status();
            return Send(new byte[] { 0x83, 0x38, 0x9C }, (4, new[] { (byte)(percent * 255 / 100) }));
        }
        bool ok = true;
        for (int start = 0; start < count; start += 28)
        {
            int n = Math.Min(28, count - start);
            var ids = new byte[n];
            for (int i = 0; i < n; i++) ids[i] = (byte)(start + i);
            ok &= Send(new byte[] { 0x03, 0x26 }, (3, new byte[] { (byte)(100 - percent), 0, (byte)n }), (6, ids));
        }
        return ok;
    }

    public void Dispose() => _h.Dispose();

    // ---------------- interop ----------------
    static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] b, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] b, int len);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        public static IEnumerable<string> HidPaths()
        {
            HidD_GetHidGuid(out var g);
            var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == new IntPtr(-1)) yield break;
            try
            {
                var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                    var buf = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(buf, 8); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W on x64
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, size, out _, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                        if (path.Contains("vid_187c", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("vid_0d62", StringComparison.OrdinalIgnoreCase))
                            yield return path;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
}
