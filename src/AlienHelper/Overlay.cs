using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>
/// Small always-on-top, click-through stats panel for games. Shown automatically while a game is in the foreground
/// (<see cref="GameDetector"/>), or manually with Ctrl+Shift+O.
/// Exclusive-fullscreen D3D games draw over every window, so the overlay is only visible in borderless mode.
/// While visible it can be moved with Ctrl+Shift+Alt+arrows (those hotkeys exist only while it is shown).
/// </summary>
public sealed class OverlayManager : IDisposable
{

    public const int StyleCount = 4;

    readonly Controller _c;
    readonly SensorMonitor _mon;
    readonly FrameMonitor _frames = new();
    readonly System.Windows.Forms.Timer _detect = new() { Interval = 2000 };
    readonly List<HotKey> _moveKeys = new();
    OverlayForm? _form;
    bool _manual;        // user forced it on with the hotkey / menu
    bool _auto;          // currently shown because a game is detected
    readonly List<OverlayItem> _items = new();
    readonly HashSet<object> _hideFromCapture = new();

    public bool Visible => _form is { Visible: true };
    public Rectangle Bounds => Visible ? _form!.Bounds : Rectangle.Empty;
    public event Action? Changed;

    /// <summary>Items plugins added, shown after the app's own rows.</summary>
    public IReadOnlyList<OverlayItem> Items => _items;
    public void AddItem(OverlayItem item) { _items.Add(item); ItemsChanged(); }
    public void RemoveItem(OverlayItem item) { if (_items.Remove(item)) ItemsChanged(); }
    /// <summary>An item changed: the overlay measures and paints again.</summary>
    public void ItemsChanged() => _form?.ItemsChanged();

    /// <summary>Kept out of screenshots and recordings while any owner (a plugin) asks for it.</summary>
    public void HideFromCapture(object owner, bool hide)
    {
        if (hide ? !_hideFromCapture.Add(owner) : !_hideFromCapture.Remove(owner)) return;
        _form?.SetCaptureHidden(HiddenFromCapture);
    }
    public bool HiddenFromCapture => _hideFromCapture.Count > 0;

    public OverlayManager(Controller c, SensorMonitor mon)
    {
        _c = c; _mon = mon;
        GameDetector.Init(c.S);
        _detect.Tick += (_, _) => Detect();
        _detect.Start();
    }

    public bool Auto
    {
        get => _c.S.OverlayAuto;
        set { _c.S.OverlayAuto = value; _c.S.Save(); if (!value && _auto) { _auto = false; Apply(); } Changed?.Invoke(); }
    }

    public int Style
    {
        get => Math.Clamp(_c.S.OverlayStyle, 0, StyleCount - 1);
        set
        {
            _c.S.OverlayStyle = Math.Clamp(value, 0, StyleCount - 1); _c.S.Save();
            if (_form != null) _form.Style = Style;
            Changed?.Invoke();
        }
    }

    public void Toggle() { _manual = !Visible; if (!_manual) _auto = false; Apply(); }

    /// <summary>Display mode: 0 off, 1 always on, 2 only while a game is in front.</summary>
    public int Mode
    {
        get => _manual ? 1 : Auto ? 2 : 0;
        set
        {
            _manual = value == 1;
            _c.S.OverlayAuto = value == 2;
            _c.S.Save();
            _auto = value == 2 && GameDetector.InFront();
            Apply();
            Changed?.Invoke();
        }
    }

    void Detect()
    {
        bool game = GameDetector.InFront() && Auto;   // asked even when off: the tray menu offers the app in front
        if (game != _auto) { _auto = game; Apply(); }
        else if (Visible) _form!.KeepOnTop();
    }

    void Apply()
    {
        bool show = _manual || _auto;
        if (show && !Visible)
        {
            _form ??= new OverlayForm(_c, _mon, _frames, this) { Style = Style };
            _frames.Start();
            _form.Reposition();
            _form.ShowNoActivate();
            _form.SetCaptureHidden(HiddenFromCapture);
            _mon.Want(this, Need.Mode | Need.Fans | Need.Cpu | Need.Gpu | Need.Power);
            RegisterMoveKeys();
        }
        else if (!show && Visible)
        {
            _form!.Hide();
            _frames.Stop();
            _mon.Want(this, Need.None);
            UnregisterMoveKeys();
        }
        Changed?.Invoke();
    }

    void RegisterMoveKeys()
    {
        if (_moveKeys.Count > 0) return;
        int step = S(16);
        const uint mods = HotKey.Ctrl | HotKey.Shift | HotKey.Alt;   // auto-repeat on: hold to keep moving
        _moveKeys.Add(new HotKey(mods, Keys.Left, () => _form?.Nudge(-step, 0)));
        _moveKeys.Add(new HotKey(mods, Keys.Right, () => _form?.Nudge(step, 0)));
        _moveKeys.Add(new HotKey(mods, Keys.Up, () => _form?.Nudge(0, -step)));
        _moveKeys.Add(new HotKey(mods, Keys.Down, () => _form?.Nudge(0, step)));
    }

    void UnregisterMoveKeys()
    {
        foreach (var k in _moveKeys) k.Dispose();
        _moveKeys.Clear();
    }

    public void SetCorner(int corner)
    {
        _c.S.OverlayCorner = corner;
        _c.S.OverlayX = _c.S.OverlayY = null;   // a corner replaces a hand-placed position
        _c.S.Save();
        _form?.Reposition();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        UnregisterMoveKeys();
        _detect.Dispose();
        _frames.Dispose();
        _form?.Dispose();
    }
}

public enum OvIcon { None, Fps, Frame, Cpu, Gpu, Power, Vram, Ram, Fan, Mode, Plugin }

/// <summary>A plugin's item in the overlay. Changed on the UI thread only; <see cref="OverlayManager.ItemsChanged"/> redraws.</summary>
public sealed class OverlayItem
{
    public string Label = "", Value = "", Extra = "";
    public string? Compact;
    public Color? Color;
    public bool Visible = true, Essential = true;
    public List<float>? History;   // null: no graph
    public float Min, Max = 100;
    public Action<Graphics, RectangleF, Color>? DrawIcon;
    /// <summary>DrawIcon threw; the plugin is stopped (which removes the item).</summary>
    public Action<Exception>? Failed;
}

/// <summary>
/// The overlay window. Styles: 0 full (rows with graphs), 1 minimal column, 2 horizontal bar with graphs,
/// 3 minimal horizontal strip with drawn icons instead of words.
/// </summary>
public sealed class OverlayForm : Form
{
    static int Cap => SensorMonitor.Samples(120);   // points per sensor sparkline: the last 2 min
    const int FrameCap = 180;      // frame-time samples shown

    sealed class Row
    {
        public string Label = "", Value = "", Extra = "", Compact = "";
        public string? StripText;      // icon strip text when the icon alone is ambiguous (two power bolts)
        public OvIcon Icon;
        public Color Color = Fg;
        public List<float>? History;
        public float Min, Max;
        public bool Bars;          // frame-time style graph
        public bool Big;
        public bool Essential = true;   // shown in the minimal styles
        public OverlayItem? Item;       // a plugin's item
        // the widest text this row can show (all digits at their widest), so items keep one width from the start
        public string Wide = "", WideExtra = "";
    }

    readonly Controller _c;
    readonly SensorMonitor _mon;
    readonly FrameMonitor _frames;
    readonly OverlayManager? _owner;   // its plugin items; none for the screenshot previews
    readonly System.Windows.Forms.Timer _fast = new() { Interval = 500 };

    readonly List<float> _fps = new(), _cpu = new(), _gpu = new(), _cpuPwr = new(), _gpuPwr = new(), _allPwr = new(), _vram = new(), _ram = new();
    List<float> _frameTimes = new();
    FrameMonitor.Stats? _stats;
    readonly List<Row> _rows = new();
    static readonly List<float> NoData = new();   // an empty graph for the frame rate rows outside games
    // widths only grow while a style is shown, so changing digits don't make items jump
    readonly Dictionary<string, int> _widths = new();
    // the last good value of each reading: a missed driver read keeps showing it instead of a dash or a missing row
    readonly Dictionary<string, float> _last = new();
    string _proc = "";
    bool _game;   // a full-screen game is in front: frame rate rows and the process name are shown
    long _gameUntil;   // the frame rate rows stay this long after the last game check passed (no blinking)

    // fonts are created once per font family (a theme file may name its own); the overlay repaints twice a second
    static string? _fontsFor;
    static Font _header = null!, _label = null!, _value = null!, _big = null!, _extra = null!, _barValue = null!, _barBig = null!;
    static void Fonts()
    {
        if (_fontsFor == Family) return;
        (_header, _label, _value, _big, _extra, _barValue, _barBig) =
            (Theme.Font(8f), Semibold(8f), Semibold(10f), Semibold(14f), Theme.Font(8.5f), Semibold(11f), Semibold(13f));
        _fontsFor = Family;
    }
    static Font HeaderFont { get { Fonts(); return _header; } }
    static Font LabelFont { get { Fonts(); return _label; } }
    static Font ValueFont { get { Fonts(); return _value; } }
    static Font BigFont { get { Fonts(); return _big; } }
    static Font ExtraFont { get { Fonts(); return _extra; } }
    static Font BarValueFont { get { Fonts(); return _barValue; } }
    static Font BarBigFont { get { Fonts(); return _barBig; } }

    /// <summary>Full style: where the values start, past the widest label (wider fonts push it right).</summary>
    int FullValueX() => Stable("full.label", Math.Max(S(58), S(12) + _rows.Select(r => TextRenderer.MeasureText(r.Label, LabelFont).Width).DefaultIfEmpty(0).Max() + S(6)));

    int _style;
    public int Style
    {
        get => _style;
        set { _style = value; _widths.Clear(); Rebuild(); Reposition(); }
    }

    public OverlayForm(Controller c, SensorMonitor mon, FrameMonitor frames, OverlayManager? owner = null)
    {
        _c = c; _mon = mon; _frames = frames; _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(10, 12, 16);
        Opacity = 0.88;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(S(270), S(300));
        _mon.Updated += OnSensors;
        _fast.Tick += (_, _) => OnFrames();
        VisibleChanged += (_, _) => { if (Visible) _fast.Start(); else _fast.Stop(); };
        FormClosed += (_, _) => { _mon.Updated -= OnSensors; _fast.Dispose(); };
        OnSensors();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // layered + transparent (click-through) + toolwindow (no alt-tab) + noactivate + topmost
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;
            return cp;
        }
    }

    public void ShowNoActivate()
    {
        if (!IsHandleCreated) CreateHandle();
        ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
        Visible = true;
        KeepOnTop();
    }

    /// <summary>Plugin items changed.</summary>
    public void ItemsChanged() { if (!IsDisposed) Rebuild(); }

    /// <summary>Kept out of screenshots and screen recordings, still shown on screen (Windows 10 2004 and later).</summary>
    public void SetCaptureHidden(bool hide)
    {
        if (IsHandleCreated) SetWindowDisplayAffinity(Handle, hide ? 0x11u : 0u);   // WDA_EXCLUDEFROMCAPTURE / WDA_NONE
    }

    public void KeepOnTop()
    {
        using var timed = Hitches.Time("overlay keep on top");
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
    }

    /// <summary>Hand-placed position if the user moved it, otherwise the chosen corner.</summary>
    public void Reposition()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        if (_c.S.OverlayX is int x && _c.S.OverlayY is int y)
        {
            Location = new Point(Math.Clamp(x, b.Left, b.Right - Width), Math.Clamp(y, b.Top, b.Bottom - Height));
            return;
        }
        int m = S(12);
        Location = _c.S.OverlayCorner switch
        {
            1 => new Point(b.Right - Width - m, b.Top + m),
            2 => new Point(b.Left + m, b.Bottom - Height - m),
            3 => new Point(b.Right - Width - m, b.Bottom - Height - m),
            _ => new Point(b.Left + m, b.Top + m),
        };
    }

    public void Nudge(int dx, int dy)
    {
        var b = Screen.PrimaryScreen!.Bounds;
        var p = new Point(Math.Clamp(Left + dx, b.Left, b.Right - Width), Math.Clamp(Top + dy, b.Top, b.Bottom - Height));
        Location = p;
        _c.S.OverlayX = p.X; _c.S.OverlayY = p.Y;
        _c.S.Save();
    }

    static void Push(List<float> l, float v, int cap = 0) { if (cap == 0) cap = Cap; l.Add(v); while (l.Count > cap) l.RemoveAt(0); }

    void OnFrames()
    {
        _stats = _frames.Read();
        if (_game) Push(_fps, (float)(_stats?.Fps ?? 0), 120);
        _frameTimes = _stats?.FrameTimes.ToList() ?? new List<float>();
        _proc = _stats?.Process ?? (_frames.Error != null ? "ETW: " + _frames.Error : L.T("ov.noframes"));
        Rebuild();
    }

    // the whole laptop as measured: PawnIO's platform counter, or the battery. The CPU + GPU estimate is not shown here:
    // both parts already have their own rows.
    bool TotalMeasured => _mon.PowerTotalW > 0 && _mon.PowerFrom is SensorMonitor.PowerSource.Platform or SensorMonitor.PowerSource.Battery;

    void OnSensors()
    {
        int Last(List<float> l) => l.Count > 0 ? (int)l[^1] : -1;
        int cpu = Last(_mon.Cpu), gpu = Last(_mon.Gpu);
        if (cpu > 0) Push(_cpu, cpu);
        if (gpu > 0) Push(_gpu, gpu);
        var g = _mon.GpuInfo;
        if (g is { PowerW: > 0 }) Push(_gpuPwr, g.PowerW);
        if (_mon.CpuPowerW > 0) Push(_cpuPwr, _mon.CpuPowerW);
        if (TotalMeasured) Push(_allPwr, _mon.PowerTotalW);
        if (g is { VramUsedGb: > 0 }) Push(_vram, g.VramUsedGb);
        var (ru, _) = SystemMemory.Read();
        if (ru > 0) Push(_ram, ru);
        Rebuild();
    }

    void Rebuild()
    {
        using var dark = Theme.UseOverlay();   // row colors come from the theme; over games it stays dark unless the theme covers it
        if (_widthsFor != Family) { _widths.Clear(); _widthsFor = Family; }   // another font: measure again
        _rows.Clear();
        var s = _stats;
        var g = _mon.GpuInfo;
        var snap = _mon.Last;
        string W = L.T("u.w"), GB = L.T("u.gb"), MS = L.T("u.ms");

        // the rows stay in place so nothing moves when a game starts, but their values belong to games only: browsers,
        // players and apps such as Electron ones present frames too, so numbers show only while a game is in front
        bool game = s != null && !GameDetector.NotGames.Contains(s.Process) && GameDetector.InFront();
        if (game) _gameUntil = Environment.TickCount64 + 5000;
        else game = Environment.TickCount64 < _gameUntil;
        _game = game;
        float fpsMax = Math.Max(60, (_fps.Count > 0 ? _fps.Max() : 60) * 1.15f);
        string fps = !game || s == null ? "—" : $"{s.Fps:0}";
        _rows.Add(new Row
        {
            Label = "FPS", Icon = OvIcon.Fps, Value = fps, Compact = fps, Big = true,
            Extra = game && s is { Low1Fps: > 0 } ? $"1% low {s.Low1Fps:0}" : "",
            Color = Good, History = game ? _fps : NoData, Min = 0, Max = fpsMax, Wide = "888", WideExtra = "1% low 888",
        });
        float ftMax = _frameTimes.Count > 0 ? Math.Max(20, _frameTimes.OrderBy(x => x).ElementAt((int)(_frameTimes.Count * 0.99)) * 1.5f) : 33;
        string frame = game && s is { FrameMs: > 0 } ? $"{s.FrameMs:0.0} {MS}" : "—";
        _rows.Add(new Row
        {
            Label = L.T("ov.frame"), Icon = OvIcon.Frame, Value = frame, Compact = frame,
            Extra = game && _frameTimes.Count > 0 ? L.T("ov.max", $"{_frameTimes.Max():0.0} {MS}") : "",
            Color = Accent, History = game ? _frameTimes : NoData, Min = 0, Max = ftMax, Bars = true,
            Wide = $"888.8 {MS}", WideExtra = L.T("ov.max", $"888.8 {MS}"),
        });

        // a missed driver read shows the last good value instead of blinking to a dash
        float cpuLoad = Keep("cpu.load", _mon.CpuLoad, _mon.CpuLoad >= 0);
        float pclock = Keep("cpu.clock", _mon.PClockMhz, _mon.PClockMhz > 0);
        int cpu = _cpu.Count > 0 ? (int)_cpu[^1] : -1;
        var cpuExtra = new List<string>();
        if (cpuLoad >= 0) cpuExtra.Add($"{cpuLoad:0}%");
        if (pclock > 0) cpuExtra.Add($"{pclock / 1000:0.0} {L.T("u.ghz")}");
        string cpuT = cpu < 0 ? "—" : $"{cpu}°";
        _rows.Add(new Row
        {
            Label = "CPU", Icon = OvIcon.Cpu, Value = cpuT, Extra = string.Join("  ", cpuExtra),
            Compact = cpuLoad >= 0 ? $"{cpuT} {cpuLoad:0}%" : cpuT,
            Color = TempColor(cpu), History = _cpu, Min = 30, Max = 100,
            Wide = "100° 100%", WideExtra = $"100%  8.8 {L.T("u.ghz")}",
        });

        int gpu = _gpu.Count > 0 ? (int)_gpu[^1] : -1;
        int gpuLoad = (int)Keep("gpu.load", g?.Load ?? -1, g is { Load: >= 0 });
        int gpuMhz = (int)Keep("gpu.mhz", g?.CoreMhz ?? -1, g is { CoreMhz: > 0 });
        var gpuExtra = new List<string>();
        if (gpuLoad >= 0) gpuExtra.Add($"{gpuLoad}%");
        if (gpuMhz > 0) gpuExtra.Add($"{gpuMhz} {L.T("u.mhz")}");
        string gpuT = gpu < 0 ? "—" : $"{gpu}°";
        _rows.Add(new Row
        {
            Label = "GPU", Icon = OvIcon.Gpu, Value = gpuT, Extra = string.Join("  ", gpuExtra),
            Compact = gpuLoad >= 0 ? $"{gpuT} {gpuLoad}%" : gpuT,
            Color = TempColor(gpu), History = _gpu, Min = 30, Max = 100,
            Wide = "100° 100%", WideExtra = $"100%  8888 {L.T("u.mhz")}",
        });

        float cpuW = Keep("cpu.w", _mon.CpuPowerW, _mon.CpuPowerW > 0);
        if (cpuW > 0)
        {
            string v = $"{cpuW:0} {W}";
            _rows.Add(new Row { Label = "CPU " + W, Icon = OvIcon.Power, Value = v, Compact = v, StripText = "CPU " + v, Wide = $"CPU 888 {W}", Color = Warn, History = _cpuPwr, Min = 0, Max = Math.Max(60, _cpuPwr.DefaultIfEmpty(0).Max() * 1.1f) });
        }
        float gpuW = Keep("gpu.w", g?.PowerW ?? -1, g is { PowerW: > 0 });
        if (gpuW > 0)
        {
            string v = $"{gpuW:0} {W}";
            _rows.Add(new Row { Label = "GPU " + W, Icon = OvIcon.Power, Value = v, Compact = v, StripText = "GPU " + v, Wide = $"GPU 888 {W}", Color = Data("#FFD27A"), History = _gpuPwr, Min = 0, Max = Math.Max(60, _gpuPwr.DefaultIfEmpty(0).Max() * 1.1f) });
        }
        float allW = Keep("all.w", _mon.PowerTotalW, TotalMeasured);
        if (allW > 0)
        {
            string name = L.T("ov.total"), v = $"{allW:0} {W}";
            _rows.Add(new Row { Label = name + " " + W, Icon = OvIcon.Power, Value = v, Compact = v, StripText = name + " " + v, Wide = $"{name} 888 {W}", Color = Warm, History = _allPwr, Min = 0, Max = Math.Max(100, _allPwr.DefaultIfEmpty(0).Max() * 1.1f) });
        }
        // NVML misses the memory info now and then (e.g. while the GPU wakes up)
        float vramTotal = Keep("vram.total", g?.VramTotalGb ?? -1, g is { VramTotalGb: > 0 });
        if (vramTotal > 0)
        {
            float used = Keep("vram.used", g?.VramUsedGb ?? -1, g is { VramTotalGb: > 0, VramUsedGb: >= 0 });
            int memTemp = (int)Keep("vram.temp", g?.MemTemp ?? -1, g is { MemTemp: > 0 });
            string u = used >= 0 ? $"{used:0.0}" : "—";
            _rows.Add(new Row { Label = "VRAM", Icon = OvIcon.Vram, Value = $"{u} / {vramTotal:0} {GB}" + (memTemp > 0 ? $" · {memTemp}°" : ""), Compact = used >= 0 ? $"{u} {GB}" : "—", Wide = $"88.8 / {vramTotal:0} {GB} · 88°", Color = Extra, History = _vram, Min = 0, Max = vramTotal });
        }
        var (ru, rt) = SystemMemory.Read();
        float ramTotal = Keep("ram.total", rt, rt > 0), ramUsed = Keep("ram.used", ru, rt > 0 && ru > 0);
        if (ramTotal > 0)
        {
            string u = ramUsed > 0 ? $"{ramUsed:0.0}" : "—";
            _rows.Add(new Row { Label = "RAM", Icon = OvIcon.Ram, Value = $"{u} / {ramTotal:0} {GB}", Compact = ramUsed > 0 ? $"{u} {GB}" : "—", Wide = $"888.8 / {ramTotal:0} {GB}", Color = Data("#62D2FF"), History = _ram, Min = 0, Max = ramTotal });
        }
        if (snap != null)
        {
            var rpms = snap.Fans.Select((f, i) => (int)Keep("fan." + i, f.rpm, f.rpm >= 0)).ToList();
            int maxRpm = rpms.DefaultIfEmpty(-1).Max();
            _rows.Add(new Row
            {
                Label = "FAN", Icon = OvIcon.Fan,
                Value = string.Join("  ", rpms.Select(rpm => rpm < 0 ? "—" : $"{rpm / 100 / 10.0:0.0}{L.T("u.k")}")),
                Compact = maxRpm < 0 ? "—" : $"{maxRpm / 100 / 10.0:0.0}{L.T("u.k")}",
                Wide = string.Join("  ", snap.Fans.Select(_ => $"88.8{L.T("u.k")}")),
            });
            var mode = _c.CurrentModeName(snap.Profile);
            _rows.Add(new Row
            {
                Label = "MODE", Icon = OvIcon.Mode, Value = mode, Compact = mode, Wide = WidestMode(),
                Color = _c.S.FanCurve ? Extra : snap.Profile == Hardware.Awcc.ProfileGMode ? Warm : Accent,
            });
        }
        foreach (var it in _owner?.Items ?? Array.Empty<OverlayItem>())
        {
            if (!it.Visible) continue;
            string compact = it.Compact ?? it.Value;
            _rows.Add(new Row
            {
                Label = it.Label, Icon = OvIcon.Plugin, Item = it, Value = it.Value, Compact = compact, Extra = it.Extra,
                StripText = it.DrawIcon == null ? $"{it.Label} {compact}" : null,
                Color = it.Color ?? Fg, History = it.History, Min = it.Min, Max = it.Max, Essential = it.Essential,
            });
        }

        var size = _style switch
        {
            1 => MeasureMiniColumn(),
            2 => MeasureBar(),
            3 => MeasureIconStrip(),
            _ => new Size(S(270) + FullValueX() - S(58), S(30) + _rows.Sum(RowHeight) + S(6)),
        };
        if (Size != size)
        {
            Size = size;
            Reposition();
        }
        Invalidate();
    }

    float Keep(string key, float value, bool ok)
    {
        if (ok) { _last[key] = value; return value; }
        return _last.TryGetValue(key, out float v) ? v : -1;
    }

    /// <summary>The longest mode name, so the row keeps one width whichever mode is on.</summary>
    string WidestMode() => (_c.Awcc?.Profiles.Select(p => _c.ProfileName(p)) ?? Array.Empty<string>()).Append("Custom")
        .OrderByDescending(n => TextRenderer.MeasureText(n, ValueFont).Width).First();

    IEnumerable<Row> ShownRows => _style is 1 or 3 ? _rows.Where(r => r.Essential) : _rows;

    int RowHeight(Row r) => r.History != null ? S(r.Big ? 46 : 40) : S(21);

    // ---------------- measuring ----------------

    /// <summary>The widest width seen for this key since the style was picked.</summary>
    string? _widthsFor;
    int Stable(string key, int w)
    {
        if (_widths.TryGetValue(key, out int old) && old >= w) return old;
        _widths[key] = w;
        return w;
    }

    int MiniLabelWidth() => Stable("mini.label", ShownRows.Max(r => TextRenderer.MeasureText(r.Label, LabelFont).Width));

    Size MeasureMiniColumn()
    {
        int valueW = Stable("mini.value", ShownRows.Max(r => Widest(ValueFont, r.Compact, ShortWide(r))));
        return new Size(S(12) + MiniLabelWidth() + S(10) + valueW + S(12), S(8) + ShownRows.Count() * S(20) + S(6));
    }

    int SegmentWidth(Row r)
    {
        int w = Math.Max(TextRenderer.MeasureText(r.Label, LabelFont).Width, Widest(r.Big ? BarBigFont : BarValueFont, r.Value, r.Icon == OvIcon.Power ? ShortWide(r) : r.Wide));
        w = Math.Max(w, Widest(HeaderFont, r.Extra, r.WideExtra));
        return Stable("bar." + r.Label, Math.Max(S(64), w + S(4)));
    }

    Size MeasureBar() => new(S(10) + _rows.Sum(r => SegmentWidth(r) + S(12)) + S(2), S(86));

    /// <summary>Plugin items without an icon show their label in the strip instead.</summary>
    static bool HasIcon(Row r) => r.Icon != OvIcon.Plugin || r.Item?.DrawIcon != null;

    int StripItemWidth(Row r) => Stable("strip." + r.Label, (HasIcon(r) ? S(18) + S(4) : 0) + Widest(ValueFont, r.StripText ?? r.Compact, r.StripText != null ? r.Wide : ShortWide(r)));

    /// <summary>The reserve for the compact value: "88.8 GB" without the "/ 32 GB" part, one fan, no source name.</summary>
    static string ShortWide(Row r) => r.Icon switch
    {
        OvIcon.Vram or OvIcon.Ram => r.Wide.Split(" / ")[0] + " " + L.T("u.gb"),
        OvIcon.Fan => r.Wide.Split("  ")[0],
        OvIcon.Power => r.Wide[(r.Wide.IndexOf(' ') + 1)..],   // "CPU 888 W" without the name
        _ => r.Wide,
    };

    static int Widest(Font f, string text, string wide) =>
        Math.Max(text.Length > 0 ? TextRenderer.MeasureText(text, f).Width : 0, wide.Length > 0 ? TextRenderer.MeasureText(wide, f).Width : 0);

    Size MeasureIconStrip() => new(S(10) + ShownRows.Sum(r => StripItemWidth(r) + S(12)) - S(12) + S(10), S(30));

    // ---------------- painting ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        using var timed = Hitches.Time("overlay paint");
        using var dark = Theme.UseOverlay();   // over games the overlay stays dark unless the theme covers it
        if (BackColor != OverlayBack) BackColor = OverlayBack;
        var g = e.Graphics;
        Smooth(g);
        switch (_style)
        {
            case 1: PaintMiniColumn(g); break;
            case 2: PaintBar(g); break;
            case 3: PaintIconStrip(g); break;
            default: PaintFull(g); break;
        }
    }

    void PaintFull(Graphics g)
    {
        if (_game) TextRenderer.DrawText(g, _proc, HeaderFont, new Rectangle(S(12), S(8), Width - S(24), S(16)), Muted, TextFormatFlags.EndEllipsis);
        int y = S(28);
        int left = S(12), right = Width - S(12), valueX = FullValueX();
        foreach (var r in _rows)
        {
            var font = r.Big ? BigFont : ValueFont;
            int vy = r.Big ? y - S(4) : y;
            TextRenderer.DrawText(g, r.Label, LabelFont, new Point(left, y + S(2)), Muted);
            TextRenderer.DrawText(g, r.Value, font, new Point(valueX, vy), r.Color);
            if (r.Extra.Length > 0)
            {
                var sz = TextRenderer.MeasureText(r.Extra, ExtraFont);
                TextRenderer.DrawText(g, r.Extra, ExtraFont, new Point(right - sz.Width, y + S(2)), Blend(Muted, Fg, 0.4f));
            }
            if (r.History != null)
            {
                int gy = y + S(r.Big ? 26 : 20), gh = S(14);
                DrawGraph(g, r, new RectangleF(left, gy, right - left, gh));
            }
            y += RowHeight(r);
        }
    }

    void PaintMiniColumn(Graphics g)
    {
        int labelW = MiniLabelWidth();
        int y = S(8);
        foreach (var r in ShownRows)
        {
            TextRenderer.DrawText(g, r.Label, LabelFont, new Point(S(12), y + S(2)), Muted);
            TextRenderer.DrawText(g, r.Compact, ValueFont, new Point(S(12) + labelW + S(10), y), r.Color);
            y += S(20);
        }
    }

    void PaintBar(Graphics g)
    {
        int x = S(10);
        using var sep = new Pen(Color.FromArgb(40, Fg));
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            int w = SegmentWidth(r);
            TextRenderer.DrawText(g, r.Label, LabelFont, new Point(x, S(7)), Muted);
            var vf = r.Big ? BarBigFont : BarValueFont;
            TextRenderer.DrawText(g, r.Value, vf, new Point(x - S(2), r.Big ? S(19) : S(22)), r.Color);
            if (r.Extra.Length > 0)
                TextRenderer.DrawText(g, r.Extra, HeaderFont, new Point(x, S(45)), Blend(Muted, Fg, 0.4f));
            if (r.History != null)
                DrawGraph(g, r, new RectangleF(x, S(64), w, S(13)));
            x += w + S(12);
            if (i < _rows.Count - 1) g.DrawLine(sep, x - S(6), S(10), x - S(6), Height - S(10));
        }
    }

    void PaintIconStrip(Graphics g)
    {
        int x = S(10), cy = Height / 2;
        foreach (var r in ShownRows)
        {
            var icon = new RectangleF(x, cy - S(9), S(18), S(18));
            if (r.Item is { DrawIcon: { } draw } item) PluginIcon(g, item, draw, icon, r.Color);
            else OverlayIcons.Draw(g, r.Icon, icon, r.Icon == OvIcon.Fan ? Muted : r.Color);
            var text = r.StripText ?? r.Compact;
            var tsz = TextRenderer.MeasureText(text, ValueFont);
            TextRenderer.DrawText(g, text, ValueFont, new Point(x + (HasIcon(r) ? S(22) : 0), cy - tsz.Height / 2), r.Color);
            x += StripItemWidth(r) + S(12);
        }
    }

    /// <summary>A plugin draws its own icon; if that throws, the plugin is stopped once painting is over.</summary>
    void PluginIcon(Graphics g, OverlayItem item, Action<Graphics, RectangleF, Color> draw, RectangleF rect, Color color)
    {
        var state = g.Save();
        try { draw(g, rect, color); }
        catch (Exception ex)
        {
            item.DrawIcon = null;   // no second try before the plugin is gone
            BeginInvoke(() => item.Failed?.Invoke(ex));
        }
        finally { g.Restore(state); }
    }

    static void DrawGraph(Graphics g, Row r, RectangleF rect)
    {
        using (var bg = Round(rect, Sf(3)))
        using (var b = new SolidBrush(Color.FromArgb(28, Fg))) g.FillPath(b, bg);
        var h = r.History!;
        if (h.Count < 2 || r.Max <= r.Min) return;
        int cap = r.Bars ? FrameCap : Math.Max(Cap, h.Count);
        float Y(float v) => rect.Bottom - rect.Height * (Math.Clamp(v, r.Min, r.Max) - r.Min) / (r.Max - r.Min);

        if (r.Bars)
        {
            // frame times as thin bars; spikes (>2x median) highlighted
            var sorted = h.OrderBy(x => x).ToList();
            float median = sorted[sorted.Count / 2];
            float bw = rect.Width / cap;
            using var ok = new SolidBrush(Color.FromArgb(200, r.Color));
            using var spike = new SolidBrush(Warm);
            for (int i = 0; i < h.Count; i++)
            {
                float x = rect.Right - bw * (h.Count - i);
                float top = Y(h[i]);
                g.FillRectangle(h[i] > median * 2 ? spike : ok, x, top, Math.Max(1, bw - 0.3f), rect.Bottom - top);
            }
            return;
        }

        var pts = LineChart.Points(h, rect.Right, rect.Width / (cap - 1), rect.Width, 0, Y);   // averaged to ~1 point per pixel
        using var area = new GraphicsPath();
        area.AddLines(pts);
        area.AddLine(pts[^1], new PointF(pts[^1].X, rect.Bottom));
        area.AddLine(new PointF(pts[^1].X, rect.Bottom), new PointF(pts[0].X, rect.Bottom));
        area.CloseFigure();
        using (var b = new SolidBrush(Color.FromArgb(50, r.Color))) g.FillPath(b, area);
        using var pen = new Pen(r.Color, Sf(1.3f)) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var p = Round(new RectangleF(0, 0, Width, Height), Sf(_style == 3 ? 8 : 10));
        Region = new Region(p);
    }

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

/// <summary>Small vector icons for the icon strip (no image assets, crisp at any DPI).</summary>
public static class OverlayIcons
{
    public static void Draw(Graphics g, OvIcon icon, RectangleF r, Color c)
    {
        float s = r.Width, w = Math.Max(1.2f, s * 0.11f);
        using var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(c);
        PointF P(float x, float y) => new(r.X + x * s, r.Y + y * s);
        switch (icon)
        {
            case OvIcon.Fps:   // speedometer
                g.DrawArc(pen, r.X + s * 0.1f, r.Y + s * 0.18f, s * 0.8f, s * 0.8f, 180, 180);
                g.DrawLine(pen, P(0.5f, 0.58f), P(0.75f, 0.33f));
                g.FillEllipse(brush, r.X + s * 0.42f, r.Y + s * 0.5f, s * 0.16f, s * 0.16f);
                break;
            case OvIcon.Frame: // stopwatch
                g.DrawEllipse(pen, r.X + s * 0.14f, r.Y + s * 0.2f, s * 0.72f, s * 0.72f);
                g.DrawLine(pen, P(0.5f, 0.56f), P(0.5f, 0.36f));
                g.DrawLine(pen, P(0.5f, 0.56f), P(0.64f, 0.64f));
                g.DrawLine(pen, P(0.42f, 0.08f), P(0.58f, 0.08f));
                break;
            case OvIcon.Cpu:   // chip with pins
                g.DrawRectangle(pen, r.X + s * 0.24f, r.Y + s * 0.24f, s * 0.52f, s * 0.52f);
                g.FillRectangle(brush, r.X + s * 0.38f, r.Y + s * 0.38f, s * 0.24f, s * 0.24f);
                foreach (float t in new[] { 0.36f, 0.5f, 0.64f })
                {
                    g.DrawLine(pen, P(t, 0.06f), P(t, 0.2f)); g.DrawLine(pen, P(t, 0.8f), P(t, 0.94f));
                    g.DrawLine(pen, P(0.06f, t), P(0.2f, t)); g.DrawLine(pen, P(0.8f, t), P(0.94f, t));
                }
                break;
            case OvIcon.Gpu:   // graphics card with a fan
                g.DrawRectangle(pen, r.X + s * 0.06f, r.Y + s * 0.26f, s * 0.82f, s * 0.48f);
                g.DrawEllipse(pen, r.X + s * 0.18f, r.Y + s * 0.34f, s * 0.32f, s * 0.32f);
                g.DrawLine(pen, P(0.62f, 0.42f), P(0.78f, 0.42f));
                g.DrawLine(pen, P(0.62f, 0.58f), P(0.78f, 0.58f));
                g.DrawLine(pen, P(0.2f, 0.74f), P(0.2f, 0.88f));
                break;
            case OvIcon.Power: // lightning bolt
                g.FillPolygon(brush, new[] { P(0.58f, 0.04f), P(0.2f, 0.56f), P(0.46f, 0.56f), P(0.38f, 0.96f), P(0.8f, 0.4f), P(0.54f, 0.4f) });
                break;
            case OvIcon.Ram:   // memory stick
            case OvIcon.Vram:
                g.DrawRectangle(pen, r.X + s * 0.06f, r.Y + s * 0.3f, s * 0.88f, s * 0.36f);
                for (int i = 0; i < 3; i++) g.FillRectangle(brush, r.X + s * (0.16f + i * 0.25f), r.Y + s * 0.38f, s * 0.16f, s * 0.2f);
                for (int i = 0; i < 5; i++) g.DrawLine(pen, P(0.14f + i * 0.18f, 0.74f), P(0.14f + i * 0.18f, 0.84f));
                if (icon == OvIcon.Vram) g.DrawLine(pen, P(0.06f, 0.14f), P(0.94f, 0.14f));
                break;
            case OvIcon.Fan:   // three blades around a hub
                for (int i = 0; i < 3; i++)
                {
                    var st = g.Save();
                    g.TranslateTransform(r.X + s / 2, r.Y + s / 2);
                    g.RotateTransform(i * 120);
                    g.FillEllipse(brush, -s * 0.12f, -s * 0.46f, s * 0.24f, s * 0.38f);
                    g.Restore(st);
                }
                g.FillEllipse(brush, r.X + s * 0.38f, r.Y + s * 0.38f, s * 0.24f, s * 0.24f);
                break;
            case OvIcon.Mode:  // mode: three sliders at different settings
                foreach (var (y, k) in new[] { (0.24f, 0.30f), (0.50f, 0.68f), (0.76f, 0.44f) })
                {
                    g.DrawLine(pen, P(0.08f, y), P(0.92f, y));
                    g.FillEllipse(brush, r.X + s * (k - 0.11f), r.Y + s * (y - 0.11f), s * 0.22f, s * 0.22f);
                }
                break;
        }
    }
}

/// <summary>Global hotkey. Several can coexist; each gets its own id.</summary>
public sealed class HotKey : NativeWindow, IDisposable
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, NoRepeat = 0x4000;

    static int _nextId = 0xA1E;
    readonly Action _action;
    readonly int _id;

    /// <summary>False if Windows refused the combination (another app already uses it).</summary>
    public bool Registered { get; }

    public HotKey(uint mods, Keys key, Action action)
    {
        _action = action;
        _id = Interlocked.Increment(ref _nextId);
        CreateHandle(new CreateParams());
        Registered = RegisterHotKey(Handle, _id, mods, (uint)key);
    }

    /// <summary>Registers a combination stored as Keys (key | Control | Shift | Alt); null for 0 (none).</summary>
    public static HotKey? FromCombo(int combo, Action action)
    {
        var k = (Keys)combo;
        if ((k & Keys.KeyCode) == Keys.None) return null;
        uint mods = NoRepeat;
        if (k.HasFlag(Keys.Control)) mods |= Ctrl;
        if (k.HasFlag(Keys.Shift)) mods |= Shift;
        if (k.HasFlag(Keys.Alt)) mods |= Alt;
        return new HotKey(mods, k & Keys.KeyCode, action);
    }

    /// <summary>"Ctrl+Shift+O" style text; empty for none.</summary>
    public static string Describe(int combo)
    {
        var k = (Keys)combo;
        var key = k & Keys.KeyCode;
        if (key == Keys.None) return "";
        var parts = new List<string>();
        if (k.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (k.HasFlag(Keys.Shift)) parts.Add("Shift");
        if (k.HasFlag(Keys.Alt)) parts.Add("Alt");
        parts.Add(key switch
        {
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (key - Keys.NumPad0),
            Keys.Oemtilde => "`", Keys.OemMinus => "-", Keys.Oemplus => "=", Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]", Keys.OemPipe => "\\", Keys.OemSemicolon => ";", Keys.OemQuotes => "'",
            Keys.Oemcomma => ",", Keys.OemPeriod => ".", Keys.OemQuestion => "/",
            Keys.Next => "PgDn", Keys.Prior => "PgUp", Keys.Back => "Backspace", Keys.Return => "Enter",
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam == (IntPtr)_id) _action();
        base.WndProc(ref m);
    }

    public void Dispose() { UnregisterHotKey(Handle, _id); DestroyHandle(); }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
}
