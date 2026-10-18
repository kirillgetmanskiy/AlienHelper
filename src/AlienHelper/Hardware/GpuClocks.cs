using System.Runtime.InteropServices;

namespace AlienHelper.Hardware;

/// <summary>
/// NVIDIA GPU clock offsets (core and memory) through NVAPI's performance-state table (GetPstates20 / SetPstates20),
/// the interface overclocking tools use. Offsets apply to the P0 state on top of the driver's own boost curve and
/// last until they are reset or the driver restarts; AlienHelper resets them on exit and when Custom ends.
/// </summary>
public sealed class GpuClocks
{
    const uint IdInitialize = 0x0150E828, IdEnumPhysicalGpus = 0xE5AC921F, IdGetBusId = 0x1BE0B8E5,
        IdGetPstates20 = 0x6FF81213, IdSetPstates20 = 0x0F4DAE6B;

    // NV_GPU_PERF_PSTATES20_INFO layout (V1 = 7316 bytes; V2/V3 add a 100-byte over-voltage block)
    const int SizeV1 = 7316, SizeV2 = 7416, PstateSize = 456, ClockSize = 44, PstatesAt = 20, ClocksAt = 8;
    public const int DomainGraphics = 0, DomainMemory = 4;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr QueryInterfaceFn(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumGpusFn([Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int BusIdFn(IntPtr gpu, out uint bus);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PstatesFn(IntPtr gpu, IntPtr info);

    readonly IntPtr _gpu;
    readonly PstatesFn _get, _set;

    /// <summary>One clock of the P0 state: whether it may be offset, the allowed range and the current offset (MHz).</summary>
    public sealed record Clock(int Domain, bool Editable, int OffsetMhz, int MinMhz, int MaxMhz, int BaseMhz);

    GpuClocks(IntPtr gpu, PstatesFn get, PstatesFn set) { _gpu = gpu; _get = get; _set = set; }

    /// <summary>The first NVIDIA GPU (or the one on <paramref name="bus"/>), or null when NVAPI is missing.</summary>
    public static GpuClocks? TryOpen(out string why, uint bus = uint.MaxValue)
    {
        why = "";
        try
        {
            if (!NativeLibrary.TryLoad("nvapi64.dll", out var lib)) { why = "nvapi64.dll not found"; return null; }
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterfaceFn>(NativeLibrary.GetExport(lib, "nvapi_QueryInterface"));
            T? Fn<T>(uint id) where T : Delegate { var p = query(id); return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p); }
            var init = Fn<InitializeFn>(IdInitialize);
            var enumGpus = Fn<EnumGpusFn>(IdEnumPhysicalGpus);
            var busId = Fn<BusIdFn>(IdGetBusId);
            var get = Fn<PstatesFn>(IdGetPstates20);
            var set = Fn<PstatesFn>(IdSetPstates20);
            if (init == null || enumGpus == null || get == null || set == null) { why = "NVAPI functions missing"; return null; }
            if (init() != 0) { why = "NvAPI_Initialize failed"; return null; }
            var handles = new IntPtr[64];
            if (enumGpus(handles, out int n) != 0 || n <= 0) { why = "no NVIDIA GPU"; return null; }
            int pick = 0;
            if (bus != uint.MaxValue && busId != null)
                for (int i = 0; i < n; i++) if (busId(handles[i], out uint b) == 0 && b == bus) { pick = i; break; }
            return new GpuClocks(handles[pick], get, set);
        }
        catch (Exception ex) { why = ex.Message; return null; }
    }

    /// <summary>NVAPI status of the last call (0 = OK), for diagnostics.</summary>
    public int LastStatus { get; private set; }

    /// <summary>The P0 clocks with their offsets and allowed ranges; null when the driver refuses.</summary>
    public List<Clock>? Read()
    {
        var buf = Marshal.AllocHGlobal(SizeV2);
        try
        {
            foreach (var (size, ver) in new[] { (SizeV2, 3), (SizeV2, 2), (SizeV1, 1) })
            {
                Zero(buf, SizeV2);
                Marshal.WriteInt32(buf, 0, size | (ver << 16));
                LastStatus = _get(_gpu, buf);
                if (LastStatus != 0) continue;
                int numP = Marshal.ReadInt32(buf, 8), numC = Marshal.ReadInt32(buf, 12);
                for (int p = 0; p < Math.Min(numP, 16); p++)
                {
                    int pAt = PstatesAt + p * PstateSize;
                    if (Marshal.ReadInt32(buf, pAt) != 0) continue;   // P0 only
                    var list = new List<Clock>();
                    for (int c = 0; c < Math.Min(numC, 8); c++)
                    {
                        int at = pAt + ClocksAt + c * ClockSize;
                        list.Add(new Clock(
                            Domain: Marshal.ReadInt32(buf, at),
                            Editable: (Marshal.ReadInt32(buf, at + 8) & 1) != 0,
                            OffsetMhz: Marshal.ReadInt32(buf, at + 12) / 1000,
                            MinMhz: Marshal.ReadInt32(buf, at + 16) / 1000,
                            MaxMhz: Marshal.ReadInt32(buf, at + 20) / 1000,
                            BaseMhz: Marshal.ReadInt32(buf, at + 24) / 1000));
                    }
                    return list;
                }
                return new List<Clock>();
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// Sets the P0 core and memory offsets (MHz) after clamping them to the ranges the driver reports; 0/0 restores
    /// the stock clocks. Returns false when the driver refuses (LastStatus says why).
    /// </summary>
    public bool Apply(int coreMhz, int memMhz)
    {
        var clocks = Read();
        if (clocks == null) return false;
        var core = clocks.FirstOrDefault(c => c.Domain == DomainGraphics);
        var mem = clocks.FirstOrDefault(c => c.Domain == DomainMemory);
        var entries = new List<(int domain, int kHz)>();
        if (core is { Editable: true }) entries.Add((DomainGraphics, Math.Clamp(coreMhz, core.MinMhz, core.MaxMhz) * 1000));
        if (mem is { Editable: true }) entries.Add((DomainMemory, Math.Clamp(memMhz, mem.MinMhz, mem.MaxMhz) * 1000));
        if (entries.Count == 0) { LastStatus = -104; return false; }   // NVAPI_NOT_SUPPORTED-like: nothing editable

        var buf = Marshal.AllocHGlobal(SizeV1);
        try
        {
            Zero(buf, SizeV1);
            Marshal.WriteInt32(buf, 0, SizeV1 | (1 << 16));
            Marshal.WriteInt32(buf, 8, 1);                 // one P-state
            Marshal.WriteInt32(buf, 12, entries.Count);    // clocks in it
            Marshal.WriteInt32(buf, 16, 0);                // no base voltages
            int pAt = PstatesAt;
            Marshal.WriteInt32(buf, pAt, 0);               // P0
            for (int i = 0; i < entries.Count; i++)
            {
                int at = pAt + ClocksAt + i * ClockSize;
                Marshal.WriteInt32(buf, at, entries[i].domain);
                Marshal.WriteInt32(buf, at + 12, entries[i].kHz);
            }
            LastStatus = _set(_gpu, buf);
            return LastStatus == 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public bool Reset() => Apply(0, 0);

    /// <summary>
    /// <see cref="Apply"/> with retries: a dozing dGPU refuses the table for a moment (NVAPI −220), so wake it through
    /// <paramref name="wake"/> (an NVML read) and try again for up to ~5 s.
    /// </summary>
    public bool ApplyWithRetry(int coreMhz, int memMhz, Action? wake = null)
    {
        for (int i = 0; i < 10; i++)
        {
            try { wake?.Invoke(); } catch { }
            if (Apply(coreMhz, memMhz)) return true;
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>Current core and memory offsets (MHz), retrying while the dGPU wakes; null when unreadable.</summary>
    public (int core, int mem)? Offsets(Action? wake = null)
    {
        for (int i = 0; i < 10; i++)
        {
            try { wake?.Invoke(); } catch { }
            var c = Read();
            if (c != null)
                return (c.FirstOrDefault(x => x.Domain == DomainGraphics)?.OffsetMhz ?? 0, c.FirstOrDefault(x => x.Domain == DomainMemory)?.OffsetMhz ?? 0);
            Thread.Sleep(500);
        }
        return null;
    }

    static void Zero(IntPtr p, int size) { for (int i = 0; i < size; i += 4) Marshal.WriteInt32(p, i, 0); }
}
