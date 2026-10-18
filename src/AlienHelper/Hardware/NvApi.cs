using System.Runtime.InteropServices;

namespace AlienHelper.Hardware;

/// <summary>
/// Extra NVIDIA temperatures NVML does not report on GeForce cards: hot spot and memory (VRAM).
/// Uses the driver's nvapi64.dll through NvAPI_QueryInterface and the thermal sensors query that
/// GPU-Z and LibreHardwareMonitor read. Read-only; like NVML, it wakes a sleeping dGPU.
/// </summary>
public sealed class NvApi
{
    const uint IdInitialize = 0x0150E828, IdEnumPhysicalGpus = 0xE5AC921F, IdGetBusId = 0x1BE0B8E5, IdThermalSensors = 0x65FE3AAD;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr QueryInterfaceFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumGpusFn([Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int BusIdFn(IntPtr gpu, out uint bus);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ThermalSensorsFn(IntPtr gpu, ref ThermalSensors sensors);

    [StructLayout(LayoutKind.Sequential)]
    struct ThermalSensors
    {
        public uint Version;
        public uint Mask;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public int[] Values;   // 1/256 °C
    }

    readonly ThermalSensorsFn _thermal;
    readonly (IntPtr handle, uint bus, uint mask)[] _gpus;

    NvApi(ThermalSensorsFn thermal, (IntPtr, uint, uint)[] gpus) { _thermal = thermal; _gpus = gpus; }

    public static NvApi? TryOpen()
    {
        try
        {
            if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib)) return null;
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(NativeLibrary.GetExport(lib, "nvapi_QueryInterface"));
            T? Fn<T>(uint id) where T : Delegate { var p = query(id); return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p); }

            var init = Fn<InitializeFn>(IdInitialize);
            var enumGpus = Fn<EnumGpusFn>(IdEnumPhysicalGpus);
            var thermal = Fn<ThermalSensorsFn>(IdThermalSensors);
            var busId = Fn<BusIdFn>(IdGetBusId);
            if (init == null || enumGpus == null || thermal == null || init() != 0) return null;
            var handles = new IntPtr[64];
            if (enumGpus(handles, out int n) != 0 || n <= 0) return null;

            var gpus = new List<(IntPtr, uint, uint)>();
            for (int i = 0; i < n; i++)
            {
                uint bus = busId != null && busId(handles[i], out uint b) == 0 ? b : uint.MaxValue;
                // the driver rejects bits for sensors the card does not have: find how many there are
                uint mask = 0;
                for (int bit = 0; bit < 32; bit++)
                {
                    var s = New(1u << bit);
                    int rc = thermal(handles[i], ref s);
                    LastStatus = rc;
                    if (rc != 0) break;
                    mask |= 1u << bit;
                }
                gpus.Add((handles[i], bus, mask));
            }
            return new NvApi(thermal, gpus.ToArray());
        }
        catch { return null; }
    }

    static ThermalSensors New(uint mask) => new()
    {
        Version = (uint)Marshal.SizeOf<ThermalSensors>() | (2u << 16),
        Mask = mask,
        Reserved = new uint[8],
        Values = new int[32],
    };

    /// <summary>All sensor values in °C for the GPU on this PCI bus (or the n-th GPU when the bus is unknown).</summary>
    public float[]? ReadAll(uint bus, int fallbackIndex)
    {
        int i = Array.FindIndex(_gpus, g => g.bus == bus);
        if (i < 0) i = fallbackIndex;
        if (i < 0 || i >= _gpus.Length || _gpus[i].mask == 0) return null;
        var s = New(_gpus[i].mask);
        if (_thermal(_gpus[i].handle, ref s) != 0) return null;
        return s.Values.Select(v => v / 256f).ToArray();
    }

    /// <summary>NVAPI status of the last probe call (0 = OK), for diagnostics.</summary>
    public static int LastStatus { get; private set; }

    public int Count => _gpus.Length;
    public uint Bus(int i) => _gpus[i].bus;
    public uint Mask(int i) => _gpus[i].mask;

    /// <summary>
    /// Memory (VRAM) temperature, or -1. The slot depends on the generation: GDDR6X cards (RTX 30/40) are known to
    /// report the memory junction at 9; this RTX 5080 Laptop reports it at 2 (matched GPU-Z: 52.0 °C). 255 = no sensor.
    /// </summary>
    public int MemoryTemp(uint bus, int fallbackIndex)
    {
        var v = ReadAll(bus, fallbackIndex);
        if (v == null) return -1;
        static bool Ok(float t) => t is > 1 and < 150;
        if (Ok(v[9])) return (int)Math.Round(v[9]);
        if (Ok(v[2])) return (int)Math.Round(v[2]);
        return -1;
    }
}
