using System.Drawing;
using System.Globalization;
using System.Text;
using AlienHelper.Hardware;

namespace AlienHelper;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // installer/uninstaller: ask a running instance to quit (no elevation needed to signal it)
        if (args.FirstOrDefault() == "--exit") return ShowSignal.TrySignal(ShowSignal.ExitName) ? 0 : 1;

        // asInvoker manifest: hardware access needs admin, so a normal start goes through the launcher
        // read-only NVIDIA thermal sensor dump (development aid; needs no admin)
        if (args.Length >= 2 && args[0] == "--nvapi") return NvApiDump(args[1]);

        if (Launcher.IsLimited)
        {
            int r = Launcher.Run(args);
            if (r != Launcher.Limited) return r;
            Log.Info("admin rights declined: limited mode");
        }

        if (args.Length >= 2 && args[0] == "--probe") return Probe(args[1]);
        if (args.Length >= 2 && args[0] == "--test") return Test(args[1], args.Skip(2).ToArray());
        if (args.Length >= 2 && args[0] == "--fpstest") return FpsTest(args[1]);
        if (args.Length >= 2 && args[0] == "--bench") return Bench(args[1]);
        if (args.Length >= 2 && args[0] == "--coretemps") return CoreTempsDump(args[1]);
        if (args.Length >= 2 && args[0] == "--demandtest") return DemandTest(args[1]);
        if (args.Length >= 2 && args[0] == "--curvetest") return CurveTest(args[1]);
        if (args.Length >= 2 && args[0] == "--curvehw") return CurveHardware(args[1]);
        if (args.Length >= 2 && args[0] == "--octest") return OcTest(args[1]);
        if (args.Length >= 2 && args[0] == "--psys") return PsysTest(args[1]);
        if (args.Length >= 2 && args[0] == "--gpuclocks") return GpuClocksDump(args[1], args.Skip(2).ToArray());

        // started from limited mode with admin rights: wait until the limited instance has exited
        using var mutex = new Mutex(false, AppId.Key + ".SingleInstance");
        bool first;
        try { first = mutex.WaitOne(args.Contains(Launcher.RestartedArg) ? 15000 : 0); }
        catch (AbandonedMutexException) { first = true; }
        if (!first && args.FirstOrDefault() is not ("--screenshot" or "--overlaytest"))
        {
            if (!args.Contains("--tray")) ShowSignal.TrySignal();
            return 0;
        }
        // the prod and the test build must not drive the hardware at the same time
        if (args.FirstOrDefault() is not ("--screenshot" or "--overlaytest") && !AppId.TakeOver(args.Contains("--tray"))) return 0;
        AppId.SeedSettings();

        if (!Launcher.UiReady) ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("background task", e.Exception); e.SetObserved(); };
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) UI.Theme.Scale = g.DpiX / 96f;
        var startSettings = Settings.Load();
        L.Init(startSettings.Language);
        UI.Theme.Apply(args.FirstOrDefault(a => a.StartsWith("theme="))?[6..] ?? startSettings.Look);
        UI.Theme.NumbersOnly = args.Contains("numbers") || startSettings.NumbersOnly;
        if (args.Length >= 3 && args[0] == "--screenshot") L.Set(args[2]);
        if (args.Length >= 2 && args[0] == "--screenshot")
        {
            int Arg(string name) => int.TryParse(args.FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..], out var v) ? v : 0;
            if (args.Contains("limited")) Launcher.PretendLimited();
            Hardware.CoreTemps.PretendMissing = args.Contains("nopawnio");
            return Screenshot(args[1], args.Contains("split"), args.FirstOrDefault(a => a.StartsWith("hide="))?[5..].Split(','), Arg("fakegpus"), Arg("fakedisks"), Arg("poll"), args.Contains("lite"), args.Contains("custom"), Arg("wait"));
        }
        if (args.Length >= 2 && args[0] == "--overlaytest") return OverlayTest(args[1]);
        Log.Cleanup();
        Log.Info($"{AppId.Key} {typeof(Program).Assembly.GetName().Version} starting, args='{string.Join(' ', args)}', os={Environment.OSVersion.VersionString}");
        Application.Run(new TrayApp(showWindow: !args.Contains("--tray") || args.Contains(Launcher.RestartedArg)));
        return 0;
    }

    /// <summary>Drives the real OverlayManager: show, hide, show again; logs window state and renders it.</summary>
    static int OverlayTest(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new StringBuilder();
        void L(string s) { log.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {s}"); File.WriteAllText(Path.Combine(dir, "overlaytest.txt"), log.ToString()); }
        var s = Settings.Load();
        s.Transient = true;
        using var c = new Controller(s);
        using var mon = new SensorMonitor(c);
        using var overlay = new OverlayManager(c, mon);
        L($"settings: auto={s.OverlayAuto} style={s.OverlayStyle} corner={s.OverlayCorner} x={s.OverlayX} y={s.OverlayY}");
        Form? Find() => Application.OpenForms.OfType<OverlayForm>().FirstOrDefault();
        void State(string tag)
        {
            var f = Find();
            L($"{tag}: managerVisible={overlay.Visible} form={(f == null ? "none" : $"visible={f.Visible} bounds={f.Bounds} opacity={f.Opacity}")}");
        }
        var steps = new Queue<(int ms, Action act)>(new (int, Action)[]
        {
            (500, () => { overlay.Toggle(); State("show #1"); }),
            (3000, () => { State("after 3s"); overlay.Toggle(); State("hide"); }),
            (1500, () => { overlay.Toggle(); State("show #2"); }),
            (3000, () =>
            {
                State("after 3s");
                if (Find() is { } f)
                {
                    using var bmp = new Bitmap(f.Width, f.Height);
                    f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                    bmp.Save(Path.Combine(dir, "overlaytest.png"));
                }
                Application.Exit();
            }),
        });
        var t = new System.Windows.Forms.Timer();
        t.Tick += (_, _) =>
        {
            t.Stop();
            try { steps.Dequeue().act(); } catch (Exception ex) { L("ERROR " + ex); Application.Exit(); return; }
            if (steps.Count > 0) { t.Interval = steps.Peek().ms; t.Start(); }
        };
        t.Interval = steps.Peek().ms;
        t.Start();
        Application.Run();
        return 0;
    }

    /// <summary>Times every sensor source over 20 rounds (read-only), to see what makes a refresh slow.</summary>
    static int Bench(string outFile)
    {
        var sb = new StringBuilder();
        var s = Settings.Load();
        s.Transient = true;
        using var c = new Controller(s);
        var a = c.Awcc;
        void Time(string name, Action act)
        {
            act();   // warm up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var each = new List<double>();
            for (int i = 0; i < 20; i++) { var t = sw.Elapsed.TotalMilliseconds; act(); each.Add(sw.Elapsed.TotalMilliseconds - t); }
            sb.AppendLine($"{name,-28} avg {each.Average(),7:0.0} ms   max {each.Max(),7:0.0} ms");
        }
        if (a != null)
        {
            var f = a.Fans[0];
            Time("awcc fan rpm", () => a.GetFanRpm(f.Id));
            Time("awcc fan percent", () => a.GetFanPercent(f.Id));
            Time("awcc fan boost", () => a.GetFanBoost(f.Id));
            Time("awcc temp", () => a.GetTemp(a.Sensors[0].Id));
            Time("awcc profile", () => a.GetProfile());
            Time("awcc gmode", () => a.GetGMode());
            Time("controller.Read (all)", () => c.Read());
        }
        var nv = Hardware.Nvml.TryOpen();
        if (nv != null) Time("nvml read", () => nv.Read(0));
        var api = Hardware.NvApi.TryOpen();
        if (api != null) Time("nvapi memory temp", () => api.MemoryTemp(api.Bus(0), 0));
        using var cores = new CoreMonitor();
        cores.Open();
        Time("core counters", cores.Sample);
        Time("ram", () => SystemMemory.Read());
        Time("disk temps (ioctl)", () => Hardware.DiskTemps.Read());
        // the same sources spaced like a real 0.5 s refresh: periodic stalls only show up with time between calls
        using var pkg = new System.Diagnostics.PerformanceCounter("Energy Meter", "Power", "RAPL_Package0_PKG");
        using var dr = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "Disk Reads/sec", "_Total");
        using var ct = Hardware.CoreTemps.TryOpen(out _);
        var spaced = new Dictionary<string, List<double>>();
        void Spaced(string name, Action act)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            act();
            if (!spaced.TryGetValue(name, out var l)) spaced[name] = l = new();
            l.Add(sw.Elapsed.TotalMilliseconds);
        }
        for (int i = 0; i < 12; i++)
        {
            if (a != null) Spaced("awcc read", () => c.Read());
            Spaced("core counters", cores.Sample);
            Spaced("rapl counter", () => pkg.NextValue());
            Spaced("disk counter", () => dr.NextValue());
            if (nv != null) Spaced("nvml", () => nv.Read(0));
            if (ct != null) Spaced("core temps (pawnio)", () => ct.Read(Environment.ProcessorCount));
            Spaced("network links", () => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces());
            Thread.Sleep(500);
        }
        sb.AppendLine("spaced 0.5 s:");
        foreach (var (name, l) in spaced) sb.AppendLine($"  {name,-26} {string.Join(" ", l.Select(x => x.ToString("0")))}");
        nv?.Dispose();
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>
    /// Checks that only wanted sources are read: nothing with nothing on screen, a section's sources only, the slow
    /// sources joining in the background. Read-only.
    /// </summary>
    static int DemandTest(string outFile)
    {
        var sb = new StringBuilder();
        var s = Settings.Load();
        s.Transient = true;
        s.FanBoost = s.FanBoostCpu = s.FanBoostGpu = 0;
        s.PollIntervalMs = 500;
        using var c = new Controller(s);
        using var mon = new SensorMonitor(c);
        SensorMonitor.Trace = new();
        void Phase(string name, int ms, Action setup)
        {
            setup();
            lock (SensorMonitor.Trace) SensorMonitor.Trace.Clear();
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(20); }
            List<(long at, long ms, Need need)> t;
            lock (SensorMonitor.Trace) t = SensorMonitor.Trace.ToList();
            sb.AppendLine($"{name,-38} reads={t.Count,3}  sources={string.Join(", ", t.Select(x => x.need.ToString()).Distinct())}  ms=[{string.Join(" ", t.Select(x => x.ms))}]");
        }
        var main = new object();
        var charts = new Form();
        Phase("main sections off (3 s)", 3000, () => { });
        Phase("main: CPU+GPU, window in tray (3 s)", 3000, () => mon.Want(main, Need.Fans | Need.Cpu | Need.Gpu));
        Phase("+ charts window open (3 s)", 3000, () => mon.Want(charts, Need.All));
        Phase("charts closed (3 s)", 3000, () => mon.Want(charts, Need.None));
        Phase("main: screen+lights only (3 s)", 3000, () => mon.Want(main, Need.None));
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>
    /// Fan curve driver offline: a simulated temperature ramp (45 → 92 → 50 °C over 90 s, 0.5 s steps) through the
    /// "balanced" CPU curve; logs every firmware write the driver would make. Nothing is written to the fans.
    /// </summary>
    static int CurveTest(string outFile)
    {
        var sb = new StringBuilder();
        var s = new Settings { Transient = true, FanCurve = false };   // off: the live sample handler stays idle, only Feed below runs
        using var c = new Controller(s);
        using var mon = new SensorMonitor(c);
        var d = new FanCurveDriver(c, mon) { Synchronous = true };
        d.Dispose();   // no live samples: only the simulated ones below
        var writes = new List<string>();
        d.Write = (sensor, v) => writes.Add($"{(sensor == Controller.CpuSensor ? "CPU" : "GPU")}={v}");
        sb.AppendLine("t(s)  temp  target  boost  writes");
        for (int i = 0; i <= 180; i++)
        {
            float t = i * 0.5f;
            int temp = t < 30 ? 45 + (int)(t / 30 * 47) : t < 45 ? 92 : 92 - (int)((t - 45) / 45 * 42);
            writes.Clear();
            d.Feed(temp, 40, (long)(t * 1000), alien: false);
            var (st, sbst, stg) = d.Cpu;
            if (i % 4 == 0 || writes.Count > 0)
                sb.AppendLine($"{t,5:0.0}  {temp,4}  {stg,6:0.0}  {sbst,5:0.0}  {string.Join(" ", writes)}");
        }
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>
    /// Fan curves on the hardware: the real driver with the "max" profile for 20 s (temperatures, boost, rpm logged every
    /// 2 s), then the boost goes back to auto (0) and the fans are logged once more. Settings are not saved.
    /// </summary>
    static int CurveHardware(string outFile)
    {
        var sb = new StringBuilder();
        var s = Settings.Load();
        s.Transient = true;
        var max = FanCurve.Presets.First(p => p.id == "max");
        s.CpuCurve = FanCurve.Copy(max.cpu);
        s.GpuCurve = FanCurve.Copy(max.gpu);
        s.FanCurve = true;
        using var c = new Controller(s);
        using var mon = new SensorMonitor(c);
        var d = new FanCurveDriver(c, mon);
        void Line(string tag)
        {
            var snap = c.Read();
            string temps = snap == null ? "-" : string.Join(" ", snap.Temps.Select(t => $"{t.s.Name}={t.temp}°"));
            string rpm = snap == null ? "-" : string.Join(" ", snap.Fans.Select(f => $"{f.fan.Id:X2}={f.rpm}"));
            sb.AppendLine($"{tag,-10} {temps}  boost CPU {d.Cpu.boost:0}% GPU {d.Gpu.boost:0}%  rpm {rpm}");
        }
        Line("start");
        var until = DateTime.UtcNow.AddSeconds(20);
        var next = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(20);
            if (DateTime.UtcNow >= next) { Line($"{(20 - (until - DateTime.UtcNow).TotalSeconds):0}s"); next = next.AddSeconds(2); }
        }
        d.Dispose();
        Thread.Sleep(1200);   // a write in flight finishes
        c.SetBoost(0);
        Thread.Sleep(6000);
        Line("auto +6s");
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>Intel RAPL platform (PSYS) energy, MSR 0x64D, through PawnIO: watts over 1 s steps next to the package (read-only).</summary>
    static int PsysTest(string outFile)
    {
        var sb = new StringBuilder();
        using var ct = Hardware.CoreTemps.TryOpen(out var why);
        if (ct == null) { File.WriteAllText(outFile, "PawnIO unavailable: " + why); return 0; }
        ulong unit = ct.ReadMsr(0, 0x606);
        sb.AppendLine($"0x606 = 0x{unit:X} status={ct.LastStatus}");
        double j = 1.0 / Math.Pow(2, (unit >> 8) & 0x1F);
        uint E(uint msr) => (uint)(ct.ReadMsr(0, msr) & 0xFFFFFFFF);
        uint p0 = E(0x64D), k0 = E(0x611);
        sb.AppendLine($"0x64D = {p0} status={ct.LastStatus}   0x611 = {k0}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 6; i++)
        {
            Thread.Sleep(1000);
            uint p1 = E(0x64D), k1 = E(0x611);
            double dt = sw.Elapsed.TotalSeconds; sw.Restart();
            sb.AppendLine($"psys {(unchecked(p1 - p0)) * j / dt,7:0.0} W   package {(unchecked(k1 - k0)) * j / dt,7:0.0} W");
            p0 = p1; k0 = k1;
        }
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>
    /// Overclock cycle on the hardware through the app's own manager: Custom + Overclock (mild) on, offsets read back,
    /// then Overclock off and the original offsets checked. Settings are not saved.
    /// </summary>
    static int OcTest(string outFile)
    {
        var sb = new StringBuilder();
        var s = Settings.Load();
        s.Transient = true;
        s.GpuOcApplied = false;
        using var c = new Controller(s);
        var gc = Hardware.GpuClocks.TryOpen(out _);
        using var nv = Hardware.Nvml.TryOpen();
        string Now() { var o = gc?.Offsets(() => nv?.Read(0)); return o == null ? "unreadable" : $"{o.Value.core}/{o.Value.mem}"; }
        sb.AppendLine("before: " + Now());
        var oc = new GpuOverclock(c);
        s.FanCurve = true; s.GpuOcOn = true; s.GpuOc = "mild";
        oc.Sync();
        Thread.Sleep(6000);
        sb.AppendLine($"overclock on ({oc.State}): " + Now());
        s.GpuOcOn = false;
        oc.Sync();
        Thread.Sleep(6000);
        sb.AppendLine("overclock off: " + Now() + $"  (saved originals {s.GpuOcBaseCore}/{s.GpuOcBaseMem}, applied={s.GpuOcApplied})");
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>
    /// NVIDIA P0 clock table: editable flags, allowed offset ranges, current offsets (read-only). With
    /// "apply CORE MEM" it sets the offsets, reads them back with the NVML clocks, waits 5 s and puts back the offsets it found.
    /// </summary>
    static int GpuClocksDump(string outFile, string[] steps)
    {
        var sb = new StringBuilder();
        var gc = Hardware.GpuClocks.TryOpen(out var why);
        if (gc == null) { File.WriteAllText(outFile, "unavailable: " + why); return 0; }
        using var nv = Hardware.Nvml.TryOpen();
        void Dump(string tag)
        {
            var clocks = gc.Read();
            var r = nv?.Read(0);
            sb.AppendLine($"[{tag}] status={gc.LastStatus} nvml core={r?.CoreMhz} mem={r?.MemMhz}");
            foreach (var c in clocks ?? new()) sb.AppendLine($"   domain={c.Domain} editable={c.Editable} offset={c.OffsetMhz} range={c.MinMhz}..{c.MaxMhz} base={c.BaseMhz}");
        }
        Dump("read");
        if (steps.Length >= 3 && steps[0] == "set" && int.TryParse(steps[1], out int sc) && int.TryParse(steps[2], out int sm))
        {
            bool ok = gc.ApplyWithRetry(sc, sm, () => nv?.Read(0));
            sb.AppendLine($"set {sc}/{sm}: {(ok ? "ok" : "refused")} status={gc.LastStatus}");
            var now = gc.Offsets(() => nv?.Read(0));
            sb.AppendLine($"offsets now: {(now == null ? "unreadable" : $"{now.Value.core}/{now.Value.mem}")}");
        }
        if (steps.Length >= 3 && steps[0] == "apply" && int.TryParse(steps[1], out int core) && int.TryParse(steps[2], out int mem))
        {
            // the offsets found before the test are put back afterwards (another tool may have set them)
            var before = gc.Read() ?? new();
            int core0 = before.FirstOrDefault(c => c.Domain == Hardware.GpuClocks.DomainGraphics)?.OffsetMhz ?? 0;
            int mem0 = before.FirstOrDefault(c => c.Domain == Hardware.GpuClocks.DomainMemory)?.OffsetMhz ?? 0;
            bool ok = gc.Apply(core, mem);
            sb.AppendLine($"apply {core}/{mem}: {(ok ? "ok" : "refused")} status={gc.LastStatus}");
            Thread.Sleep(1500);
            Dump("after apply");
            Thread.Sleep(5000);
            ok = gc.Apply(core0, mem0);
            sb.AppendLine($"restore {core0}/{mem0}: {(ok ? "ok" : "refused")} status={gc.LastStatus}");
            Thread.Sleep(1500);
            Dump("after reset");
        }
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>Per-core CPU temperatures through PawnIO (read-only), five readings a second apart.</summary>
    static int CoreTempsDump(string outFile)
    {
        var sb = new StringBuilder();
        using var ct = Hardware.CoreTemps.TryOpen(out var why);
        if (ct == null) sb.AppendLine("unavailable: " + why);
        else
        {
            sb.AppendLine($"module: {ct.Source}  TjMax={ct.TjMax}");
            for (int i = 0; i < 5; i++)
            {
                sb.AppendLine(string.Join(" ", ct.Read(Environment.ProcessorCount)));
                Thread.Sleep(1000);
            }
        }
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    static int NvApiDump(string outFile)
    {
        var sb = new StringBuilder();
        var api = Hardware.NvApi.TryOpen();
        if (api == null) sb.AppendLine("nvapi: unavailable");
        else
            for (int i = 0; i < api.Count; i++)
            {
                var v = api.ReadAll(api.Bus(i), i);
                sb.AppendLine($"gpu {i} bus={api.Bus(i)} mask=0x{api.Mask(i):X} status={Hardware.NvApi.LastStatus}: " + (v == null ? "read failed" : string.Join(" ", v.Select((t, k) => $"[{k}]={t:0.0}"))));
            }
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>Captures ETW present events for 6 s and dumps per-process counts (read-only).</summary>
    static int FpsTest(string outFile)
    {
        var sb = new StringBuilder();
        using var fm = new FrameMonitor();
        fm.Start();
        sb.AppendLine($"running={fm.Running} error={fm.Error}");
        Thread.Sleep(6000);
        foreach (var (pid, name, count) in fm.Counts()) sb.AppendLine($"{name} ({pid}): {count} presents in last 2 s");
        var s = fm.Read();
        sb.AppendLine(s == null ? "stats: none" : $"stats: {s.Process} fps={s.Fps:0} frame={s.FrameMs:0.0}ms low1={s.Low1Fps:0} n={s.FrameTimes.Count}");
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>Renders the windows to PNG files after a few samples (UI development aid; changes nothing).</summary>
    static int Screenshot(string dir, bool split = false, string[]? hide = null, int fakeGpus = 0, int fakeDisks = 0, int poll = 0, bool lite = false, bool custom = false, int wait = 0)
    {
        var s = Settings.Load();
        s.Transient = true;
        if (split) s.FanExpert = true;
        if (custom) s.FanCurve = true;
        if (poll > 0) s.PollIntervalMs = poll;
        if (hide != null) s.HiddenCards = hide.ToList();
        using var c = new Controller(s);
        using var mon = new SensorMonitor(c);
        mon.FakeGpus = fakeGpus; mon.FakeDrives = fakeDisks;   // layout checks for machines with more GPUs / drives
        using var overlay = new OverlayManager(c, mon);
        var main = new MainForm(c, mon, () => { });
        var settings = new SettingsForm(c, overlay, _ => { }, () => { }, () => Array.Empty<int>(), () => { });
        if (!lite) settings.Show();
        var charts = new ChartsForm(c, mon);
        using var frames = new FrameMonitor();
        frames.Start();
        var overlays = Enumerable.Range(0, OverlayManager.StyleCount).Select(i => new OverlayForm(c, mon, frames) { Style = i }).ToList();
        var keys = new KeyboardForm(c, preview: true, template: 3);   // cat
        if (!lite) keys.Show();
        main.Show();
        charts.Show();
        // fan curve editor; its driver records nothing and writes nothing here
        var curveDriver = new FanCurveDriver(c, mon) { Write = (_, _) => { } };
        var curves = new FanCurveForm(c, mon, curveDriver);
        if (!lite) curves.Show();
        var about = new AboutForm();
        if (!lite) about.Show();
        var notice = new ElevationNotice { TopMost = false };
        if (!lite) notice.Show();
        if (!lite) foreach (var ov in overlays) ov.ShowNoActivate();   // lite: only the main window and charts, as in daily use
        // sample timing: how evenly readings arrive and what they cost (written to samples.txt)
        var stamps = new List<long>();
        SensorMonitor.Trace = new();
        long from = SensorMonitor.Clock.ElapsedMilliseconds + 4000;   // skip start-up (windows opening, JIT)
        var cpu0 = TimeSpan.Zero;
        mon.Updated += () =>
        {
            long now = SensorMonitor.Clock.ElapsedMilliseconds;
            if (now < from) return;
            if (stamps.Count == 0) cpu0 = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            stamps.Add(now);
        };
        mon.Want(mon, Need.All);
        var t = new System.Windows.Forms.Timer { Interval = wait > 0 ? wait * 1000 : 12000 };   // wait=<sec>: longer chart history
        t.Tick += (_, _) =>
        {
            t.Stop();
            Directory.CreateDirectory(dir);
            var forms = new List<(Form, string)> { (main, "main.png"), (charts, "charts.png"), (keys, "keys.png"), (settings, "settings.png"), (curves, "curves.png"), (about, "about.png"), (notice, "elevation.png") };
            forms.AddRange(overlays.Select((o, i) => ((Form)o, $"overlay{i}.png")));
            foreach (var (f, name) in forms)
            {
                if (f.IsDisposed) continue;   // a window closed while the screenshots were being taken
                using var bmp = new Bitmap(f.Width, f.Height);
                // PrintWindow shows what the screen shows; DrawToBitmap paints overlapping children in the wrong order
                bool ok;
                using (var gr = Graphics.FromImage(bmp))
                {
                    var hdc = gr.GetHdc();
                    ok = PrintWindow(f.Handle, hdc, 2);   // PW_RENDERFULLCONTENT
                    gr.ReleaseHdc(hdc);
                }
                if (!ok) f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                bmp.Save(Path.Combine(dir, name));
            }
            var gaps = stamps.Zip(stamps.Skip(1), (x, y) => y - x).ToList();
            var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu0;
            File.WriteAllText(Path.Combine(dir, "samples.txt"), gaps.Count == 0 ? "no samples" :
                $"interval {SensorMonitor.IntervalMs} ms: {gaps.Count} gaps, avg {gaps.Average():0} ms, min {gaps.Min()} ms, max {gaps.Max()} ms; process CPU {cpu.TotalMilliseconds / Math.Max(1, stamps[^1] - stamps[0]) * 100:0.0}% of one core" + Environment.NewLine +
                "reads: " + string.Join(" ", SensorMonitor.Trace.Where(x => x.at >= from).Select(x => $"{x.at - from}+{x.ms}")));
            Application.Exit();
        };
        t.Start();
        Application.Run();
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    /// <summary>Read-only dump of everything the firmware reports.</summary>
    static int Probe(string outFile)
    {
        var sb = new StringBuilder();
        var awcc = Awcc.TryOpen(out var err);
        if (awcc == null) sb.AppendLine("AWCC: " + err);
        else using (awcc)
        {
            sb.AppendLine($"SystemDescription: 0x{awcc.Call("Thermal_Information", 0x02):X8}");
            for (byte i = 0; i < 64; i++)
            {
                long id = awcc.Call("Thermal_Information", 0x03, i);
                sb.AppendLine($"ResourceId[{i}] = 0x{id:X}");
                if (id <= 0 || id >= 0xFFFFFFFE) break;
            }
            foreach (var f in awcc.Fans)
                sb.AppendLine($"FAN 0x{f.Id:X2} sensor=0x{f.SensorId:X2} max={f.MaxRpm} rpm={awcc.GetFanRpm(f.Id)} pct={awcc.GetFanPercent(f.Id)} boost={awcc.GetFanBoost(f.Id)}");
            foreach (var s in awcc.Sensors)
                sb.AppendLine($"TEMP 0x{s.Id:X2} {s.Name} = {awcc.GetTemp(s.Id)}");
            sb.AppendLine("PROFILES: " + string.Join(", ", awcc.Profiles.Select(p => $"0x{p:X2} {Awcc.ProfileNames.GetValueOrDefault(p, "?")}")));
            sb.AppendLine($"CurrentProfile: 0x{awcc.GetProfile():X2}");
            sb.AppendLine($"GMode: {awcc.GetGMode()} (has={awcc.HasGMode})");
        }
        try
        {
            foreach (var d in AlienFxDevice.Enumerate())
                using (d) sb.AppendLine($"LIGHT {d.Name} {d.Vid:X4}:{d.Pid:X4} API v{(int)d.Version} status={d.Status()} {d.Path}");
        }
        catch (Exception ex) { sb.AppendLine("LIGHT error: " + ex.Message); }
        File.WriteAllText(outFile, sb.ToString());
        return 0;
    }

    /// <summary>
    /// Step-by-step hardware test used during development, e.g.
    /// --test out.txt read profile:A3 wait:10 read restore
    /// </summary>
    static int Test(string outFile, string[] steps)
    {
        var log = new StringBuilder();
        void L(string s) { log.AppendLine($"{DateTime.Now:HH:mm:ss} {s}"); File.WriteAllText(outFile, log.ToString()); }

        using var c = new Controller(new Settings { Transient = true });
        if (c.Awcc == null) L("AWCC unavailable: " + c.AwccError);
        int startProfile = c.Awcc?.GetProfile() ?? -1;
        L($"start profile 0x{startProfile:X2}");

        foreach (var step in steps)
        {
            var p = step.Split(':');
            try
            {
                switch (p[0])
                {
                    case "read":
                        var snap = c.Read();
                        if (snap == null) { L("read: no AWCC"); break; }
                        L($"profile=0x{snap.Profile:X2} gmode={snap.GMode} " +
                          string.Join(" ", snap.Fans.Select(f => $"fan{f.fan.Id:X2}={f.rpm}rpm/{f.pct}%/boost{f.boost}")) + " " +
                          string.Join(" ", snap.Temps.Select(t => $"{t.s.Name}={t.temp}C")));
                        break;
                    case "profile":
                        L($"SetProfile 0x{p[1]} -> {c.SetProfile(byte.Parse(p[1], NumberStyles.HexNumber))}");
                        break;
                    case "boost":
                        L($"SetBoost {p[1]} -> {c.SetBoost(int.Parse(p[1]))}");
                        break;
                    case "wait":
                        Thread.Sleep(int.Parse(p[1]) * 1000);
                        break;
                    case "kb":
                        L($"Keyboard #{p[1]} {p[2]}% -> {c.SetKeyboard(ColorTranslator.FromHtml("#" + p[1]), int.Parse(p[2]))}");
                        break;
                    case "ch":
                        L($"Chassis #{p[1]} {p[2]}% -> {c.SetChassis(ColorTranslator.FromHtml("#" + p[1]), int.Parse(p[2]))}");
                        break;
                    case "kbrows":
                        {
                            // matrix check: each block of 20 ids (one matrix row) gets its own color
                            (byte, byte, byte)[] rowColors =
                            {
                                (255, 0, 0), (255, 120, 0), (255, 255, 0), (0, 255, 0), (0, 200, 255), (0, 0, 255), (200, 0, 255), (255, 255, 255),
                            };
                            L($"kbrows -> {c.Keyboard?.SetKeyColors(id => rowColors[Math.Min(id / 20, rowColors.Length - 1)], c.S.KeyboardLights)}");
                        }
                        break;
                    case "kbunused":
                        {
                            // kbunused            — ids not in the layout, colored by matrix row (id / 20)
                            // kbunused:<row>      — ids not in the layout inside one row, each its own color (by order)
                            (byte, byte, byte)[] pal =
                            {
                                (255, 0, 0), (255, 120, 0), (255, 255, 0), (0, 255, 0), (0, 200, 255), (0, 0, 255), (200, 0, 255), (255, 255, 255),
                            };
                            var used = KeyboardLayout.Keys.Select(k => k.DefaultId).ToHashSet();
                            int row = p.Length > 1 ? int.Parse(p[1]) : -1;
                            var unused = Enumerable.Range(0, c.S.KeyboardLights).Where(i => !used.Contains(i) && (row < 0 || i / 20 == row)).ToList();
                            L($"unused{(row >= 0 ? $" row {row}" : "")}: " + string.Join(",", unused.Select((id, n) => $"{id}={(row < 0 ? id / 20 : n)}")));
                            L($"kbunused -> {c.Keyboard?.SetKeyColors(id =>
                            {
                                int n = unused.IndexOf(id);
                                if (n < 0) return ((byte)0, (byte)0, (byte)0);
                                return pal[row < 0 ? Math.Min(id / 20, pal.Length - 1) : n % pal.Length];
                            }, c.S.KeyboardLights)}");
                        }
                        break;
                    case "kbfind":
                        {
                            // kbfind:1,3,4,5,7 — inside each listed row, unused ids get palette colors by order (max 8 per row)
                            (byte, byte, byte)[] pal =
                            {
                                (255, 0, 0), (255, 120, 0), (255, 255, 0), (0, 255, 0), (0, 200, 255), (0, 0, 255), (200, 0, 255), (255, 255, 255),
                            };
                            var used = KeyboardLayout.Keys.Select(k => k.DefaultId).ToHashSet();
                            var map = new Dictionary<int, int>();
                            foreach (int row in p[1].Split(',').Select(int.Parse))
                            {
                                var ids = Enumerable.Range(row * 20, 20).Where(i => i < c.S.KeyboardLights && !used.Contains(i)).Take(pal.Length).ToList();
                                for (int n = 0; n < ids.Count; n++) map[ids[n]] = n;
                                L($"row {row}: " + string.Join(", ", ids.Select((id, n) => $"{id}=c{n}")));
                            }
                            L($"kbfind -> {c.Keyboard?.SetKeyColors(id => map.TryGetValue(id, out var n) ? pal[n] : ((byte)0, (byte)0, (byte)0), c.S.KeyboardLights)}");
                        }
                        break;
                    case "kbanim":
                        // kbanim:fire:10[:reset] — run an animated template for N seconds
                        if (c.Keyboard != null) c.Keyboard.FramesWithoutReset = !(p.Length > 3 && p[3] == "reset");
                        c.FrameCount = 0; c.FrameMsTotal = 0;
                        c.UseAnimatedTemplate(p[1]);
                        L($"kbanim {p[1]} running={c.AnimationId} noreset={c.Keyboard?.FramesWithoutReset}");
                        Thread.Sleep(int.Parse(p[2]) * 1000);
                        c.StopAnimation();
                        L($"  frames={c.FrameCount} avg={(c.FrameCount > 0 ? c.FrameMsTotal / c.FrameCount : 0):0.0} ms");
                        break;
                    case "boostgrp":
                        {
                            // boostgrp — CPU fans +60 for 10 s, then GPU fans +60 for 10 s, then reset
                            string Rpms() => c.Awcc == null ? "-" : string.Join(" ", c.Awcc.Fans.Select(f => $"{(f.SensorId == Controller.CpuSensor ? "cpu" : "gpu")}{f.Id:X2}={c.Awcc.GetFanRpm(f.Id)}"));
                            L($"start: {Rpms()}");
                            c.SetBoostGroup(Controller.CpuSensor, 60);
                            Thread.Sleep(10000);
                            L($"CPU +60: {Rpms()}");
                            c.SetBoostGroup(Controller.CpuSensor, 0);
                            c.SetBoostGroup(Controller.GpuSensor, 60);
                            Thread.Sleep(10000);
                            L($"GPU +60: {Rpms()}");
                            c.SetBoost(0);
                            Thread.Sleep(5000);
                            L($"reset:   {Rpms()}");
                        }
                        break;
                    case "screen":
                        {
                            // screen — lower the brightness by 20, wait for the WMI event, restore
                            int before = Hardware.ScreenBrightness.Read();
                            int got = -1;
                            using var ev = new ManualResetEventSlim();
                            if (c.Screen != null) c.Screen.Changed += v => { got = v; ev.Set(); };
                            L($"screen: brightness={before} live={c.Screen?.LiveUpdates}");
                            c.Screen?.Set(Math.Max(0, before - 20));
                            bool fired = ev.Wait(3000);
                            L($"set {Math.Max(0, before - 20)} -> event fired={fired} value={got} read={Hardware.ScreenBrightness.Read()}");
                            Thread.Sleep(1000);
                            c.Screen?.Set(before);
                            Thread.Sleep(500);
                            L($"restored -> read={Hardware.ScreenBrightness.Read()}");
                        }
                        break;
                    case "kbswitch":
                        {
                            // kbswitch:3 — fire 3 s, then WASD 3 s, repeated; checks no animation frame overwrites WASD
                            var wasd = KeyboardLayout.Render(KeyboardLayout.Find("wasd")!);
                            for (int n = 0; n < int.Parse(p[1]); n++)
                            {
                                c.UseAnimatedTemplate("fire");
                                Thread.Sleep(3000);
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                c.SetKeyScheme(wasd, save: true);
                                L($"switch {n + 1}: fire -> wasd in {sw.ElapsedMilliseconds} ms, animation={c.AnimationId ?? "none"}");
                                Thread.Sleep(3000);
                            }
                            Thread.Sleep(2000);
                        }
                        break;
                    case "kbcolors":
                        {
                            // kbcolors:80=FF0000,81=FF7800 — light exactly these ids with these colors
                            var map = p[1].Split(',').Select(x => x.Split('=')).ToDictionary(x => int.Parse(x[0]), x => ColorTranslator.FromHtml("#" + x[1]));
                            L($"kbcolors -> {c.Keyboard?.SetKeyColors(id => map.TryGetValue(id, out var col) ? (col.R, col.G, col.B) : ((byte)0, (byte)0, (byte)0), c.S.KeyboardLights)}");
                        }
                        break;
                    case "kbfill":
                        {
                            // kbfill:RRGGBB — every key of the layout (what "Fill all" does)
                            var col = ColorTranslator.FromHtml("#" + p[1]);
                            L($"kbfill -> {c.SetKeyScheme(KeyboardLayout.Keys.ToDictionary(k => k.Index, _ => col), save: false)}");
                        }
                        break;
                    case "kbtpl":
                        {
                            // kbtpl:3 — send a keyboard template (index in KeyboardLayout.Templates) to the keyboard
                            var t = KeyboardLayout.Templates[int.Parse(p[1])];
                            var colors = new Dictionary<int, Color>();
                            foreach (var k in KeyboardLayout.Keys)
                            {
                                var col = t.ColorOf(k);
                                if (col.R + col.G + col.B > 0) colors[k.Index] = col;
                            }
                            L($"kbtpl {t.NameKey} -> {c.SetKeyScheme(colors, save: false)}");
                        }
                        break;
                    case "kbids":
                        {
                            // kbids:43,62,63,64:FF0000 — light only these ids
                            var ids = p[1].Split(',').Select(int.Parse).ToHashSet();
                            var col = ColorTranslator.FromHtml("#" + p[2]);
                            L($"kbids {p[1]} -> {c.Keyboard?.SetKeyColors(id => ids.Contains(id) ? (col.R, col.G, col.B) : ((byte)0, (byte)0, (byte)0), c.S.KeyboardLights)}");
                        }
                        break;
                    case "status":
                        L($"status keyboard={c.Keyboard?.Status()} chassis={c.Chassis?.Status()}");
                        break;
                    case "restore":
                        if (c.Awcc != null)
                        {
                            c.SetBoost(0);
                            if (startProfile >= 0) L($"restore profile 0x{startProfile:X2} -> {c.SetProfile((byte)startProfile)}");
                        }
                        break;
                    default:
                        L("unknown step " + step);
                        break;
                }
            }
            catch (Exception ex) { L($"{step} failed: {ex.Message}"); }
        }
        L("done");
        return 0;
    }
}
