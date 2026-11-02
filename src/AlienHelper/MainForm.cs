using System.Drawing;
using AlienHelper.Hardware;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>Main window: monitoring, power mode + fan boost, lighting. Options live in <see cref="SettingsForm"/>.</summary>
public sealed class MainForm : Form
{
    static readonly Color[] Presets =
    {
        Hex("#00B4FF"), Hex("#FFFFFF"), Hex("#FF1E1E"), Hex("#FF7A00"), Hex("#00FF6A"), Hex("#8A2BE2"), Hex("#FF2D95"),
    };

    readonly Controller _c;
    readonly SensorMonitor _mon;
    readonly Action _openCharts;

    readonly StatTile _cpu = new() { Title = "CPU", BigSize = Theme.NumbersOnly ? 16f : 18f };
    readonly StatTile _gpu = new() { Title = "GPU", BigSize = Theme.NumbersOnly ? 16f : 18f };
    readonly StatTile _vram = new() { Title = "VRAM", BigSize = 11.5f, HistoryBehind = true };
    readonly StatTile _ram = new() { Title = "RAM", BigSize = 11.5f, HistoryBehind = true };
    readonly List<FanBar> _fans = new();
    readonly List<PillButton> _profiles = new();
    readonly Card _modeCard = new() { Caption = L.T("card.mode") };
    readonly Slider _boost = new();
    readonly Label _boostValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = Fg };
    readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 300 };

    /// <summary>Opens the per-key keyboard editor (set by the tray app).</summary>
    public Action? OpenKeys { get; set; }

    /// <summary>Opens the settings window (set by the tray app).</summary>
    public Action? OpenSettings { get; set; }

    // compact metrics: window margin, card padding, gap between cards, card caption height
    const int W = 380, M = 10, Pad = 12, Gap = 6, Top = 32;

    public MainForm(Controller c, SensorMonitor mon, Action openCharts)
    {
        _c = c; _mon = mon; _openCharts = openCharts;
        Text = Log.AppTitle;
        Style(this);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;

        int y = S(M);
        y = BuildHeader(y);
        _cardsTop = y;
        Controls.Add(_view);
        _cpu.Title = SystemInfo.CpuName.Length > 0 ? SystemInfo.CpuName : "CPU";
        y = BuildChip(y, "cpu", _cpu, _ram, Controller.CpuSensor, out _cpuChip);
        y = BuildChip(y, "gpu", _gpu, _vram, Controller.GpuSensor, out _gpuChip);
        y = BuildSsd(y);
        y = BuildNet(y);
        y = BuildPower(y);
        if (c.Awcc != null) y = BuildModes(y);
        if (c.Screen != null) y = BuildScreen(y);
        if (c.Keyboard != null || c.Chassis != null) y = BuildLights(y);
        ClientSize = new Size(S(W), y + S(M - Gap));
        ApplyCards();

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Load += (_, _) => Location = new Point(wa.Right - Width - S(10), Math.Max(wa.Top, wa.Bottom - Height - S(10)));
        _mon.Updated += OnSample;
        _c.BoostChanged += OnBoostChanged;
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { _mon.Updated -= OnSample; _c.BoostChanged -= OnBoostChanged; if (_c.Screen != null) _c.Screen.Changed -= OnScreenChanged; };
        OnSample();
    }

    int Inner => S(W - 2 * M - 2 * Pad);
    int CardW => S(W - 2 * M);

    static PillButton SmallButton(string text)
    {
        var b = new PillButton(text) { Height = S(24) };
        b.Width = TextRenderer.MeasureText(text, Theme.Font(9.5f)).Width + S(22);
        return b;
    }

    int BuildHeader(int y)
    {
        var title = new Label { Text = Log.AppTitle, Font = Semibold(12f), ForeColor = Fg, AutoSize = true, Location = new Point(S(M), y) };
        Controls.Add(title);
        var sub = new Label { Text = SystemInfo.DisplayModel, Font = Theme.Font(8.5f), ForeColor = Muted, AutoSize = true };
        sub.Location = new Point(S(M) + title.PreferredWidth + S(6), y + (title.PreferredHeight - sub.PreferredHeight) / 2 + S(1));
        Controls.Add(sub);
        if (SystemInfo.ModelCode is { Length: > 0 } code)
        {
            // model code highlighted in brackets, e.g. "(AA16250)"
            var codeLbl = new Label { Text = $"({code})", Font = Semibold(8.5f), ForeColor = Accent, AutoSize = true };
            codeLbl.Location = new Point(sub.Left + sub.PreferredWidth, sub.Top);
            Controls.Add(codeLbl);
        }
        var gear = new PillButton("⚙") { Size = new Size(S(30), S(26)), Subtle = true };
        gear.Location = new Point(S(W - M) - gear.Width, y + (title.PreferredHeight - gear.Height) / 2);
        _tips.SetToolTip(gear, L.T("settings"));
        gear.Click += (_, _) => OpenSettings?.Invoke();
        Controls.Add(gear);
        var charts = new PillButton(L.T("btn.charts")) { Size = gear.Size, Subtle = true, Glyph = Glyphs.Chart };
        charts.Location = new Point(gear.Left - S(6) - charts.Width, gear.Top);
        _tips.SetToolTip(charts, L.T("btn.charts"));
        charts.Click += (_, _) => _openCharts();
        Controls.Add(charts);
        return y + Math.Max(title.PreferredHeight, gear.Height) + S(6);
    }

    /// <summary>Main window sections in display order; the ids are what Settings hides (<see cref="Settings.HiddenCards"/>).</summary>
    readonly List<(string id, Card card)> _cards = new();
    int _cardsTop;
    // the cards scroll under the header when they don't fit on the screen (wheel, thin thumb in the margin)
    readonly SmoothScroll _view = new();

    Card AddCard(string id, Card card)
    {
        _cards.Add((id, card));
        _view.Content.Controls.Add(card);
        return card;
    }

    /// <summary>A CPU or GPU section: rows of (main tile, memory tile), then the fans driven by that chip.</summary>
    sealed class Chip
    {
        public Card Card { get; init; } = null!;
        public List<(StatTile Main, StatTile Mem)> Rows { get; } = new();
        public List<FanBar> Fans { get; } = new();
    }

    Chip _cpuChip = null!, _gpuChip = null!;

    // numbers-only mode: tiles without graphs are lower
    static int RowH => Theme.NumbersOnly ? S(56) : S(64);   // numbers: room for the RAM / VRAM second line
    static int SmallH => Theme.NumbersOnly ? S(50) : S(56);

    int BuildChip(int y, string id, StatTile main, StatTile mem, byte sensor, out Chip chip)
    {
        chip = new Chip { Card = AddCard(id, new Card { Caption = id.ToUpperInvariant() }) };
        chip.Rows.Add((main, mem));
        if (_c.Awcc != null)
        {
            // fans driven by this chip's sensor; anything unknown goes with the CPU
            var fans = _c.Awcc.Fans.Where(f => sensor == Controller.CpuSensor
                ? f.SensorId != Controller.GpuSensor : f.SensorId == Controller.GpuSensor).ToList();
            foreach (var f in fans)
            {
                var bar = new FanBar { Title = FanName(f), MaxRpm = f.MaxRpm, Height = S(26), Tag = _c.Awcc.Fans.IndexOf(f) };
                _fans.Add(bar);
                chip.Fans.Add(bar);
                chip.Card.Controls.Add(bar);
            }
        }
        chip.Card.SetBounds(S(M), y, CardW, 0);
        LayoutChip(chip);
        return chip.Card.Bottom + S(Gap);
    }

    /// <summary>Stacks the tile rows (one per GPU) and the fans, two per line, and sizes the card.</summary>
    void LayoutChip(Chip chip)
    {
        int cy = S(Top + 4);
        int third = (Inner - 2 * S(Gap)) / 3, half = (Inner - S(Gap)) / 2;
        foreach (var (main, mem) in chip.Rows)
        {
            main.SetBounds(S(Pad), cy, Inner - third - S(Gap), RowH);
            mem.SetBounds(S(Pad) + Inner - third, cy, third, RowH);
            if (main.Parent != chip.Card) chip.Card.Controls.Add(main);
            if (mem.Parent != chip.Card) chip.Card.Controls.Add(mem);
            cy += RowH + S(Gap);
        }
        cy += S(2);
        for (int i = 0; i < chip.Fans.Count; i++)
            chip.Fans[i].SetBounds(S(Pad) + (i % 2) * (half + S(Gap)), cy + (i / 2) * S(28), half, S(26));
        cy += (chip.Fans.Count + 1) / 2 * S(28);
        chip.Card.Height = cy + S(4);
    }

    /// <summary>One GPU row per NVIDIA card (at least one, which also covers a GPU NVML cannot see).</summary>
    void SyncGpuRows()
    {
        int want = Math.Max(1, _mon.Gpus.Count);
        if (_gpuChip.Rows.Count == want) return;
        while (_gpuChip.Rows.Count > want)
        {
            var (main, mem) = _gpuChip.Rows[^1];
            _gpuChip.Rows.RemoveAt(_gpuChip.Rows.Count - 1);
            main.Dispose(); mem.Dispose();
        }
        while (_gpuChip.Rows.Count < want)
            _gpuChip.Rows.Add((new StatTile { Title = "GPU", BigSize = Theme.NumbersOnly ? 16f : 18f }, new StatTile { Title = "VRAM", BigSize = 11.5f, HistoryBehind = true }));
        LayoutChip(_gpuChip);
        Reflow();
    }

    readonly Card _ssdCard = new() { Caption = "SSD" };
    readonly List<StatTile> _ssdTiles = new();

    int BuildSsd(int y)
    {
        AddCard("ssd", _ssdCard);
        _ssdCard.SetBounds(S(M), y, CardW, 0);
        LayoutSsd(0);
        return _ssdCard.Bottom + S(Gap);
    }

    /// <summary>One tile per drive, up to three in a row. Until the first reading there is a single placeholder.</summary>
    void LayoutSsd(int drives)
    {
        foreach (var t in _ssdTiles) { _ssdCard.Controls.Remove(t); t.Dispose(); }
        _ssdTiles.Clear();
        int n = Math.Max(1, drives), cols = Math.Min(3, n);
        int tw = (Inner - S(Gap) * (cols - 1)) / cols, th = SmallH;
        int cy = S(Top + 4);
        for (int i = 0; i < n; i++)
        {
            var t = new StatTile { Title = "SSD", BigSize = 11.5f, HistoryBehind = true, Value = "—", Sub = drives == 0 ? L.T("card.nodisks") : "" };
            t.SetBounds(S(Pad) + (i % cols) * (tw + S(Gap)), cy + (i / cols) * (th + S(Gap)), tw, th);
            _ssdTiles.Add(t);
            _ssdCard.Controls.Add(t);
        }
        _ssdCard.Height = cy + (n + cols - 1) / cols * (th + S(Gap)) - S(Gap) + S(Pad);
    }

    readonly Card _netCard = new() { Caption = L.T("card.net") };
    readonly List<StatTile> _netTiles = new();

    int BuildNet(int y)
    {
        AddCard("net", _netCard);
        _netCard.SetBounds(S(M), y, CardW, 0);
        LayoutNet(0);
        return _netCard.Bottom + S(Gap);
    }

    /// <summary>One tile per active connection, two in a row (speeds need the width). A placeholder when there is none.</summary>
    void LayoutNet(int links)
    {
        foreach (var t in _netTiles) { _netCard.Controls.Remove(t); t.Dispose(); }
        _netTiles.Clear();
        int n = Math.Max(1, links), cols = Math.Min(2, n);
        int tw = (Inner - S(Gap) * (cols - 1)) / cols, th = SmallH;
        int cy = S(Top + 4);
        for (int i = 0; i < n; i++)
        {
            var t = new StatTile { Title = L.T("card.net"), BigSize = 11.5f, HistoryBehind = true, Value = "—", Sub = links == 0 ? L.T("card.nonet") : "" };
            t.SetBounds(S(Pad) + (i % cols) * (tw + S(Gap)), cy + (i / cols) * (th + S(Gap)), tw, th);
            _netTiles.Add(t);
            _netCard.Controls.Add(t);
        }
        _netCard.Height = cy + (n + cols - 1) / cols * (th + S(Gap)) - S(Gap) + S(Pad);
    }

    readonly Card _powerCard = new() { Caption = L.T("card.power") };
    readonly StatTile _power = new() { Title = L.T("card.power"), BigSize = 15f, HistoryBehind = true, Value = "—" };
    readonly List<PillButton> _powerModes = new();
    readonly LinkLabel _pawnNotice = PawnIoNotice.Make("pawn.power");
    bool _pawnShown;

    /// <summary>Power: the laptop's draw (measured on battery, CPU + GPU on AC) and the Windows power mode.</summary>
    int BuildPower(int y)
    {
        AddCard("power", _powerCard);
        int cy = S(Top + 4);
        _power.SetBounds(S(Pad), cy, Inner, SmallH);
        _tips.SetToolTip(_power, L.T("pw.tip"));
        _powerCard.Controls.Add(_power);
        cy += SmallH + S(8);
        var lbl = new Label { Text = L.T("pw.mode"), ForeColor = Muted, Font = Theme.Font(9f), AutoSize = true, Location = new Point(S(Pad), cy + S(4)) };
        _powerCard.Controls.Add(lbl);
        int x0 = S(Pad) + lbl.PreferredWidth + S(10), gap = S(5);
        int w = (S(Pad) + Inner - x0 - 2 * gap) / 3;
        var modes = new[] { WindowsPowerMode.Mode.Efficiency, WindowsPowerMode.Mode.Balanced, WindowsPowerMode.Mode.Performance };
        string[] names = { L.T("pw.eff"), L.T("pw.bal"), L.T("pw.perf") };
        for (int i = 0; i < 3; i++)
        {
            var mode = modes[i];
            var b = new PillButton(names[i]) { Bounds = new Rectangle(x0 + i * (w + gap), cy, w, S(26)), Tag = mode };
            b.Click += (_, _) =>
            {
                bool ok = WindowsPowerMode.Set(mode);
                Log.Info($"windows power mode -> {mode}{(ok ? "" : " (refused)")}");
                SyncPowerModes();
            };
            _powerModes.Add(b);
            _powerCard.Controls.Add(b);
        }
        cy += S(26) + S(6);
        _powerCard.SetBounds(S(M), y, CardW, cy + S(4));
        int noteH = TextRenderer.MeasureText(_pawnNotice.Text, _pawnNotice.Font, new Size(Inner, 0), TextFormatFlags.WordBreak).Height + S(4);
        _pawnNotice.SetBounds(S(Pad), cy, Inner, noteH);
        _powerCard.Controls.Add(_pawnNotice);
        SyncPowerModes();
        return _powerCard.Bottom + S(Gap);
    }

    void SyncPowerModes()
    {
        var cur = WindowsPowerMode.Get();
        foreach (var b in _powerModes) b.Active = (WindowsPowerMode.Mode)b.Tag! == cur;
    }

    static string Hours(float h)
    {
        int m = (int)Math.Round(h * 60);
        return m >= 60 ? $"{m / 60} {L.T("u.h")} {m % 60} {L.T("u.min")}" : $"{m} {L.T("u.min")}";
    }

    void UpdatePower()
    {
        if (_c.S.HiddenCards.Contains("power")) return;
        if (_mon.PawnIoMissing != _pawnShown)
        {
            // without the driver the total on AC is only CPU + GPU: say so and recommend it
            _pawnShown = _mon.PawnIoMissing;
            _pawnNotice.Visible = _pawnShown;
            _powerCard.Height += (_pawnShown ? 1 : -1) * (_pawnNotice.Height + S(4));
            Reflow();
        }
        var b = _mon.Battery;
        float w = _mon.PowerTotalW;
        if (b is { OnAc: false })
        {
            // on battery the gauge measures the whole laptop
            _power.Title = L.T("pw.battery", b.Percent);
            _power.Value = w > 0 ? $"{w:0.0} {L.T("u.w")}" : "—";
            _power.Sub = b.HoursLeft > 0 ? L.T("pw.left", Hours(b.HoursLeft)) : "";
        }
        else
        {
            // on AC: the platform energy counter (whole platform); without PawnIO only CPU + GPU is measured
            float gpu = _mon.Gpus.Sum(x => x.Stats is { PowerW: > 0 } st ? st.PowerW : 0);
            _power.Title = b == null ? L.T("pw.acnobat") : b.Charging ? L.T("pw.charging", b.Percent, $"{b.ChargeW:0}") : L.T("pw.ac", b.Percent);
            _power.Value = w <= 0 ? "—" : _mon.PowerFrom == SensorMonitor.PowerSource.Platform ? $"{w:0} {L.T("u.w")}" : $"CPU+GPU {w:0} {L.T("u.w")}";
            _power.Sub = _mon.CpuPowerW > 0 ? L.T("pw.cpugpu", $"{_mon.CpuPowerW:0}", $"{gpu:0}") : "";
        }
        _power.ValueColor = Warn;
        _power.History = _mon.PowerTotal.Recent(80);
        _power.HistoryMin = 0;
        _power.HistoryMax = Math.Max(20, _power.History.DefaultIfEmpty(0).Max() * 1.15f);
        _power.Invalidate();
        SyncPowerModes();
    }

    /// <summary>Formats a speed in Mbit/s as Kbps / Mbps / Gbps.</summary>
    internal static string Rate(float mbps) =>
        mbps < 1 ? $"{mbps * 1000:0} {L.T("u.kbps")}" : mbps < 1000 ? $"{mbps:0.0} {L.T("u.mbps")}" : $"{mbps / 1000:0.00} {L.T("u.gbps")}";

    /// <summary>Connection tiles: download speed, upload and link speed below, total traffic as the background graph.</summary>
    void UpdateLinks()
    {
        var links = _mon.Links;
        int want = links.Count;
        if (want != _netTiles.Count && !(want == 0 && _netTiles.Count == 1 && _netTiles[0].Sub == L.T("card.nonet")))
        {
            LayoutNet(want);
            Reflow();
        }
        for (int i = 0; i < links.Count && i < _netTiles.Count; i++)
        {
            var l = links[i];
            var t = _netTiles[i];
            t.Title = l.Name;
            if (l.DownMbps >= 0)
            {
                t.Value = "↓ " + Rate(l.DownMbps);
                t.Sub = "↑ " + Rate(l.UpMbps) + (l.LinkMbps > 0 ? " · " + L.T("net.link", Rate(l.LinkMbps)) : "");
                var io = l.Down.Zip(l.Up, (d, u) => d + u).Recent(80);
                t.History = io;
                t.HistoryMin = 0; t.HistoryMax = Math.Max(1, io.DefaultIfEmpty(0).Max() * 1.1f);
            }
            if (_tips.GetToolTip(t) != l.Description) _tips.SetToolTip(t, l.Description);
            t.Invalidate();
        }
    }

    /// <summary>Drive tiles: temperature, read / write IOPS below it, total IOPS as the background graph.</summary>
    void UpdateDrives()
    {
        var drives = _mon.Drives;
        if (drives.Count > 0 && drives.Count != _ssdTiles.Count)
        {
            LayoutSsd(drives.Count);
            Reflow();
        }
        for (int i = 0; i < drives.Count && i < _ssdTiles.Count; i++)
        {
            var d = drives[i];
            var t = _ssdTiles[i];
            t.Title = d.Name;
            t.Value = d.Temp > 0 ? $"{d.Temp}°" : "—";
            t.ValueColor = d.Temp >= 70 ? Warm : d.Temp >= 55 ? Warn : Fg;
            if (d.ReadIops >= 0)
            {
                t.Sub = L.T("io.short", Iops(d.ReadIops), Iops(d.WriteIops));
                var io = d.Reads.Zip(d.Writes, (r, w) => r + w).Recent(80);
                t.History = io;
                t.HistoryMin = 0; t.HistoryMax = Math.Max(50, io.DefaultIfEmpty(0).Max() * 1.1f);
            }
            if (_tips.GetToolTip(t) != d.Model) _tips.SetToolTip(t, d.Model);
            t.Invalidate();
        }
    }

    static IEnumerable<Control> Descendants(Control c)
    {
        foreach (Control x in c.Controls)
        {
            yield return x;
            foreach (var y in Descendants(x)) yield return y;
        }
    }

    /// <summary>
    /// Sources behind the main window's enabled sections. They are read all the time, also while the window sits in
    /// the tray, so the window always opens with current values; everything else is read only by the window showing it.
    /// </summary>
    public static Need NeedFor(Controller c)
    {
        var hidden = c.S.HiddenCards;
        var need = Need.None;
        bool On(string id) => !hidden.Contains(id);
        if (On("cpu")) need |= Need.Fans | Need.Cpu;
        if (On("gpu")) need |= Need.Fans | Need.Gpu;
        if (On("ssd")) need |= Need.Disks;
        if (On("net")) need |= Need.Net;
        if (On("power")) need |= Need.Power | Need.Cpu | Need.Gpu;   // on AC the total is CPU + GPU
        if (On("mode") && c.Awcc != null) need |= Need.Mode;
        return need;
    }

    /// <summary>Shows the sections enabled in Settings (at least one) and re-stacks the window.</summary>
    public void ApplyCards()
    {
        var hidden = _c.S.HiddenCards;
        if (_cards.All(x => hidden.Contains(x.id))) hidden.Remove(_cards[0].id);
        foreach (var (id, card) in _cards) card.Visible = !hidden.Contains(id);
        Reflow();
    }

    int BuildModes(int y)
    {
        var a = _c.Awcc!;
        const int cols = 3;
        int bw = (Inner - S(Gap) * (cols - 1)) / cols, bh = S(28);
        int cy = S(Top + 4);
        for (int i = 0; i < a.Profiles.Count; i++)
        {
            byte p = a.Profiles[i];
            var b = new PillButton(_c.ProfileName(p).Replace("Balanced Performance", "Balanced Perf"))
            {
                Tag = p,
                AccentColor = p == Awcc.ProfileGMode ? Warm : Accent,
                Bounds = new Rectangle(S(Pad) + (i % cols) * (bw + S(Gap)), cy + (i / cols) * (bh + S(5)), bw, bh),
            };
            if (Controller.ModeTip(p) is { Length: > 0 } tip) _tips.SetToolTip(b, tip);
            b.Click += async (_, _) =>
            {
                foreach (var x in _profiles) x.Active = x == b;
                _customMode.Active = false;
                await Task.Run(() => _c.SelectMode(p));
                await _mon.Poll();
            };
            _profiles.Add(b);
            _modeCard.Controls.Add(b);
        }
        // Custom: its own mode, the fans follow the curves
        int ci = a.Profiles.Count;
        _customMode.AccentColor = Hex("#9B7BFF");
        _customMode.Bounds = new Rectangle(S(Pad) + (ci % cols) * (bw + S(Gap)), cy + (ci / cols) * (bh + S(5)), bw, bh);
        _tips.SetToolTip(_customMode, L.T("boost.custom.tip"));
        _customMode.Click += async (_, _) =>
        {
            foreach (var x in _profiles) x.Active = false;
            _customMode.Active = true;
            await Task.Run(() => _c.SelectMode(Controller.CustomMode));
            await _mon.Poll();
        };
        _modeCard.Controls.Add(_customMode);
        cy += (a.Profiles.Count + 1 + cols - 1) / cols * (bh + S(5)) + S(2);

        // fan boost: caption + "split" switch on one line, then one slider (all fans) or two (CPU / GPU fans)
        var lbl = new Label { Text = L.T("tray.boost"), ForeColor = Muted, Font = Theme.Font(9f), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        lbl.SetBounds(S(Pad), cy, Inner - S(110), S(22));
        _split.Height = S(22);
        _split.Width = TextRenderer.MeasureText(_split.Text, Theme.Font(9.5f)).Width + S(22);
        _split.Location = new Point(S(Pad) + Inner - _split.Width, cy);
        _split.Click += (_, _) => ToggleSplit();
        _tips.SetToolTip(_split, L.T("boost.split.tip"));
        // in Custom "Curves…" takes the place of "Split" and opens the editor
        _curvesBtn.Height = S(22);
        _curvesBtn.Width = TextRenderer.MeasureText(_curvesBtn.Text, Theme.Font(9.5f)).Width + S(22);
        _curvesBtn.Location = new Point(S(Pad) + Inner - _curvesBtn.Width, cy);
        _curvesBtn.Click += (_, _) => OpenCurves?.Invoke();
        // Overclock: GPU clock offsets inside Custom (preset in the curve editor)
        _ocBtn.Height = S(22);
        _ocBtn.Width = TextRenderer.MeasureText(_ocBtn.Text, Theme.Font(9.5f)).Width + S(22);
        _ocBtn.Location = new Point(_curvesBtn.Left - S(6) - _ocBtn.Width, cy);
        _ocBtn.Click += (_, _) => { GpuOverclock.Toggle(_c, !_c.S.GpuOcOn, this); LayoutBoost(); };
        lbl.Width = Math.Min(_split.Left, _ocBtn.Left) - S(Pad) - S(4);   // the caption must not cover the buttons
        _boostTop = cy + S(24);

        _boost.Value = Math.Clamp(_c.S.FanBoost, 0, 100);
        _boost.ValueChanged += (_, _) => UpdateBoostLabels();
        _boost.ValueCommitted += async (_, _) => { int v = _boost.Value; _c.S.FanBoost = v; await Task.Run(() => _c.SetBoost(v)); };
        _boostCpu.Value = Math.Clamp(_c.S.FanBoostCpu, 0, 100);
        _boostCpu.ValueChanged += (_, _) => UpdateBoostLabels();
        _boostCpu.ValueCommitted += async (_, _) => { int v = _boostCpu.Value; _c.S.FanBoostCpu = v; await Task.Run(() => _c.SetBoostGroup(Controller.CpuSensor, v)); };
        _boostGpu.Value = Math.Clamp(_c.S.FanBoostGpu, 0, 100);
        _boostGpu.ValueChanged += (_, _) => UpdateBoostLabels();
        _boostGpu.ValueCommitted += async (_, _) => { int v = _boostGpu.Value; _c.S.FanBoostGpu = v; await Task.Run(() => _c.SetBoostGroup(Controller.GpuSensor, v)); };
        foreach (var s in new[] { _boost, _boostCpu, _boostGpu }) _tips.SetToolTip(s, L.T("boost.tip"));
        foreach (var l in new[] { _boostValue, _cpuValue, _gpuValue, _cpuLabel, _gpuLabel }) l.Font = Theme.Font(9f);
        _modeCard.Controls.AddRange(new Control[] { lbl, _split, _curvesBtn, _ocBtn, _boost, _boostValue, _cpuLabel, _boostCpu, _cpuValue, _gpuLabel, _boostGpu, _gpuValue });

        _modeCard.SetBounds(S(M), y, CardW, 0);
        LayoutBoost();
        AddCard("mode", _modeCard);
        return _modeCard.Bottom + S(Gap);
    }

    /// <summary>Custom: the curves set the boost; Alien Mode: the fans run flat out. Only the other modes take the sliders.</summary>
    void LockSliders()
    {
        bool alien = !_c.S.FanCurve && (_mon.Last?.Profile ?? _c.S.Profile) == Awcc.ProfileGMode;
        bool locked = _c.S.FanCurve || alien;
        foreach (var sl in new[] { _boost, _boostCpu, _boostGpu })
            if (sl.Enabled == locked) { sl.Enabled = !locked; sl.Invalidate(); }
    }

    // expert fan control: one slider for all fans, or separate CPU and GPU sliders
    readonly PillButton _split = new(L.T("boost.split")) { Subtle = true };
    readonly PillButton _customMode = new("Custom");
    readonly PillButton _curvesBtn = new(L.T("curves.btn")) { Subtle = true };
    readonly PillButton _ocBtn = new("Overclock") { Subtle = true, AccentColor = Hex("#FF6A3D") };

    /// <summary>Opens the fan curve editor (set by the tray app).</summary>
    public Action? OpenCurves { get; set; }
    /// <summary>Custom entered or left (any window, the tray, the G key): relayout the boost area.</summary>
    public void RefreshBoost() { LayoutBoost(); Reflow(); }
    readonly Slider _boostCpu = new(), _boostGpu = new();
    readonly Label _cpuValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = Fg };
    readonly Label _gpuValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = Fg };
    readonly Label _cpuLabel = new() { Text = "CPU", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted };
    readonly Label _gpuLabel = new() { Text = "GPU", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted };
    int _boostTop;

    /// <summary>Places the boost slider(s) for the current mode and resizes the card.</summary>
    void LayoutBoost()
    {
        // Custom shows the CPU and GPU boost the curves set; the sliders are locked then
        bool curve = _c.S.FanCurve;
        bool split = _c.S.FanExpert || curve;
        _split.Active = _c.S.FanExpert;
        _split.Visible = !curve;
        _customMode.Active = curve;
        _curvesBtn.Visible = curve;
        _ocBtn.Visible = curve;
        _ocBtn.Active = _c.S.GpuOcOn;
        var ocp = GpuOverclock.Preset(_c.S.GpuOc);
        _tips.SetToolTip(_ocBtn, L.T("oc.tip", L.T("oc." + ocp.id), HotKey.Describe(_c.S.OcHotkey)));
        LockSliders();
        int cy = _boostTop, valW = S(52), lblW = S(50);
        _boost.Visible = _boostValue.Visible = !split;
        foreach (var ctl in new Control[] { _cpuLabel, _boostCpu, _cpuValue, _gpuLabel, _boostGpu, _gpuValue }) ctl.Visible = split;
        if (!split)
        {
            // the slider keeps 10 px of its own padding on the left; on the right it stops short of the value, so the knob
            // at 100 % never covers "+100%"
            _boost.SetBounds(S(Pad - 10), cy, Inner + S(6) - valW, S(24));
            _boostValue.SetBounds(S(Pad) + Inner - valW, cy, valW, S(24));
            cy += S(24);
        }
        else
        {
            foreach (var (label, slider, value) in new[] { (_cpuLabel, _boostCpu, _cpuValue), (_gpuLabel, _boostGpu, _gpuValue) })
            {
                label.SetBounds(S(Pad), cy, lblW - S(12), S(24));   // clear of the slider, whose knob sits in its 10 px padding
                slider.SetBounds(S(Pad) + lblW - S(10), cy, Inner - lblW - valW + S(6), S(24));
                value.SetBounds(S(Pad) + Inner - valW, cy, valW, S(24));
                cy += S(26);
            }
        }
        foreach (var sl in new[] { _boost, _boostCpu, _boostGpu }) sl.BringToFront();   // a neighbour must never cover the knob
        _modeCard.Height = cy + S(6);
        UpdateBoostLabels();
    }

    void ToggleSplit()
    {
        _c.S.FanExpert = !_c.S.FanExpert;
        _c.S.Save();
        if (!_c.S.FanExpert && _c.S.FanBoostCpu != _c.S.FanBoostGpu)
        {
            // back to one slider: all fans get the higher of the two boosts
            int v = _c.S.FanBoost;
            Task.Run(() => _c.SetBoost(v));
        }
        LayoutBoost();
        Reflow();
    }

    /// <summary>Re-stacks the cards after one of them changed height; the window keeps its bottom edge.</summary>
    void Reflow()
    {
        int y = 0;
        foreach (var (id, card) in _cards)
        {
            if (_c.S.HiddenCards.Contains(id)) continue;   // card.Visible is false for every card while the window is hidden
            card.Top = y;
            y += card.Height + S(Gap);
        }
        int bottom = Bottom;
        // more sections than the screen holds: the window stops at the screen height and the cards scroll
        int content = Math.Max(0, y - S(Gap));
        int maxView = Screen.FromControl(this).WorkingArea.Height - (Height - ClientSize.Height) - S(20) - _cardsTop - S(M);
        int view = Math.Max(S(60), Math.Min(content, maxView));
        _view.Content.Height = content;
        _view.SetBounds(0, _cardsTop, S(W), view);
        _view.Relayout();
        ClientSize = new Size(S(W), _cardsTop + view + S(M));
        var wa = Screen.FromControl(this).WorkingArea;
        if (Visible) Location = new Point(Math.Min(Left, wa.Right - Width), Math.Max(wa.Top, bottom - Height));
    }

    /// <summary>The controller changed the boost (a mode was switched): move the sliders.</summary>
    void OnBoostChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(SyncBoostSliders);
    }

    void SyncBoostSliders()
    {
        if (!_boost.Dragging) _boost.Value = _c.S.FanBoost;
        if (!_boostCpu.Dragging) _boostCpu.Value = _c.S.FanBoostCpu;
        if (!_boostGpu.Dragging) _boostGpu.Value = _c.S.FanBoostGpu;
        UpdateBoostLabels();
    }

    static string BoostText(int v) => v == 0 ? L.T("auto") : $"+{v}%";
    static string Iops(float v) => v >= 1000 ? $"{v / 1000:0.#}k" : $"{v:0}";

    void UpdateBoostLabels()
    {
        _boostValue.Text = BoostText(_boost.Value);
        _cpuValue.Text = BoostText(_boostCpu.Value);
        _gpuValue.Text = BoostText(_boostGpu.Value);
    }

    readonly Card _screenCard = new() { Caption = L.T("card.screen") };
    readonly Slider _screen = new();
    int _screenWanted = -1, _screenApplied = -1;
    bool _screenWorker;

    /// <summary>Built-in display brightness; follows the brightness keys live (WmiMonitorBrightnessEvent).</summary>
    int BuildScreen(int y)
    {
        int cy = S(Top + 2);
        _screen.SetBounds(S(Pad - 10), cy, Inner + S(20), S(24));
        int now = ScreenBrightness.Read();
        _screen.Value = Math.Max(0, now);
        _screenApplied = now;
        _screenCard.Hint = now >= 0 ? $"{now}%" : null;
        _screen.ValueChanged += (_, _) => { _screenCard.Hint = $"{_screen.Value}%"; _screenCard.Invalidate(); RequestScreen(_screen.Value); };
        _screen.ValueCommitted += (_, _) => RequestScreen(_screen.Value);
        _screenCard.Controls.Add(_screen);
        _c.Screen!.Changed += OnScreenChanged;
        cy += S(24) + S(6);
        _screenCard.SetBounds(S(M), y, CardW, cy);
        AddCard("screen", _screenCard);
        return _screenCard.Bottom + S(Gap);
    }

    /// <summary>Applies the latest requested level on a worker, skipping the in-between ones while dragging.</summary>
    void RequestScreen(int level)
    {
        _screenWanted = level;
        if (_screenWorker) return;
        _screenWorker = true;
        Task.Run(async () =>
        {
            while (_screenWanted != _screenApplied)
            {
                int v = _screenWanted;
                _c.Screen?.Set(v);
                _screenApplied = v;
                await Task.Delay(40);
            }
            _screenWorker = false;
        });
    }

    /// <summary>Brightness changed anywhere (keys, Windows, this app): mirror it unless the user is dragging.</summary>
    void OnScreenChanged(int level)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _screenApplied = level;
            if (_screen.Dragging) return;
            _screen.Value = level;
            _screenCard.Hint = $"{level}%";
            _screenCard.Invalidate();
        });
    }

    int BuildLights(int y)
    {
        var card = new Card { Caption = L.T("card.lights") };
        var onOff = new Toggle("") { On = _c.S.LightsEnabled };
        onOff.SetBounds(CardW - S(Pad) - S(44), S(6), S(44), S(26));
        onOff.Toggled += (_, _) => { bool on = onOff.On; Task.Run(() => { if (on) _c.LightsOn(); else _c.LightsOff(); }); };
        card.Controls.Add(onOff);
        if (_c.Keyboard != null)
        {
            var keysBtn = SmallButton(L.T("btn.keys"));
            keysBtn.Location = new Point(onOff.Left - S(8) - keysBtn.Width, S(7));
            keysBtn.Click += (_, _) => OpenKeys?.Invoke();
            card.Controls.Add(keysBtn);
        }

        int cy = S(Top + 4);
        var rows = new List<(string name, string color, int bright, Func<Color, int, bool> apply)>();
        if (_c.Keyboard != null) rows.Add((L.T("light.kb"), _c.S.KeyboardColor, _c.S.KeyboardBrightness, _c.SetKeyboard));
        if (_c.Chassis != null) rows.Add((L.T("light.body"), _c.S.ChassisColor, _c.S.ChassisBrightness, _c.SetChassis));

        foreach (var (name, color, bright, apply) in rows)
        {
            Color cur;
            try { cur = ColorTranslator.FromHtml(color); } catch { cur = Presets[0]; }
            card.Controls.Add(new Label { Text = name, ForeColor = Muted, Font = Theme.Font(9f), AutoSize = true, Location = new Point(S(Pad), cy + S(2)) });

            var slider = new Slider { Value = bright };
            var swatches = new List<Swatch>();
            var custom = new Swatch(cur) { IsCustom = true, Size = new Size(S(22), S(22)) };
            void Pick(Color col)
            {
                foreach (var s in swatches) s.Selected = !s.IsCustom && s.Color.ToArgb() == col.ToArgb();
                custom.Color = col;
                int br = slider.Value;
                onOff.On = true;
                Task.Run(() => apply(col, br));
            }
            custom.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { Color = custom.Color, FullOpen = true };
                if (dlg.ShowDialog(this) == DialogResult.OK) Pick(dlg.Color);
            };
            var all = Presets.Select(p => new Swatch(p) { Size = new Size(S(22), S(22)) }).ToList();
            foreach (var s in all) { var col = s.Color; s.Click += (_, _) => Pick(col); }
            all.Add(custom);
            swatches.AddRange(all);
            int sx = S(Pad) + Inner;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                sx -= all[i].Width + S(3);
                all[i].Location = new Point(sx + S(3), cy);
                card.Controls.Add(all[i]);
            }
            foreach (var s in swatches) s.Selected = !s.IsCustom && s.Color.ToArgb() == cur.ToArgb();

            var brLabel = new Label { Text = $"{bright}%", ForeColor = Fg, Font = Theme.Font(8.5f), AutoSize = false, TextAlign = ContentAlignment.MiddleRight };
            slider.SetBounds(S(Pad - 10), cy + S(23), Inner - S(30), S(24));
            brLabel.SetBounds(S(Pad) + Inner - S(44), cy + S(23), S(44), S(24));
            slider.ValueChanged += (_, _) => brLabel.Text = $"{slider.Value}%";
            slider.ValueCommitted += (_, _) =>
            {
                var col = swatches.FirstOrDefault(s => s.Selected)?.Color ?? custom.Color;
                int br = slider.Value;
                onOff.On = true;
                bool kb = name == L.T("light.kb");
                // a per-key scheme keeps its colors; only the brightness changes
                Task.Run(() => { if (kb && _c.S.KeyboardCustom) _c.SetKeyboardBrightness(br); else apply(col, br); });
            };
            card.Controls.Add(slider);
            card.Controls.Add(brLabel);
            cy += S(50);
        }

        card.SetBounds(S(M), y, CardW, cy + S(2));
        AddCard("lights", card);
        return card.Bottom + S(Gap);
    }

    string FanName(Awcc.Fan f)
    {
        // Fans are named after the temperature sensor that drives them (CPU / GPU).
        string group = Awcc.SensorNames.GetValueOrDefault(f.SensorId, "Fan");
        int n = _c.Awcc!.Fans.Where(x => x.SensorId == f.SensorId).ToList().IndexOf(f) + 1;
        return $"{group} {n}";
    }

    void OnSample()
    {
        if (IsDisposed) return;
        // every sparkline spans the same 80 s, whatever the refresh period
        int spark = SensorMonitor.Samples(80);
        foreach (var t in Descendants(this).OfType<StatTile>()) t.Capacity = spark;
        foreach (var f in _fans) f.Capacity = spark;
        // the slider mirrors the boost the controller set (e.g. 100% for Alien Mode), unless it is being dragged
        if (_c.Awcc != null && (_boost.Value != _c.S.FanBoost || _boostCpu.Value != _c.S.FanBoostCpu || _boostGpu.Value != _c.S.FanBoostGpu)) SyncBoostSliders();
        var snap = _mon.Last;
        int Last(List<float> l) => l.Count > 0 ? (int)l[^1] : -1;

        int cpu = Last(_mon.Cpu);
        _cpu.Value = cpu < 0 ? "—" : $"{cpu}°";
        _cpu.ValueColor = TempColor(cpu);
        _cpu.History = _mon.Cpu.Recent(80);
        _cpu.Lines.Clear();
        _cpu.Lines.Add((_mon.CpuPower.Recent(80), Warn, L.T("u.w")));
        _cpu.Lines.Add((_mon.PClock.Recent(80), Hex("#9B7BFF"), L.T("u.ghz")));
        var cpuSub = new List<string>();
        if (_mon.CpuLoad >= 0) cpuSub.Add($"{_mon.CpuLoad:0}%");
        if (_mon.PClockMhz > 0) cpuSub.Add($"{_mon.PClockMhz / 1000:0.0} {L.T("u.ghz")}");
        if (_mon.PClockMhz > 0 && _mon.EClockMhz > 0) cpuSub[^1] = $"{_mon.PClockMhz / 1000:0.0}/{_mon.EClockMhz / 1000:0.0} {L.T("u.ghz")}";
        if (_mon.CpuPowerW > 0) cpuSub.Add($"{_mon.CpuPowerW:0} {L.T("u.w")}");
        _cpu.Sub = string.Join(" · ", cpuSub);

        SyncGpuRows();
        var gpus = _mon.Gpus;
        for (int i = 0; i < _gpuChip.Rows.Count; i++)
        {
            var (main, mem) = _gpuChip.Rows[i];
            var gd = i < gpus.Count ? gpus[i] : null;
            var g = gd?.Stats;
            // the first GPU's temperature comes from the AWCC sensor (always polled); further ones from NVML
            var temps = i == 0 && _mon.Gpu.Count > 0 ? _mon.Gpu : gd?.Temps ?? new List<float>();
            int t = temps.Count > 0 ? (int)temps[^1] : -1;
            main.Title = gd?.Name is { Length: > 0 } n ? n : SystemInfo.GpuName.Length > 0 ? SystemInfo.GpuName : "GPU";
            main.Value = t < 0 ? "—" : $"{t}°";
            main.ValueColor = TempColor(t);
            main.History = temps.Recent(80);
            main.Lines.Clear();
            if (gd != null)
            {
                main.Lines.Add((gd.Power.Recent(80), Warn, L.T("u.w")));
                main.Lines.Add((gd.Clock.Recent(80), Hex("#9B7BFF"), L.T("u.ghz")));
            }
            var sub = new List<string>();
            if (g != null)
            {
                if (g.Load >= 0) sub.Add($"{g.Load}%");
                if (g.CoreMhz > 0) sub.Add($"{g.CoreMhz} {L.T("u.mhz")}");
                if (g.PowerW > 0) sub.Add($"{g.PowerW:0} {L.T("u.w")}");
            }
            main.Sub = string.Join(" · ", sub);
            mem.Title = gpus.Count > 1 ? $"VRAM {i + 1}" : "VRAM";
            if (g is { VramTotalGb: > 0 })
            {
                mem.Value = $"{g.VramUsedGb:0.0}/{g.VramTotalGb:0} {L.T("u.gb")}";
                mem.Sub = (g.MemTemp > 0 ? $"{g.MemTemp}° · " : "") + (g.MemMhz > 0 ? $"{g.MemMhz} {L.T("u.mhz")}" : "");
                mem.History = gd!.Vram.Recent(80);
                mem.HistoryMin = 0; mem.HistoryMax = g.VramTotalGb;
            }
            else mem.Value = "—";
            main.Invalidate();
            mem.Invalidate();
        }
        var (ru, rt) = SystemMemory.Read();
        if (rt > 0)
        {
            _ram.Value = $"{ru:0.0}/{rt:0} {L.T("u.gb")}";
            _ram.Sub = $"{ru / rt * 100:0}%";
            _ram.History = _mon.RamUsed.Recent(80);
            _ram.HistoryMin = 0; _ram.HistoryMax = rt;
        }
        UpdateDrives();
        UpdateLinks();
        UpdatePower();
        foreach (var t in new[] { _cpu, _ram }) t.Invalidate();

        if (snap == null) return;
        foreach (var bar in _fans)
        {
            int i = (int)bar.Tag!;
            if (i >= snap.Fans.Count) continue;
            bar.Rpm = snap.Fans[i].rpm;
            bar.History = i < _mon.Fans.Count ? _mon.Fans[i].Recent(80) : Array.Empty<float>();
            bar.Invalidate();
        }
        foreach (var b in _profiles) b.Active = !_c.S.FanCurve && (byte)b.Tag! == snap.Profile;
        _customMode.Active = _c.S.FanCurve;
        LockSliders();
        _modeCard.Hint = snap.Profile >= 0 || _c.S.FanCurve ? L.T("mode.now", _c.CurrentModeName(snap.Profile)) : null;
        _modeCard.Invalidate();
    }
}
