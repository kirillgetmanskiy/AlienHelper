using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AlienHelper.Hardware;

/// <summary>
/// Per-core Intel CPU temperatures from the digital thermal sensors (MSR IA32_THERM_STATUS 0x19C, TjMax from
/// IA32_TEMPERATURE_TARGET 0x1A2). Windows has no API for them, so this needs the signed PawnIO driver and its
/// IntelMSR module. The module is built into the app; the driver is not installed by it: it is used when present
/// (its own installer, or another app such as CapFrameX). Read-only.
/// </summary>
public sealed class CoreTemps : IDisposable
{
    const uint MsrThermStatus = 0x19C, MsrTempTarget = 0x1A2;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OpenFn(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int LoadFn(IntPtr handle, byte[] blob, nint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int ExecuteFn(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string name, ulong[] input, nint inSize, ulong[] output, nint outSize, out nint returned);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CloseFn(IntPtr handle);

    readonly IntPtr _h;
    readonly ExecuteFn _exec;
    readonly CloseFn _close;
    readonly object _lock = new();

    /// <summary>TjMax in °C (temperatures are TjMax minus the sensor's distance to it).</summary>
    public int TjMax { get; private set; }
    /// <summary>Where the module came from, for diagnostics.</summary>
    public string Source { get; }

    CoreTemps(IntPtr h, ExecuteFn exec, CloseFn close, string source)
    { _h = h; _exec = exec; _close = close; Source = source; }

    /// <summary>Opens PawnIO with the IntelMSR module, or returns null and says why.</summary>
    public static CoreTemps? TryOpen(out string why)
    {
        try
        {
            if (!IsIntel()) { why = "not an Intel CPU"; return null; }
            var dir = PawnIoDir();
            if (dir == null) { why = "PawnIO driver not installed"; return null; }
            var libPath = Path.Combine(dir, "PawnIOLib.dll");
            if (!File.Exists(libPath)) libPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll");
            if (!File.Exists(libPath) || !NativeLibrary.TryLoad(libPath, out var lib)) { why = "PawnIOLib.dll not found"; return null; }
            var (blob, source) = FindModule();
            if (blob == null) { why = "IntelMSR module not found"; return null; }

            var open = Marshal.GetDelegateForFunctionPointer<OpenFn>(NativeLibrary.GetExport(lib, "pawnio_open"));
            var load = Marshal.GetDelegateForFunctionPointer<LoadFn>(NativeLibrary.GetExport(lib, "pawnio_load"));
            var exec = Marshal.GetDelegateForFunctionPointer<ExecuteFn>(NativeLibrary.GetExport(lib, "pawnio_execute"));
            var close = Marshal.GetDelegateForFunctionPointer<CloseFn>(NativeLibrary.GetExport(lib, "pawnio_close"));
            int hr = open(out var h);
            if (hr < 0) { why = $"pawnio_open 0x{hr:X8}"; return null; }
            hr = load(h, blob, blob.Length);
            if (hr < 0) { close(h); why = $"pawnio_load 0x{hr:X8}"; return null; }

            var ct = new CoreTemps(h, exec, close, source);
            int tj = (int)((ct.ReadMsrOn(0, MsrTempTarget) >> 16) & 0xFF);
            ct.TjMax = tj is >= 70 and <= 125 ? tj : 100;
            why = "";
            return ct;
        }
        catch (Exception ex) { why = ex.Message; return null; }
    }

    /// <summary>Temperature of each logical processor in °C (-1 when its reading is not valid).</summary>
    public int[] Read(int count)
    {
        var res = new int[count];
        for (int i = 0; i < count; i++)
        {
            ulong v = ReadMsrOn(i, MsrThermStatus);
            bool valid = (v & (1UL << 31)) != 0;
            int toMax = (int)((v >> 16) & 0x7F);
            res[i] = valid ? TjMax - toMax : -1;
        }
        return res;
    }

    /// <summary>Reads an MSR on one logical processor: the calling thread is pinned to it for the read.</summary>
    public ulong ReadMsr(int cpu, uint msr) => ReadMsrOn(cpu, msr);

    /// <summary>Last NVAPI-style status of an MSR read (0 = OK), for diagnostics.</summary>
    public int LastStatus { get; private set; }

    ulong ReadMsrOn(int cpu, uint msr)
    {
        lock (_lock)
        {
            var thread = GetCurrentThread();
            var old = SetThreadAffinityMask(thread, (UIntPtr)(1UL << cpu));
            try
            {
                var outBuf = new ulong[1];
                int hr = _exec(_h, "ioctl_read_msr", new ulong[] { msr }, 1, outBuf, 1, out _);
                LastStatus = hr;
                return hr < 0 ? 0 : outBuf[0];
            }
            finally { if (old != UIntPtr.Zero) SetThreadAffinityMask(thread, old); }
        }
    }

    public void Dispose() { try { _close(_h); } catch { } }

    static bool IsIntel()
    {
        try
        {
            using var q = new System.Management.ManagementObjectSearcher("SELECT Manufacturer FROM Win32_Processor");
            return q.Get().Cast<System.Management.ManagementObject>().Any(o => (o["Manufacturer"]?.ToString() ?? "").Contains("Intel"));
        }
        catch { return false; }
    }

    /// <summary>Folder of the installed PawnIO driver (its service ImagePath), or null.</summary>
    static string? PawnIoDir()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        if (key?.GetValue("ImagePath") is not string path) return null;
        path = path.Replace(@"\??\", "").Trim('"');
        if (path.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path);
        return Path.GetDirectoryName(path);
    }

    /// <summary>The signed IntelMSR module: a file next to AlienHelper.exe, or the copy built into the app.</summary>
    static (byte[]? blob, string source) FindModule()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "IntelMSR.bin");
        if (File.Exists(local)) return (File.ReadAllBytes(local), local);
        using var res = typeof(CoreTemps).Assembly.GetManifestResourceStream("AlienHelper.IntelMSR.bin");
        if (res == null) return (null, "");
        using var ms = new MemoryStream();
        res.CopyTo(ms);
        return (ms.ToArray(), "built in");
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
}
