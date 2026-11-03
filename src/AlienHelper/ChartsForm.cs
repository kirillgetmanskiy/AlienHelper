using System.Drawing;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>Temperature / fan history and per-core load, all fed by the shared SensorMonitor.</summary>
public sealed class ChartsForm : Form
{
    static Color[] FanColors => new[] { Data("#1FB6FF"), Data("#62D2FF"), Data("#FF8A3D"), Data("#FFB37D") };

    readonly SensorMonitor _mon;
    readonly LineChart _temps = new() { Title = L.T("ch.temps"), FixedMin = 30, FixedMax = 100, Capacity = SensorMonitor.Capacity };
    readonly LineChart _fans = new() { Title = L.T("ch.fans"), FixedMin = 0, Capacity = SensorMonitor.Capacity, AxisFormat = "0" };
    readonly LineChart _clocks = new() { Title = L.T("ch.clocks"), FixedMin = 0, FixedMax = 6, Capacity = SensorMonitor.Capacity, AxisFormat = "0.0" };
    readonly LineChart _memory = new() { Title = L.T("ch.memory"), FixedMin = 0, Capacity = SensorMonitor.Capacity, AxisFormat = "0.0" };
    readonly LineChart _ssd = new() { Title = L.T("ch.ssd"), FixedMin = 20, FixedMax = 90, Capacity = SensorMonitor.Capacity };
    readonly LineChart _net = new() { Title = L.T("ch.net"), FixedMin = 0, Capacity = SensorMonitor.Capacity, AxisFormat = "0.0" };
    readonly LineChart _ssdIo = new() { Title = L.T("ch.ssdio"), FixedMin = 0, Capacity = SensorMonitor.Capacity, AxisFormat = "0" };
    static Color[] DiskColors => new[] { Data("#C9A7FF"), Data("#8FD3FF"), Data("#FFE08A") };
    static Color[] ReadColors => new[] { Data("#62D2FF"), Data("#C9A7FF"), Data("#8FFFD0") };
    static Color[] WriteColors => new[] { Data("#FF8A3D"), Data("#FFE08A"), Data("#FF6B8B") };
    bool _clocksFlat;   // the clocks chart shows one CPU line (no P/E cores)
    readonly Label _coreHeader = new() { AutoSize = false, ForeColor = Fg, Font = Semibold(10.5f), Text = L.T("ch.cores.loading") };
    readonly Label _coreNote = new()
    {
        AutoSize = false, ForeColor = Muted, Font = Theme.Font(8.5f), TextAlign = ContentAlignment.MiddleRight,
        Text = L.T("ch.coretemp"),
    };
    readonly LinkLabel _pawnNotice = PawnIoNotice.Make("pawn.core");
    bool _pawnShown;
    readonly Panel _coreHost = new() { BackColor = Bg };
    readonly Panel _scroll = new() { BackColor = Bg, Dock = DockStyle.Fill, AutoScroll = true };
    readonly List<CoreTile> _tiles = new();

    public ChartsForm(Controller c, SensorMonitor mon)
    {
        _mon = mon;
        Text = Log.AppTitle + " — " + L.T("btn.charts");
        Style(this);
        // open over most of the work area; content scrolls if the window is made smaller
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(S(720), S(480));
        var wa = Screen.PrimaryScreen!.WorkingArea;
        int ww = Math.Min(wa.Width - S(40), S(1400));
        Bounds = new Rectangle(wa.X + (wa.Width - ww) / 2, wa.Y + S(8), ww, wa.Height - S(16));

        _temps.Series.Add(new Series { Name = "CPU", Color = Warm, Unit = "°" });
        _temps.Series.Add(new Series { Name = "GPU", Color = Good, Unit = "°" });
        string ghz = " " + L.T("u.ghz"), gb = " " + L.T("u.gb");
        _clocks.Series.Add(new Series { Name = "P-core", Color = Accent, Unit = ghz });
        _clocks.Series.Add(new Series { Name = "E-core", Color = Extra, Unit = ghz });
        _memory.Series.Add(new Series { Name = "RAM", Color = Data("#62D2FF"), Unit = gb });
        int maxRpm = c.Awcc?.Fans.Select(f => f.MaxRpm).DefaultIfEmpty(6000).Max() ?? 6000;
        _fans.FixedMax = (float)Math.Ceiling((maxRpm + 400) / 1000.0) * 1000;
        if (c.Awcc != null)
            for (int i = 0; i < c.Awcc.Fans.Count; i++)
            {
                var f = c.Awcc.Fans[i];
                string grp = Hardware.Awcc.SensorNames.GetValueOrDefault(f.SensorId, "Fan");
                int n = c.Awcc.Fans.Take(i + 1).Count(x => x.SensorId == f.SensorId);
                _fans.Series.Add(new Series { Name = $"{grp} {n}", Color = FanColors[i % FanColors.Length] });
            }

        _scroll.Controls.AddRange(new Control[] { _temps, _fans, _clocks, _memory, _ssd, _ssdIo, _net, _coreHeader, _coreNote, _pawnNotice, _coreHost });
        Controls.Add(_scroll);
        _scroll.Resize += (_, _) => DoLayout();
        _mon.Updated += OnSample;
        // reading starts when the window appears and stops when it is minimized or closed
        Shown += (_, _) => { _since = SensorMonitor.SampleNo; _mon.Want(this, Need.All); DoLayout(); OnSample(); _glide.Start(); };
        Resize += (_, _) => { if (IsHandleCreated) _mon.Want(this, Visible && WindowState != FormWindowState.Minimized ? Need.All : Need.None); };
        VisibleChanged += (_, _) => _mon.Want(this, Visible && WindowState != FormWindowState.Minimized ? Need.All : Need.None);
        FormClosed += (_, _) => { _mon.Updated -= OnSample; _mon.Want(this, Need.None); _glide.Dispose(); };
        Disposed += (_, _) => { _mon.Updated -= OnSample; _mon.Want(this, Need.None); };
        _glide.Tick += (_, _) => Glide();
    }

    // ~30 fps: slide the charts between samples instead of jumping a whole step when one arrives
    readonly System.Windows.Forms.Timer _glide = new() { Interval = 33 };

    int _glidePx = -1;

    void Glide()
    {
        if (!Visible || WindowState == FormWindowState.Minimized) return;
        // only worth it when a step is wider than a pixel (slow refresh); fast refresh already looks continuous.
        // Redraw only when the slide moved a whole pixel: a 2 s step of ~5 px costs ~5 redraws, not 60.
        float step = (_temps.Width - S(64)) / (float)Math.Max(1, SensorMonitor.Capacity - 1);
        float phase = step < 1.5f ? 0 : Math.Clamp((SensorMonitor.Clock.ElapsedMilliseconds - SensorMonitor.LastSampleMs) / (float)SensorMonitor.IntervalMs, 0, 1);
        int px = (int)(phase * step);
        if (px == _glidePx) return;
        _glidePx = px;
        foreach (var ch in new[] { _temps, _fans, _clocks, _memory, _ssd, _ssdIo, _net })
        {
            ch.Phase = step > 0 ? px / step : 0;
            ch.Invalidate();
        }
    }

    void BuildTiles()
    {
        // P-cores first, then E-cores.
        foreach (var core in _mon.Cores.OrderByDescending(c => c.Performance).ThenBy(c => c.Index))
        {
            var t = new CoreTile
            {
                Title = _mon.Hybrid ? L.T("ch.core", core.Performance ? "P" : "E", core.Index) : $"CPU {core.Index}",
                Performance = core.Performance,
                Capacity = SensorMonitor.CoreCapacity,
                Tag = core,
            };
            _tiles.Add(t);
            _coreHost.Controls.Add(t);
        }
        DoLayout();
    }

    bool _inLayout;

    void DoLayout()
    {
        if (_inLayout) return;   // AutoScrollMinSize toggles the scrollbar, which resizes the panel
        _inLayout = true;
        try
        {
            int w = _scroll.ClientSize.Width;
            LayoutCore();
            // the vertical scrollbar may have just appeared and taken width: lay out again to fit, or a horizontal one shows up
            if (_scroll.ClientSize.Width != w) LayoutCore();
        }
        finally { _inLayout = false; }
    }

    void LayoutCore()
    {
        int m = S(12), w = _scroll.ClientSize.Width - 2 * m, vh = _scroll.ClientSize.Height;
        int gap = S(8);
        // compact core grid: up to 12 tiles a row, so 24 cores take two rows
        int cols = Math.Clamp(w / S(104), 4, 12);
        int rows = Math.Max(1, (Math.Max(_tiles.Count, _mon.Cores.Count) + cols - 1) / cols);
        int minTile = S(70);

        // charts take ~55% of the free height (140…210 px each), the core grid the rest
        int free = vh - (m + 3 * gap + S(4) + S(28) + (rows - 1) * gap + m);
        int chartH = Math.Clamp(free * 11 / 20 / 3, S(140), S(210));

        int y = m - _scroll.VerticalScroll.Value;
        // 3 x 2 chart grid, then the network chart across the full width
        int cw = (w - 2 * gap) / 3;
        void Row(params LineChart[] charts)
        {
            for (int i = 0; i < charts.Length; i++)
            {
                int x = m + i * (cw + gap);
                charts[i].SetBounds(x, y, i == charts.Length - 1 ? m + w - x : cw, chartH);
            }
            y += chartH + gap;
        }
        Row(_temps, _fans, _clocks);
        Row(_memory, _ssd, _ssdIo);
        Row(_net);
        y += S(4);
        _coreHeader.SetBounds(m + S(2), y, w / 2, S(24));
        _coreNote.SetBounds(m + w / 2, y, w / 2, S(24));
        y += S(28);
        if (_pawnShown)
        {
            _pawnNotice.SetBounds(m + S(2), y, w, S(22));
            y += S(26);
        }

        int th = Math.Clamp((vh - (y + _scroll.VerticalScroll.Value) - m - (rows - 1) * gap) / rows, minTile, S(200));
        int hostH = rows * th + (rows - 1) * gap;
        _coreHost.SetBounds(m, y, w, hostH);
        _scroll.AutoScrollMinSize = new Size(0, y + _scroll.VerticalScroll.Value + hostH + m);

        int tw = (w - gap * (cols - 1)) / cols;
        for (int i = 0; i < _tiles.Count; i++)
            _tiles[i].SetBounds((i % cols) * (tw + gap), (i / cols) * (th + gap), tw, th);
    }

    void OnSample()
    {
        if (IsDisposed) return;
        if (_mon.PawnIoMissing != _pawnShown)
        {
            // no driver: a recommendation in place of the note about the temperature line
            _pawnShown = _mon.PawnIoMissing;
            _pawnNotice.Visible = _pawnShown;
            _coreNote.Visible = !_pawnShown;
            DoLayout();
        }
        // the refresh period can change in Settings: the same 5 minutes then hold a different number of points
        foreach (var ch in new[] { _temps, _fans, _clocks, _memory, _ssd, _ssdIo, _net }) { ch.Capacity = SensorMonitor.Capacity; ch.Phase = 0; }
        _glidePx = 0;
        foreach (var t in _tiles) t.Capacity = SensorMonitor.CoreCapacity;
        Copy(_mon.Cpu, _temps.Series[0].Values);
        Copy(_mon.Gpu, _temps.Series[1].Values);
        for (int i = 0; i < _fans.Series.Count && i < _mon.Fans.Count; i++)
            Copy(_mon.Fans[i], _fans.Series[i].Values);
        // each drive gets its own temperature line and its own read / write IOPS lines once it is found
        float io = 0;
        var drives = _mon.Drives;
        // a drive was added or removed: start the drive lines over so none is left behind
        if (!_ssd.Series.Select(x => x.Name).SequenceEqual(drives.Select(x => x.Name))) { _ssd.Series.Clear(); _ssdIo.Series.Clear(); }
        for (int d = 0; d < drives.Count; d++)
        {
            var drv = drives[d];
            Copy(drv.Temps, Ensure(_ssd, drv.Name, DiskColors[d % DiskColors.Length], "°").Values);
            var r = Ensure(_ssdIo, $"{drv.Label} {L.T("io.read")}", ReadColors[d % ReadColors.Length], "");
            var w = Ensure(_ssdIo, $"{drv.Label} {L.T("io.write")}", WriteColors[d % WriteColors.Length], "");
            Copy(drv.Reads, r.Values);
            Copy(drv.Writes, w.Values);
            io = Math.Max(io, Math.Max(r.Values.DefaultIfEmpty(0).Max(), w.Values.DefaultIfEmpty(0).Max()));
        }
        if (!_clocksFlat && !_mon.Hybrid)
        {
            // one kind of core (AMD, older Intel): one CPU line instead of P-core and E-core
            _clocksFlat = true;
            var p = _clocks.Series[0];
            _clocks.Series[0] = new Series { Name = "CPU", Color = p.Color, Unit = p.Unit };
            _clocks.Series.RemoveAt(1);
        }
        Copy(_mon.PClock, _clocks.Series[0].Values);
        if (!_clocksFlat) Copy(_mon.EClock, _clocks.Series[1].Values);
        Copy(_mon.RamUsed, _memory.Series[0].Values);
        // one line per NVIDIA GPU; numbered only when there is more than one
        var gpus = _mon.Gpus;
        for (int i = 0; i < gpus.Count; i++)
        {
            string n = gpus.Count > 1 ? $" {i + 1}" : "";
            var gc = GpuColors[i % GpuColors.Length];
            Copy(gpus[i].Clock, Ensure(_clocks, "GPU" + n, gc, " " + L.T("u.ghz")).Values);
            Copy(gpus[i].Vram, Ensure(_memory, "VRAM" + n, VramColors[i % VramColors.Length], " " + L.T("u.gb")).Values);
            // the first GPU's temperature comes from the AWCC sensor (above); the others from NVML
            if (i > 0) Copy(gpus[i].Temps, Ensure(_temps, "GPU" + n, gc, "°").Values);
            if (gpus[i].MemTemps.Count > 0) Copy(gpus[i].MemTemps, Ensure(_temps, "VRAM" + n, VramColors[i % VramColors.Length], "°").Values);
        }
        if (gpus.Count > 1 && _temps.Series[1].Name == "GPU") _temps.Series[1] = new Series { Name = "GPU 1", Color = Good, Unit = "°" };
        // IOPS swing by orders of magnitude: round the scale up to a 1-2-5 step of the visible peak
        _ssdIo.FixedMax = NiceMax(io);
        // network: download and upload line per active connection, Mbit/s
        var links = _mon.Links;
        var netNames = links.SelectMany(l => new[] { l.Name + " ↓", l.Name + " ↑" }).ToList();
        if (!_net.Series.Select(x => x.Name).SequenceEqual(netNames)) _net.Series.Clear();
        float netMax = 0;
        for (int i = 0; i < links.Count; i++)
        {
            Copy(links[i].Down, Ensure(_net, links[i].Name + " ↓", ReadColors[i % ReadColors.Length], " " + L.T("u.mbps")).Values);
            Copy(links[i].Up, Ensure(_net, links[i].Name + " ↑", WriteColors[i % WriteColors.Length], " " + L.T("u.mbps")).Values);
            netMax = Math.Max(netMax, _net.Series.Skip(2 * i).Take(2).SelectMany(x => x.Values).DefaultIfEmpty(0).Max());
        }
        _net.FixedMax = NiceMax(Math.Max(1, netMax));
        float memMax = Math.Max(_mon.RamTotal, gpus.Select(x => x.Stats?.VramTotalGb ?? 0).DefaultIfEmpty(0).Max());
        if (memMax > 0) _memory.FixedMax = (float)Math.Ceiling(memMax / 4) * 4;
        _temps.Invalidate();
        _fans.Invalidate();
        _clocks.Invalidate();
        _memory.Invalidate();
        _ssd.Invalidate();
        _ssdIo.Invalidate();
        _net.Invalidate();

        if (_tiles.Count == 0 && _mon.Cores.Count > 0) BuildTiles();
        foreach (var t in _tiles)
        {
            var core = (CoreMonitor.Core)t.Tag!;
            t.Load = core.Load;
            t.Mhz = core.Mhz;
            Copy(core.History, t.History);
            t.Temp = core.Temp;
            Copy(core.Temps, t.Temps);
            t.Invalidate();
        }
        if (_mon.Cores.Count > 0)
        {
            int p = _mon.Cores.Count(c => c.Performance), e = _mon.Cores.Count - p;
            _coreHeader.Text = _mon.Hybrid ? L.T("ch.cores", _mon.CpuLoad.ToString("0"), p, e) : L.T("ch.cores.flat", _mon.CpuLoad.ToString("0"), p);
            int hot = _mon.Cores.Select(c => c.Temp).DefaultIfEmpty(-1).Max();
            _coreNote.Text = hot > 0 ? L.T("ch.coretemp.on", hot) : _mon.CoreTempsUnsupported ? L.T("ch.coretemp.intel") : L.T("ch.coretemp");
        }
    }

    static Color[] GpuColors => new[] { Good, Data("#B4F07A"), Data("#4FD8C4") };
    static Color[] VramColors => new[] { Extra, Data("#D08BFF"), Data("#7B9BFF") };

    /// <summary>The named series, added on first use.</summary>
    static Series Ensure(LineChart chart, string name, Color color, string unit)
    {
        var s = chart.Series.FirstOrDefault(x => x.Name == name);
        if (s == null) chart.Series.Add(s = new Series { Name = name, Color = color, Unit = unit });
        return s;
    }

    static float NiceMax(float v)
    {
        if (v < 10) return 10;
        float p = (float)Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var k in new[] { 1f, 2f, 5f, 10f }) if (v <= k * p) return k * p;
        return 10 * p;
    }

    /// <summary>Sample number when the window opened: it shows what was read since, not history collected before.</summary>
    long _since;

    void Copy(List<float> from, List<float> to)
    {
        int n = (int)Math.Clamp(SensorMonitor.SampleNo - _since, 0, from.Count);
        to.Clear();
        for (int i = from.Count - n; i < from.Count; i++) to.Add(from[i]);
    }
}
