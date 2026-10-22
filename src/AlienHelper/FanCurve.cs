namespace AlienHelper;

/// <summary>
/// Software fan curve: temperature (°C) → fan boost (%), piecewise linear between the points.
/// The boost only adds speed above the BIOS curve, like the slider; 0 leaves the fans to the BIOS.
/// </summary>
public static class FanCurve
{
    public const int MinTemp = 30, MaxTemp = 100;

    public static List<int[]> DefaultCpu() => new() { new[] { 50, 0 }, new[] { 65, 15 }, new[] { 75, 35 }, new[] { 85, 65 }, new[] { 95, 100 } };
    public static List<int[]> DefaultGpu() => new() { new[] { 45, 0 }, new[] { 60, 15 }, new[] { 70, 35 }, new[] { 80, 65 }, new[] { 87, 100 } };

    /// <summary>Built-in profiles, by id; the names come from L10n ("curve.&lt;id&gt;").</summary>
    public static readonly (string id, List<int[]> cpu, List<int[]> gpu)[] Presets =
    {
        ("quiet",       P((60, 0), (75, 10), (85, 35), (95, 80)),               P((55, 0), (70, 10), (80, 35), (87, 80))),
        ("balanced",    DefaultCpu(),                                            DefaultGpu()),
        ("performance", P((40, 0), (55, 20), (70, 50), (80, 80), (90, 100)),     P((40, 0), (52, 20), (65, 50), (75, 80), (83, 100))),
        ("max",         P((30, 40), (50, 70), (65, 100)),                        P((30, 40), (50, 70), (62, 100))),
    };

    static List<int[]> P(params (int t, int b)[] pts) => pts.Select(p => new[] { p.t, p.b }).ToList();

    public static List<int[]> Copy(IEnumerable<int[]> pts) => pts.Select(p => new[] { p[0], p[1] }).ToList();

    public static bool Same(IReadOnlyList<int[]> a, IReadOnlyList<int[]> b) =>
        a.Count == b.Count && a.Zip(b).All(x => x.First[0] == x.Second[0] && x.First[1] == x.Second[1]);

    /// <summary>Boost for a temperature: flat before the first and after the last point, linear in between.</summary>
    public static float Eval(IReadOnlyList<int[]> pts, float temp)
    {
        if (pts.Count == 0) return 0;
        if (temp <= pts[0][0]) return pts[0][1];
        for (int i = 1; i < pts.Count; i++)
        {
            if (temp > pts[i][0]) continue;
            var (a, b) = (pts[i - 1], pts[i]);
            float t = (temp - a[0]) / Math.Max(1f, b[0] - a[0]);
            return a[1] + (b[1] - a[1]) * t;
        }
        return pts[^1][1];
    }

    /// <summary>Points sorted by temperature, at least 1 °C apart, inside the chart's range.</summary>
    public static List<int[]> Clean(IEnumerable<int[]>? pts, Func<List<int[]>> fallback)
    {
        var list = (pts ?? Array.Empty<int[]>()).Where(p => p is { Length: 2 })
            .Select(p => new[] { Math.Clamp(p[0], MinTemp, MaxTemp), Math.Clamp(p[1], 0, 100) })
            .OrderBy(p => p[0]).ToList();
        for (int i = 1; i < list.Count; i++) if (list[i][0] <= list[i - 1][0]) list[i][0] = list[i - 1][0] + 1;
        return list.Count >= 2 && list[^1][0] <= MaxTemp ? list : fallback();
    }
}

/// <summary>A user's own pair of curves, saved under a name.</summary>
public sealed class CurveProfile
{
    public string Name { get; set; } = "";
    public List<int[]> Cpu { get; set; } = new();
    public List<int[]> Gpu { get; set; } = new();
}

/// <summary>
/// Drives the CPU and GPU fan boost from their curves while Custom is on. Temperatures are smoothed (~3 s), the boost
/// rises up to 15 %/s and falls up to 5 %/s, so the fans ramp instead of jumping, and the firmware is written at most
/// once a second per group (a 1 % drift only every 3 s). This holds on every base mode, Alien Mode included.
/// </summary>
public sealed class FanCurveDriver : IDisposable
{
    const float UpPerSec = 15f, DownPerSec = 5f, TempTau = 3f;

    sealed class Group
    {
        public byte Sensor;
        public string Name = "";
        public float Temp = -1, Boost, Target;
        public int Written = -1;
        public long WrittenAt;
        public volatile bool Busy;
    }

    readonly Controller _c;
    readonly SensorMonitor _mon;
    readonly Group _cpu = new() { Sensor = Controller.CpuSensor, Name = "CPU" };
    readonly Group _gpu = new() { Sensor = Controller.GpuSensor, Name = "GPU" };
    long _last = -1;

    /// <summary>Firmware write; the offline test swaps in a recorder (and runs it synchronously).</summary>
    internal Action<byte, int> Write;
    internal bool Synchronous;

    /// <summary>Live state for the UI: smoothed temperature and the boost being applied.</summary>
    public (float temp, float boost, float target) Cpu => (_cpu.Temp, _cpu.Boost, _cpu.Target);
    public (float temp, float boost, float target) Gpu => (_gpu.Temp, _gpu.Boost, _gpu.Target);

    public FanCurveDriver(Controller c, SensorMonitor mon)
    {
        _c = c; _mon = mon;
        Write = (sensor, v) => _c.SetBoostGroup(sensor, v, save: false);
        _mon.Updated += OnSample;
        Refresh();
    }

    public bool On => _c.S.FanCurve && _c.Awcc != null;

    /// <summary>Custom switched on or off: start or stop asking the monitor for temperatures.</summary>
    public void Refresh()
    {
        _mon.Want(this, On ? Need.Fans : Need.None);
        _last = -1;
        foreach (var g in new[] { _cpu, _gpu }) { g.Written = -1; g.Temp = -1; }
    }

    void OnSample()
    {
        if (!On) return;
        var snap = _mon.Last;
        if (snap == null || snap.Fans.Count == 0) return;   // a mode-only sample carries no temperatures
        int Temp(string name) => snap.Temps.FirstOrDefault(t => t.s.Name == name).temp;
        // Alien Mode as Custom's base runs on the curves too (alien = false); only outside Custom it means 100 %
        Feed(Temp("CPU"), Temp("GPU"), SensorMonitor.Clock.ElapsedMilliseconds, alien: false);
    }

    /// <summary>One step of both groups for these temperatures at time <paramref name="now"/> (ms).</summary>
    internal void Feed(int cpuTemp, int gpuTemp, long now, bool alien)
    {
        float dt = _last < 0 ? 0 : Math.Clamp((now - _last) / 1000f, 0, 5);
        _last = now;
        Step(_cpu, cpuTemp, _c.S.CpuCurve, dt, now, alien);
        Step(_gpu, gpuTemp, _c.S.GpuCurve, dt, now, alien);
    }

    void Step(Group g, int temp, List<int[]> curve, float dt, long now, bool alien)
    {
        if (temp <= 0) return;
        // first sample starts where the curve is, afterwards temperature and boost follow smoothly
        bool first = g.Temp < 0;
        g.Temp = first ? temp : g.Temp + (temp - g.Temp) * Math.Min(1f, dt / TempTau);
        g.Target = alien ? 100 : FanCurve.Eval(curve, g.Temp);
        if (first) g.Boost = g.Target;
        else if (g.Target > g.Boost) g.Boost = Math.Min(g.Target, g.Boost + UpPerSec * dt);
        else g.Boost = Math.Max(g.Target, g.Boost - DownPerSec * dt);
        if (alien) { g.Written = -1; return; }   // Alien Mode sets 100 % itself (BoostForMode); rewrite after it

        int v = (int)Math.Round(g.Boost);
        if (v == g.Written || g.Busy) return;
        // write a 2 % step at most once a second, a 1 % drift only every 3 s; reaching 0 (auto) always goes through
        int diff = Math.Abs(v - g.Written);
        bool due = g.Written < 0 || diff >= 2 || v == 0 || now - g.WrittenAt >= 3000;
        if (!due || (g.Written >= 0 && now - g.WrittenAt < 1000)) return;
        g.Busy = true;
        g.Written = v;
        g.WrittenAt = now;
        void Do()
        {
            try { Write(g.Sensor, v); }
            catch (Exception ex) { Log.Warn($"curve {g.Name}: {ex.Message}"); }
            finally { g.Busy = false; }
        }
        if (Synchronous) Do(); else Task.Run(Do);
    }

    public void Dispose()
    {
        _mon.Updated -= OnSample;
        _mon.Want(this, Need.None);
    }
}
