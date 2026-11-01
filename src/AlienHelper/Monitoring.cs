using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using AlienHelper.Hardware;

namespace AlienHelper;

/// <summary>
/// Single poller for every sensor. Only the sources shown by a visible window are read (<see cref="Want"/>);
/// with nothing on screen nothing is read, so the CPU stays idle and the dGPU can sleep.
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    public const int MinIntervalMs = 100, MaxIntervalMs = 10000;
    /// <summary>Current refresh period (Settings → Metrics refresh).</summary>
    public static int IntervalMs { get; private set; } = 2000;
    /// <summary>Histories cover a fixed time, so the number of samples follows the refresh period.</summary>
    public static int Samples(int seconds) => Math.Max(10, seconds * 1000 / IntervalMs);
    public const int WindowSeconds = 300;                    // charts: last 5 min
    public static int Capacity => Samples(WindowSeconds);
    public static int CoreCapacity => Samples(120);           // 2 min per core

    public sealed record GpuStats(int Load, int CoreMhz, int MemMhz, float PowerW, float VramUsedGb, float VramTotalGb, int MemTemp);

    readonly Controller _c;
    readonly CoreMonitor _cores = new();
    Nvml? _nvml;
    NvApi? _nvapi;
    CoreTemps? _coreTemps;   // per-core temperatures, only when the PawnIO driver is already installed
    /// <summary>Why per-core temperatures are missing ("" when they are read).</summary>
    public string CoreTempsStatus { get; private set; } = "";   // memory temperature, which NVML does not report on GeForce
    volatile bool _coresOpen;
    DateTime _lastDisks = DateTime.MinValue;

    public Controller.Snapshot? Last { get; private set; }
    public List<float> Cpu { get; } = new();
    public List<float> Gpu { get; } = new();
    public List<List<float>> Fans { get; } = new();
    public List<float> PClock { get; } = new();     // GHz
    public List<float> EClock { get; } = new();     // GHz
    public List<float> CpuPower { get; } = new();   // W
    public List<float> RamUsed { get; } = new();    // GB
    public float RamTotal { get; private set; }
    /// <summary>Drives that report a temperature, by physical drive number. Replaced as a whole when the set changes.</summary>
    public IReadOnlyList<Drive> Drives { get; private set; } = Array.Empty<Drive>();
    /// <summary>Battery: on battery its discharge rate is the laptop's total draw.</summary>
    public BatteryState? Battery { get; private set; }
    /// <summary>
    /// Total draw, W: the battery's discharge rate on battery; on AC the Intel platform energy counter (RAPL PSYS,
    /// MSR 0x64D, read through PawnIO) — the whole platform, not just the CPU; CPU + GPU when neither is available.
    /// </summary>
    public List<float> PowerTotal { get; } = new();
    public float PowerTotalW { get; private set; } = -1;
    public enum PowerSource { None, Battery, Platform, CpuGpu }
    public PowerSource PowerFrom { get; private set; }
    /// <summary>Platform power from RAPL PSYS, W (-1 when unavailable).</summary>
    public float PsysW { get; private set; } = -1;
    uint _psysRaw;
    long _psysAt = -1;
    double _psysUnit = -1;
    DateTime _lastBattery = DateTime.MinValue;

    /// <summary>Active network connections (up, with a gateway). Replaced as a whole when the set changes.</summary>
    public IReadOnlyList<NetLink> Links { get; private set; } = Array.Empty<NetLink>();
    DateTime _lastLinks = DateTime.MinValue;

    // detailed
    public IReadOnlyList<CoreMonitor.Core> Cores => _cores.Cores;
    public float CpuLoad { get; private set; } = -1;
    public float PClockMhz { get; private set; } = -1;
    public float EClockMhz { get; private set; } = -1;
    public float CpuPowerW { get; private set; } = -1;   // RAPL package power from the "Energy Meter" counters
    PerformanceCounter? _cpuPower;
    /// <summary>NVIDIA GPUs (NVML), in driver order; empty without an NVIDIA card. The first one is the main dGPU.</summary>
    public IReadOnlyList<GpuDevice> Gpus { get; private set; } = Array.Empty<GpuDevice>();
    /// <summary>The first GPU's latest reading (overlay, diagnostics).</summary>
    public GpuStats? GpuInfo => Gpus.Count > 0 ? Gpus[0].Stats : null;
    /// <summary>UI development aid (screenshot mode): extra fake GPUs and drives appended to the real ones.</summary>
    public int FakeGpus { get; set; }
    public int FakeDrives { get; set; }

    // What is read is decided by what is on screen: every window states the sources it shows (Want), and the
    // sampler reads only their union. With nothing on screen nothing is read at all, except the power mode every
    // 2 s while a fan boost is active (switching modes with the G key must be able to drop the boost).
    readonly Dictionary<object, Need> _demand = new();
    Need _once;

    /// <summary>Sets what <paramref name="owner"/> shows; <see cref="Need.None"/> when it is hidden or closed.</summary>
    public void Want(object owner, Need need)
    {
        lock (_demand)
        {
            if (need == Need.None) _demand.Remove(owner); else _demand[owner] = need;
        }
        _wake.Set();
    }

    /// <summary>One reading of these sources right away (e.g. the tray tooltip when the mouse is over the icon).</summary>
    public void Once(Need need)
    {
        lock (_demand) _once |= need;
        _wake.Set();
    }

    /// <summary>Sources wanted right now by everything on screen.</summary>
    public Need Demand
    {
        get { lock (_demand) return _demand.Values.Aggregate(Need.None, (x, y) => x | y); }
    }

    public event Action? Updated;

    /// <summary>
    /// Count of samples applied so far. A window that opens later shows only the ones after it opened, and graphs
    /// average long histories in buckets aligned to this count, so a bucket keeps its value as new samples arrive.
    /// </summary>
    public static long SampleNo { get; private set; }

    /// <summary>When the latest sample reached the UI (Stopwatch ms); charts use it to scroll smoothly between samples.</summary>
    public static long LastSampleMs { get; private set; }
    public static readonly Stopwatch Clock = Stopwatch.StartNew();
    /// <summary>Development aid (screenshot mode): start time and duration of every read, when not null.</summary>
    public static List<(long at, long ms, Need need)>? Trace;

    // Sampling runs on its own thread on a fixed schedule, so a busy UI never delays or skips a reading; every
    // sample is queued and applied to the histories on the UI thread in order.
    readonly Thread _thread;
    readonly AutoResetEvent _wake = new(false);
    readonly ConcurrentQueue<(Controller.Snapshot? snap, Need need)> _queue = new();
    readonly SynchronizationContext _ui;
    volatile bool _stop;
    int _posted;

    public SensorMonitor(Controller c)
    {
        _c = c;
        if (c.Awcc != null)
            foreach (var _ in c.Awcc.Fans) Fans.Add(new List<float>());
        IntervalMs = Math.Clamp(c.S.PollIntervalMs, MinIntervalMs, MaxIntervalMs);
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _thread = new Thread(Run) { IsBackground = true, Name = "AlienHelper sensors" };
        _thread.Start();
    }

    static void Push(List<float> list, float v, int cap = 0)
    {
        if (cap == 0) cap = Capacity;
        list.Add(v);
        while (list.Count > cap) list.RemoveAt(0);
    }

    void Run()
    {
        long next = Clock.ElapsedMilliseconds;
        while (!_stop)
        {
            Need need;
            lock (_demand) { need = _demand.Values.Aggregate(_once, (x, y) => x | y); _once = Need.None; }
            bool boost = _c.S.FanBoost > 0 || _c.S.FanBoostCpu > 0 || _c.S.FanBoostGpu > 0;
            if (need == Need.None && boost) need = Need.Mode;
            if (need == Need.None)
            {
                // nothing on screen and no boost to watch: sleep until a window asks for data
                _wake.WaitOne();
                next = Clock.ElapsedMilliseconds;
                continue;
            }
            Controller.Snapshot? snap = null;
            long t0 = Clock.ElapsedMilliseconds;
            try { snap = ReadSources(need); } catch (Exception ex) { Log.Warn("sensor read: " + ex.Message); }
            if (Trace != null) lock (Trace) Trace.Add((t0, Clock.ElapsedMilliseconds - t0, need));
            _queue.Enqueue((snap, need));
            if (Interlocked.Exchange(ref _posted, 1) == 0) _ui.Post(_ => ApplyQueued(), null);

            // only the mode is watched: every 2 s is plenty
            int period = need == Need.Mode ? Math.Max(IntervalMs, 2000) : IntervalMs;
            next += period;
            long now = Clock.ElapsedMilliseconds;
            if (next < now) next = now;                       // a slow read: carry on from now instead of bursting
            if (_wake.WaitOne((int)(next - now))) next = Clock.ElapsedMilliseconds;   // woken early: new consumer or interval
        }
    }

    /// <summary>Kept for callers that want a fresh reading right away (e.g. after switching the mode).</summary>
    public Task Poll() { _wake.Set(); return Task.CompletedTask; }

    /// <summary>
    /// One sensor source read on its own task. WMI (firmware, battery, disks) and performance counters have no timeout and
    /// now and then stop answering for many seconds; a source that hangs then holds up only itself, the rest keep updating
    /// and its last values stay on screen. A hang is logged with the source's name, and so is its recovery.
    /// </summary>
    sealed class Lane
    {
        readonly string _name;
        Task _task = Task.CompletedTask;
        long _since;
        bool _stuck, _failed;

        public Lane(string name) => _name = name;

        /// <summary>Starts a read unless the previous one is still running; returns the started task or null.</summary>
        public Task? Start(Action read)
        {
            long now = Clock.ElapsedMilliseconds;
            if (!_task.IsCompleted)
            {
                if (!_stuck && now - _since > 3000) { _stuck = true; Log.Warn($"sensors: '{_name}' has not answered for {now - _since} ms"); }
                return null;
            }
            if (_stuck) { _stuck = false; Log.Warn($"sensors: '{_name}' answered again after {now - _since} ms"); }
            _since = now;
            return _task = Task.Run(() =>
            {
                try { read(); _failed = false; }
                catch (Exception ex) { if (!_failed) Log.Warn($"sensors: '{_name}' failed: {ex.Message}"); _failed = true; }
            });
        }
    }

    readonly Lane _firmwareLane = new("firmware (fans, temperatures, mode)"), _cpuLane = new("cpu counters and core temperatures"),
        _cpuPowerLane = new("cpu power"), _gpuLane = new("nvidia"), _batteryLane = new("battery"), _psysLane = new("platform power"),
        _netLane = new("network"), _disksLane = new("disks");
    Controller.Snapshot? _snap;

    /// <summary>Background thread: reads the wanted sources once, each on its own lane, and waits for them briefly.</summary>
    Controller.Snapshot? ReadSources(Need need)
    {
        var started = new List<Task>();
        void Go(Lane lane, Action read) { if (lane.Start(read) is { } t) started.Add(t); }

        if (need.HasFlag(Need.Fans)) Go(_firmwareLane, () => _snap = _c.Read());
        else if (need.HasFlag(Need.Mode)) Go(_firmwareLane, () => _snap = _c.ReadMode());
        // Sources that are slow to open (≈50 counters, NVML, PawnIO, disk counters) open in the background, so a window
        // shows the fast ones at once and the rest join as soon as they are ready.
        bool cores = (need.HasFlag(Need.Cpu) || need.HasFlag(Need.CoreTemps)) && Ready(ref _coresInit, () => { _cores.Open(); _coresOpen = true; }) && _coresOpen;
        bool temps = need.HasFlag(Need.CoreTemps) && Ready(ref _coreTempsInit, OpenPawnIo) && _coreTemps != null && _coresOpen;
        if (cores || temps)
            Go(_cpuLane, () =>
            {
                if (cores) try { _cores.Sample(); } catch { }
                if (!temps) return;
                try
                {
                    var t = _coreTemps!.Read(Environment.ProcessorCount);
                    foreach (var core in _cores.Cores) core.Temp = core.Index < t.Length ? t[core.Index] : -1;
                }
                catch { }
            });
        if (need.HasFlag(Need.Cpu) && Ready(ref _powerInit, () =>
            {
                try { var pc = new PerformanceCounter("Energy Meter", "Power", "RAPL_Package0_PKG"); pc.NextValue(); _cpuPower = pc; } catch { _cpuPower = null; }
            }) && _cpuPower != null)
            Go(_cpuPowerLane, () => { try { CpuPowerW = _cpuPower.NextValue() / 1000f; } catch { CpuPowerW = -1; } });
        if (need.HasFlag(Need.Gpu) && Ready(ref _gpuInit, () =>
            {
                _nvml = Nvml.TryOpen();
                if (_nvml != null) _nvapi = NvApi.TryOpen();
                var list = _nvml?.Names.Select((n, i) => new GpuDevice(i, n) { Bus = _nvml.Bus(i) }).ToList() ?? new List<GpuDevice>();
                for (int i = 0; i < FakeGpus; i++) list.Add(new GpuDevice(list.Count, $"Fake GPU {i + 1}") { Fake = true });
                Gpus = list;
            }))
        {
            // NVML can take ~0.5 s when the dGPU has dozed off between reads: not waited for, so that never holds up the
            // sample. Each sample takes the latest GPU values that are ready.
            _gpuLane.Start(ReadGpus);
        }
        if (need.HasFlag(Need.Power) && DateTime.UtcNow - _lastBattery > TimeSpan.FromMilliseconds(900))
        {
            _lastBattery = DateTime.UtcNow;   // the gauge updates about once a second
            Go(_batteryLane, () => Battery = BatteryState.Read());
        }
        if (need.HasFlag(Need.Power) && Ready(ref _coreTempsInit, OpenPawnIo) && _coreTemps != null) Go(_psysLane, ReadPsys);
        if (need.HasFlag(Need.Net))
            Go(_netLane, () =>
            {
                if (DateTime.UtcNow - _lastLinks > TimeSpan.FromSeconds(10)) { _lastLinks = DateTime.UtcNow; UpdateLinks(); }
                foreach (var l in Links) l.Sample();
            });
        if (need.HasFlag(Need.Disks) && Ready(ref _drivesInit, () => { _lastDisks = DateTime.UtcNow; UpdateDrives(); }))
            Go(_disksLane, () =>
            {
                if (DateTime.UtcNow - _lastDisks > TimeSpan.FromSeconds(30)) { _lastDisks = DateTime.UtcNow; UpdateDrives(); }
                foreach (var d in Drives) d.SampleIo();
            });

        // normally every read is done in a few ms; one that hangs is left running and its last values are used
        if (started.Count > 0) Task.WaitAll(started.ToArray(), Math.Clamp(IntervalMs * 4 / 5, 50, 800));
        return need.HasFlag(Need.Fans) || need.HasFlag(Need.Mode) ? _snap : null;
    }

    /// <summary>PawnIO with the IntelMSR module: per-core temperatures and the platform energy counter.</summary>
    void OpenPawnIo()
    {
        _coreTemps = CoreTemps.TryOpen(out var why);
        CoreTempsStatus = why;
        Log.Info(_coreTemps != null ? $"PawnIO MSR reads available, TjMax {_coreTemps.TjMax}, module {_coreTemps.Source}" : "PawnIO MSR reads unavailable: " + why);
    }

    /// <summary>Platform power from the RAPL PSYS energy counter: energy since the last read over the time between.</summary>
    void ReadPsys()
    {
        try
        {
            if (_psysUnit < 0)
            {
                ulong unit = _coreTemps!.ReadMsr(0, 0x606);   // MSR_RAPL_POWER_UNIT: energy unit = 1 / 2^bits[12:8] J
                _psysUnit = _coreTemps.LastStatus < 0 ? 0 : 1.0 / Math.Pow(2, (unit >> 8) & 0x1F);
            }
            if (_psysUnit <= 0) return;
            uint raw = (uint)(_coreTemps!.ReadMsr(0, 0x64D) & 0xFFFFFFFF);   // MSR_PLATFORM_ENERGY_STATUS
            long now = Clock.ElapsedMilliseconds;
            if (_coreTemps.LastStatus < 0 || raw == 0) { PsysW = -1; return; }
            if (_psysAt >= 0 && now > _psysAt)
            {
                double w = unchecked(raw - _psysRaw) * _psysUnit / ((now - _psysAt) / 1000.0);   // wraps at 2^32
                PsysW = w is > 1 and < 1000 ? (float)w : -1;
            }
            _psysRaw = raw;
            _psysAt = now;
        }
        catch { PsysW = -1; }
    }
    Task? _coresInit, _coreTempsInit, _powerInit, _gpuInit, _drivesInit;

    /// <summary>Starts a one-time open in the background on first use; true once it has finished.</summary>
    static bool Ready(ref Task? init, Action open)
    {
        init ??= Task.Run(() => { try { open(); } catch { } });
        return init.IsCompleted;
    }

    /// <summary>Diagnostics: the GPU power limit in the log whenever it moves, with the mode and the load at that moment.</summary>
    void LogPowerLimit(GpuDevice gd, Nvml.Reading r)
    {
        long now = Clock.ElapsedMilliseconds;
        if (r.PowerLimitW <= 0 || Math.Abs(r.PowerLimitW - gd.LoggedLimit) < 5 || now - gd.LimitLoggedAt < 10000) return;
        Log.Info($"gpu power limit {gd.LoggedLimit:0} -> {r.PowerLimitW:0} W: mode 0x{Last?.Profile ?? -1:X2}, draw {r.PowerW:0} W, load {r.Load}%");
        gd.LoggedLimit = r.PowerLimitW;
        gd.LimitLoggedAt = now;
    }

    void ReadGpus()
    {
        foreach (var gd in Gpus)
        {
            try
            {
                if (gd.Fake) { var f = Gpus[0].Stats; gd.Stats = f; gd.Temp = Gpus[0].Temp; continue; }
                var r = _nvml!.Read(gd.Index);
                LogPowerLimit(gd, r);
                int mem = r.MemTemp > 0 ? r.MemTemp : _nvapi?.MemoryTemp(gd.Bus, gd.Index) ?? -1;
                gd.Stats = new GpuStats(r.Load, r.CoreMhz, r.MemMhz, r.PowerW, r.VramUsedGb, r.VramTotalGb, mem);
                gd.Temp = r.Temp;
            }
            catch { gd.Stats = null; }
        }
    }

    /// <summary>UI thread: moves all queued samples into the histories, then notifies once.</summary>
    void ApplyQueued()
    {
        Volatile.Write(ref _posted, 0);
        bool any = false;
        while (_queue.TryDequeue(out var item)) { Apply(item.snap, item.need); any = true; }
        if (!any) return;
        LastSampleMs = Clock.ElapsedMilliseconds;
        Updated?.Invoke();
    }

    void Apply(Controller.Snapshot? snap, Need need)
    {
        SampleNo++;
        if (snap != null)
        {
            Last = snap;
            _c.NotifyProfile(snap.Profile);
            int Temp(string name) => snap.Temps.FirstOrDefault(t => t.s.Name == name).temp;
            int cpu = Temp("CPU"), gpu = Temp("GPU");
            if (cpu > 0) Push(Cpu, cpu);
            if (gpu > 0) Push(Gpu, gpu);
            for (int i = 0; i < snap.Fans.Count && i < Fans.Count; i++)
                if (snap.Fans[i].rpm >= 0) Push(Fans[i], snap.Fans[i].rpm);
        }
        if (need.HasFlag(Need.Cpu) && _coresOpen)
        {
            CpuLoad = _cores.TotalLoad;
            foreach (var core in _cores.Cores)
            {
                Push(core.History, core.Load, CoreCapacity);
                if (need.HasFlag(Need.CoreTemps) && core.Temp > 0) Push(core.Temps, core.Temp, CoreCapacity);
            }
            var p = _cores.Cores.Where(x => x.Performance).Select(x => x.Mhz).DefaultIfEmpty(-1).Max();
            var e = _cores.Cores.Where(x => !x.Performance).Select(x => x.Mhz).DefaultIfEmpty(-1).Max();
            PClockMhz = p; EClockMhz = e;
            if (p > 0) Push(PClock, p / 1000f);
            if (e > 0) Push(EClock, e / 1000f);
        }
        if (need.HasFlag(Need.Gpu))
            foreach (var gd in Gpus)
            {
                if (gd.Stats is not { } st) continue;
                if (gd.Temp > 0) Push(gd.Temps, gd.Temp);
                if (st.CoreMhz > 0) Push(gd.Clock, st.CoreMhz / 1000f);
                if (st.PowerW > 0) Push(gd.Power, st.PowerW);
                if (st.VramUsedGb > 0) Push(gd.Vram, st.VramUsedGb);
                if (st.MemTemp > 0) Push(gd.MemTemps, st.MemTemp);
            }
        if (need.HasFlag(Need.Cpu))
        {
            if (CpuPowerW > 0) Push(CpuPower, CpuPowerW);
            var (ru, rt) = SystemMemory.Read();
            if (rt > 0) { Push(RamUsed, ru); RamTotal = rt; }
        }
        if (need.HasFlag(Need.Power))
        {
            var b = Battery;
            float gpuW = Gpus.Sum(x => x.Stats is { PowerW: > 0 } st ? st.PowerW : 0);
            (PowerTotalW, PowerFrom) = b is { OnAc: false, DischargeW: > 0 } ? (b.DischargeW, PowerSource.Battery)
                : PsysW > 0 ? (PsysW, PowerSource.Platform)
                : CpuPowerW > 0 ? (CpuPowerW + gpuW, PowerSource.CpuGpu) : (-1f, PowerSource.None);
            if (PowerTotalW > 0) Push(PowerTotal, PowerTotalW);
        }
        if (need.HasFlag(Need.Disks))
            foreach (var d in Drives)
            {
                if (d.Temp > 0) Push(d.Temps, d.Temp);
                if (d.ReadIops >= 0) { Push(d.Reads, d.ReadIops); Push(d.Writes, d.WriteIops); }
            }
        if (need.HasFlag(Need.Net))
            foreach (var l in Links)
                if (l.DownMbps >= 0) { Push(l.Down, l.DownMbps); Push(l.Up, l.UpMbps); }
    }

    /// <summary>Changes the refresh period. Histories start over, since their samples were spaced differently.</summary>
    public void SetInterval(int ms)
    {
        ms = Math.Clamp(ms, MinIntervalMs, MaxIntervalMs);
        if (ms == IntervalMs) return;
        IntervalMs = ms;
        _wake.Set();
        foreach (var l in new[] { Cpu, Gpu, PClock, EClock, CpuPower, RamUsed }.Concat(Fans)) l.Clear();
        foreach (var core in _cores.Cores) { core.History.Clear(); core.Temps.Clear(); }
        foreach (var gd in Gpus) foreach (var l in new[] { gd.Temps, gd.Clock, gd.Power, gd.Vram, gd.MemTemps }) l.Clear();
        foreach (var d in Drives) foreach (var l in new[] { d.Temps, d.Reads, d.Writes }) l.Clear();
        foreach (var n in Links) { n.Down.Clear(); n.Up.Clear(); }
        PowerTotal.Clear();
        Log.Info($"metrics refresh -> {ms} ms");
        Changed?.Invoke();
    }

    /// <summary>Raised after the refresh period changed (histories were cleared).</summary>
    public event Action? Changed;

    /// <summary>Finds the active connections: up, not loopback or tunnel, with a default gateway (background thread).</summary>
    void UpdateLinks()
    {
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); } catch { return; }
        var active = all.Where(n => n.OperationalStatus == OperationalStatus.Up
            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            && HasGateway(n)).OrderBy(n => n.Name).ToList();
        var current = Links;
        if (active.Select(n => n.Id).SequenceEqual(current.Select(l => l.Id))) return;
        Links = active.Select(n => current.FirstOrDefault(l => l.Id == n.Id) ?? new NetLink(n)).ToList();
    }

    static bool HasGateway(NetworkInterface n)
    {
        try { return n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)); }
        catch { return false; }
    }

    /// <summary>Reads drive temperatures; a drive that appears gets its own IOPS counters (background thread).</summary>
    void UpdateDrives()
    {
        var temps = DiskTemps.Read();
        var current = Drives;
        if (temps.Count + FakeDrives != current.Count || temps.Any(t => current.All(d => d.Index != t.index)))
        {
            string[] instances;
            try { instances = new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames(); } catch { instances = Array.Empty<string>(); }
            var next = temps.OrderBy(t => t.index).Select(t =>
                current.FirstOrDefault(d => d.Index == t.index) ?? new Drive(t.index, t.name, instances)).ToList();
            for (int i = 0; i < FakeDrives; i++)
                next.Add(current.FirstOrDefault(d => d.Index == 10 + i) ?? new Drive(10 + i, $"Fake SSD {i + 1}", new[] { $"{10 + i} X{i}:" }));
            foreach (var gone in current.Except(next)) gone.Dispose();
            Drives = next;
        }
        foreach (var d in Drives) d.Temp = d.Index >= 10 ? 40 + d.Index : temps.FirstOrDefault(t => t.index == d.Index).temp;
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread.Join(1000);   // let a read in progress finish before its sources go away
        _cores.Dispose();
        _nvml?.Dispose();
        _coreTemps?.Dispose();
        _cpuPower?.Dispose();
        foreach (var d in Drives) d.Dispose();
    }
}

/// <summary>Sensor sources a window can ask for (<see cref="SensorMonitor.Want"/>).</summary>
[Flags]
public enum Need
{
    None = 0,
    /// <summary>Power mode only: one firmware call.</summary>
    Mode = 1,
    /// <summary>Fan speeds, CPU/GPU temperatures and the mode (firmware).</summary>
    Fans = 2,
    /// <summary>Per-core load and clocks, package power, RAM (Windows counters).</summary>
    Cpu = 4,
    /// <summary>Per-core temperatures (PawnIO).</summary>
    CoreTemps = 8,
    /// <summary>NVIDIA GPUs (NVML, NVAPI); wakes the dGPU.</summary>
    Gpu = 16,
    Disks = 32,
    Net = 64,
    /// <summary>Battery gauge (total draw on battery).</summary>
    Power = 128,
    All = Mode | Fans | Cpu | CoreTemps | Gpu | Disks | Net | Power,
}

/// <summary>Helpers for small sparklines.</summary>
public static class HistoryExtensions
{
    /// <summary>The last <paramref name="seconds"/> of a history (the tile spaces them by the same window).</summary>
    public static List<float> Recent(this IEnumerable<float> history, int seconds) =>
        history.TakeLast(SensorMonitor.Samples(seconds)).ToList();
}

/// <summary>One NVIDIA GPU: latest NVML reading and its histories.</summary>
public sealed class GpuDevice
{
    public int Index { get; }
    /// <summary>Short model name, e.g. "RTX 5080 Laptop".</summary>
    public string Name { get; }
    public bool Fake { get; init; }
    public SensorMonitor.GpuStats? Stats { get; set; }
    public int Temp { get; set; } = -1;
    public List<float> Temps { get; } = new();   // °C (NVML)
    public List<float> Clock { get; } = new();   // GHz
    internal float LoggedLimit;      // power limit last written to the log
    internal long LimitLoggedAt = -10000;
    public List<float> Power { get; } = new();   // W
    public List<float> Vram { get; } = new();    // GB
    public List<float> MemTemps { get; } = new();   // °C, VRAM (NVAPI)
    public uint Bus { get; init; } = uint.MaxValue;

    public GpuDevice(int index, string name) { Index = index; Name = SystemInfo.ShortName(name); }
}

/// <summary>One network connection: download / upload speed from the interface byte counters.</summary>
public sealed class NetLink
{
    readonly NetworkInterface _nic;
    long _rx = -1, _tx = -1, _at;
    public string Id => _nic.Id;
    /// <summary>Connection name as in Windows, e.g. "Wi-Fi".</summary>
    public string Name => _nic.Name;
    /// <summary>Adapter model, e.g. "Killer Wi-Fi 7 BE1750w…".</summary>
    public string Description => _nic.Description;
    /// <summary>Negotiated link speed, Mbit/s.</summary>
    public float LinkMbps { get; private set; }
    public float DownMbps { get; private set; } = -1;
    public float UpMbps { get; private set; } = -1;
    public List<float> Down { get; } = new();   // Mbit/s
    public List<float> Up { get; } = new();     // Mbit/s

    public NetLink(NetworkInterface nic) { _nic = nic; }

    public void Sample()
    {
        try
        {
            var st = _nic.GetIPStatistics();
            long rx = st.BytesReceived, tx = st.BytesSent, now = SensorMonitor.Clock.ElapsedMilliseconds;
            LinkMbps = _nic.Speed > 0 ? _nic.Speed / 1e6f : 0;
            if (_rx >= 0 && now > _at)
            {
                float sec = (now - _at) / 1000f;
                DownMbps = Math.Max(0, rx - _rx) * 8 / 1e6f / sec;
                UpMbps = Math.Max(0, tx - _tx) * 8 / 1e6f / sec;
            }
            _rx = rx; _tx = tx; _at = now;
        }
        catch { DownMbps = UpMbps = -1; }
    }
}

/// <summary>One physical drive: temperature (IOCTL) and read / write operations per second (PhysicalDisk counters).</summary>
public sealed class Drive : IDisposable
{
    public int Index { get; }
    public string Model { get; }
    /// <summary>Drive letters on this disk, e.g. "C:" or "E: F:"; empty when it has none.</summary>
    public string Letters { get; }
    public int Temp { get; set; }
    public float ReadIops { get; private set; } = -1;
    public float WriteIops { get; private set; } = -1;
    public List<float> Temps { get; } = new();
    public List<float> Reads { get; } = new();
    public List<float> Writes { get; } = new();
    readonly PerformanceCounter? _r, _w;

    public Drive(int index, string model, string[] instances)
    {
        Index = index; Model = model;
        // PhysicalDisk instances are named "<number> <letters>", e.g. "0 C:", "1", "2 E: F:"
        string num = index.ToString();
        string? inst = instances.FirstOrDefault(n => n == num || n.StartsWith(num + " "));
        Letters = inst != null && inst.Length > num.Length ? inst[(num.Length + 1)..].Trim() : "";
        if (inst == null) return;
        try
        {
            _r = new PerformanceCounter("PhysicalDisk", "Disk Reads/sec", inst); _r.NextValue();
            _w = new PerformanceCounter("PhysicalDisk", "Disk Writes/sec", inst); _w.NextValue();
        }
        catch { _r = _w = null; }
    }

    /// <summary>Tile and chart name: "SSD 1 · C:", or "SSD 2" for a drive without letters.</summary>
    public string Name => Letters.Length > 0 ? $"SSD {Index + 1} · {Letters}" : $"SSD {Index + 1}";
    /// <summary>Compact name for crowded legends: the letters, or "SSD 2".</summary>
    public string Label => Letters.Length > 0 ? Letters : $"SSD {Index + 1}";

    public void SampleIo()
    {
        try
        {
            if (_r != null && _w != null) { ReadIops = _r.NextValue(); WriteIops = _w.NextValue(); }
        }
        catch { ReadIops = WriteIops = -1; }
    }

    public void Dispose() { _r?.Dispose(); _w?.Dispose(); }
}

/// <summary>Per-core load and clock from Windows performance counters. Opened lazily by SensorMonitor.</summary>
public sealed class CoreMonitor : IDisposable
{
    public sealed class Core
    {
        public int Index { get; init; }
        public bool Performance { get; init; }
        public PerformanceCounter Util { get; init; } = null!;
        public PerformanceCounter Perf { get; init; } = null!;
        public PerformanceCounter Base { get; init; } = null!;   // P- and E-cores have different base clocks
        public float Load, Mhz;
        public List<float> History { get; } = new();
        /// <summary>Digital thermal sensor, °C; -1 without PawnIO.</summary>
        public int Temp = -1;
        public List<float> Temps { get; } = new();
    }

    public List<Core> Cores { get; } = new();
    public float TotalLoad { get; private set; }
    PerformanceCounter? _total;

    /// <summary>Slow (creates ~50 counters); call off the UI thread.</summary>
    public void Open()
    {
        const string cat = "Processor Information";
        var eff = EfficiencyClasses();
        byte maxClass = eff.Count > 0 ? eff.Values.Max() : (byte)0;
        _total = new PerformanceCounter(cat, "% Processor Utility", "_Total");
        int n = Environment.ProcessorCount;
        for (int i = 0; i < n; i++)
        {
            string inst = $"0,{i}";
            Cores.Add(new Core
            {
                Index = i,
                Performance = eff.TryGetValue(i, out var c) && c == maxClass && maxClass > 0,
                Util = new PerformanceCounter(cat, "% Processor Utility", inst),
                Perf = new PerformanceCounter(cat, "% Processor Performance", inst),
                Base = new PerformanceCounter(cat, "Processor Frequency", inst),
            });
        }
        Sample();
    }

    public void Sample()
    {
        if (_total == null) return;
        TotalLoad = Math.Clamp(_total.NextValue(), 0, 100);
        foreach (var c in Cores)
        {
            c.Load = Math.Clamp(c.Util.NextValue(), 0, 100);
            c.Mhz = c.Base.NextValue() * c.Perf.NextValue() / 100f;
        }
    }

    public void Dispose()
    {
        _total?.Dispose();
        foreach (var c in Cores) { c.Util.Dispose(); c.Perf.Dispose(); c.Base.Dispose(); }
        Cores.Clear();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemCpuSetInformation(IntPtr info, int length, out int returned, IntPtr process, int flags);

    /// <summary>Logical processor index → efficiency class (higher = P-core on hybrid Intel CPUs).</summary>
    static Dictionary<int, byte> EfficiencyClasses()
    {
        var map = new Dictionary<int, byte>();
        GetSystemCpuSetInformation(IntPtr.Zero, 0, out int len, IntPtr.Zero, 0);
        if (len <= 0) return map;
        var buf = Marshal.AllocHGlobal(len);
        try
        {
            if (!GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) return map;
            for (int off = 0; off < len;)
            {
                int size = Marshal.ReadInt32(buf, off);
                if (size <= 0) break;
                // SYSTEM_CPU_SET_INFORMATION: Size, Type, then CpuSet { Id, Group(2), LogicalProcessorIndex, CoreIndex, LLC, Numa, EfficiencyClass }
                short group = Marshal.ReadInt16(buf, off + 12);
                byte logical = Marshal.ReadByte(buf, off + 14);
                byte eff = Marshal.ReadByte(buf, off + 18);
                if (group == 0) map[logical] = eff;
                off += size;
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return map;
    }
}

public static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    /// <summary>(used GB, total GB)</summary>
    public static (float used, float total) Read()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (-1, -1);
        const float gb = 1073741824f;
        return ((m.ullTotalPhys - m.ullAvailPhys) / gb, m.ullTotalPhys / gb);
    }
}
