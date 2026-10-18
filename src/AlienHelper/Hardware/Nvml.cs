using System.Runtime.InteropServices;

namespace AlienHelper.Hardware;

/// <summary>
/// NVIDIA GPU telemetry through nvml.dll (shipped with the driver in System32).
/// Note: querying NVML wakes a sleeping dGPU, so callers should poll it only while something is on screen.
/// </summary>
public sealed class Nvml : IDisposable
{
    public sealed record Reading(int Temp, int MemTemp, int CoreMhz, int MemMhz, float PowerW, int Load, float VramUsedGb, float VramTotalGb);

    readonly IntPtr[] _devs;
    readonly uint[] _maxMw;   // highest power reading we believe, per GPU
    /// <summary>Names of the NVIDIA GPUs, in NVML index order.</summary>
    public IReadOnlyList<string> Names { get; }
    public int Count => _devs.Length;

    /// <summary>PCI bus number of a GPU (matches it with NVAPI), or uint.MaxValue when unknown.</summary>
    public uint Bus(int index) => nvmlDeviceGetPciInfo_v3(_devs[index], out var p) == 0 ? p.bus : uint.MaxValue;

    Nvml(IntPtr[] devs, string[] names)
    {
        _devs = devs;
        Names = names;
        // the top of the power-limit range (175 W on an RTX 5090 Laptop) plus room for short boost peaks;
        // past that a reading is a glitch: right after the GPU wakes, NVML sometimes returns hundreds of watts or more
        _maxMw = devs.Select(d => nvmlDeviceGetPowerManagementLimitConstraints(d, out _, out uint max) == 0 && max > 0
            ? max + max / 2 : 400_000u).ToArray();
    }

    public static Nvml? TryOpen()
    {
        try
        {
            if (nvmlInit_v2() != 0) return null;
            if (nvmlDeviceGetCount_v2(out uint count) != 0) count = 1;
            var devs = new List<IntPtr>();
            var names = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                if (nvmlDeviceGetHandleByIndex_v2(i, out var dev) != 0) continue;
                var sb = new System.Text.StringBuilder(96);
                nvmlDeviceGetName(dev, sb, (uint)sb.Capacity);
                devs.Add(dev);
                names.Add(sb.ToString());
            }
            if (devs.Count == 0) { nvmlShutdown(); return null; }
            return new Nvml(devs.ToArray(), names.ToArray());
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    public Reading Read(int index)
    {
        var dev = _devs[index];
        int temp = nvmlDeviceGetTemperature(dev, 0, out uint t) == 0 ? (int)t : -1;
        int core = nvmlDeviceGetClockInfo(dev, 0, out uint c) == 0 ? (int)c : -1;
        int mem = nvmlDeviceGetClockInfo(dev, 2, out uint m) == 0 ? (int)m : -1;
        // a GPU waking from D3 sometimes reports nonsense (500 W to tens of kW): drop anything past its power limit
        float pw = nvmlDeviceGetPowerUsage(dev, out uint mw) == 0 && mw <= _maxMw[index] ? mw / 1000f : -1;
        int load = nvmlDeviceGetUtilizationRates(dev, out var u) == 0 ? (int)u.gpu : -1;
        float used = -1, total = -1;
        if (nvmlDeviceGetMemoryInfo(dev, out var mi) == 0) { used = mi.used / 1073741824f; total = mi.total / 1073741824f; }
        return new Reading(temp, MemTemp(dev), core, mem, pw, load, used, total);
    }

    static int MemTemp(IntPtr dev)
    {
        // NVML_FI_DEV_MEMORY_TEMP; most laptop GPUs report it as not supported.
        var f = new FieldValue[] { new() { fieldId = 82 } };
        if (nvmlDeviceGetFieldValues(dev, 1, f) != 0 || f[0].nvmlReturn != 0) return -1;
        int v = (int)f[0].value;
        return v is > 0 and < 150 ? v : -1;
    }

    public void Dispose() { try { nvmlShutdown(); } catch { } }

    [StructLayout(LayoutKind.Sequential)] struct Utilization { public uint gpu, memory; }
    [StructLayout(LayoutKind.Sequential)]
    struct PciInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] busIdLegacy;
        public uint domain, bus, device, pciDeviceId, pciSubSystemId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] busId;
    }
    [StructLayout(LayoutKind.Sequential)] struct MemoryInfo { public ulong total, free, used; }
    [StructLayout(LayoutKind.Sequential)]
    struct FieldValue { public uint fieldId, scopeId; public long timestamp, latencyUsec; public int valueType, nvmlReturn; public long value; }

    const string Dll = "nvml.dll";
    [DllImport(Dll)] static extern int nvmlInit_v2();
    [DllImport(Dll)] static extern int nvmlShutdown();
    [DllImport(Dll)] static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport(Dll)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr dev);
    [DllImport(Dll, CharSet = CharSet.Ansi)] static extern int nvmlDeviceGetName(IntPtr dev, System.Text.StringBuilder name, uint len);
    [DllImport(Dll)] static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint temp);
    [DllImport(Dll)] static extern int nvmlDeviceGetClockInfo(IntPtr dev, int type, out uint mhz);
    [DllImport(Dll)] static extern int nvmlDeviceGetPowerUsage(IntPtr dev, out uint mw);
    [DllImport(Dll)] static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr dev, out uint minMw, out uint maxMw);
    [DllImport(Dll)] static extern int nvmlDeviceGetUtilizationRates(IntPtr dev, out Utilization u);
    [DllImport(Dll)] static extern int nvmlDeviceGetPciInfo_v3(IntPtr dev, out PciInfo p);
    [DllImport(Dll)] static extern int nvmlDeviceGetMemoryInfo(IntPtr dev, out MemoryInfo m);
    [DllImport(Dll)] static extern int nvmlDeviceGetFieldValues(IntPtr dev, int count, [In, Out] FieldValue[] values);
}
