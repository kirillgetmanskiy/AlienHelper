using System.Drawing;
using System.Drawing.Drawing2D;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>
/// Fan curve editor: a profile list (built-in presets and the user's own), one chart per fan group with draggable
/// points, the live temperature and boost, and the Custom switch. Choosing a profile applies it at once.
/// </summary>
public sealed class FanCurveForm : Form
{
    readonly Controller _c;
    readonly SensorMonitor _mon;
    readonly FanCurveDriver _driver;
    readonly CurveEditor _cpu = new() { Title = "CPU", LineColor = Accent };
    readonly CurveEditor _gpu = new() { Title = "GPU", LineColor = Good };
    readonly PillButton _profileBtn = new("") { Height = S(30) };
    readonly ContextMenuStrip _menu = new() { Renderer = new Theme.MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
    readonly PillButton _save, _saveAs, _delete, _reset;
    readonly Toggle _customToggle = new("Custom");
    readonly PillButton _baseBtn = new("") { Height = S(30) };
    readonly List<PillButton> _ocPresets = new();
    readonly Toggle _ocToggle = new("Overclock");

    void SyncOc()
    {
        foreach (var b in _ocPresets) { b.Active = (string)b.Tag! == _c.S.GpuOc; b.Invalidate(); }
        _ocToggle.On = _c.S.GpuOcOn;
    }
    readonly ContextMenuStrip _baseMenu = new() { Renderer = new Theme.MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
    readonly Label _state = new() { AutoSize = false, ForeColor = Warm, Font = Theme.Font(8.5f), TextAlign = ContentAlignment.MiddleLeft };

    const int M = 14;

    public FanCurveForm(Controller c, SensorMonitor mon, FanCurveDriver driver)
    {
        _c = c; _mon = mon; _driver = driver;
        Text = Log.AppTitle + " — " + L.T("curves.title");
        Style(this);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(980), S(470));

        // top row: profile list, save / save as / delete / reset, Custom on the right
        int y = S(M);
        var lbl = new Label { Text = L.T("curves.profile"), ForeColor = Muted, Font = Theme.Font(9.5f), AutoSize = true, Location = new Point(S(M), y + S(6)) };
        Controls.Add(lbl);
        _profileBtn.SetBounds(S(M) + lbl.PreferredWidth + S(8), y, S(240), S(30));
        _profileBtn.Click += (_, _) => { BuildMenu(); _menu.Show(_profileBtn, new Point(0, _profileBtn.Height + S(2))); };
        Controls.Add(_profileBtn);
        int x = _profileBtn.Right + S(8);
        PillButton Btn(string text, Action act)
        {
            var b = new PillButton(text) { Height = S(30) };
            b.Width = TextRenderer.MeasureText(text, Theme.Font(9.5f)).Width + S(26);
            b.Location = new Point(x, y);
            b.Click += (_, _) => act();
            Controls.Add(b);
            x = b.Right + S(6);
            return b;
        }
        _save = Btn(L.T("kb.save"), Save);
        _saveAs = Btn(L.T("kb.saveas"), SaveAs);
        _delete = Btn(L.T("kb.delete"), Delete);
        _reset = Btn(L.T("curves.reset"), Reset);
        _customToggle.SetBounds(ClientSize.Width - S(M) - S(130), y + S(2), S(130), S(26));
        _customToggle.On = _c.S.FanCurve;
        _customToggle.Toggled += (_, _) =>
        {
            if (_customToggle.On == _c.S.FanCurve) return;
            // Custom is a mode: on selects it, off goes back to its base firmware profile
            int mode = _customToggle.On ? Controller.CustomMode : _c.CustomBase;
            Task.Run(() => _c.SelectMode(mode));
        };
        Controls.Add(_customToggle);
        y += S(30) + S(10);

        // the firmware profile Custom runs on: its power limits (the fans follow the curves on top of it)
        var baseLbl = new Label { Text = L.T("curves.base"), ForeColor = Muted, Font = Theme.Font(9.5f), AutoSize = true, Location = new Point(S(M), y + S(6)) };
        Controls.Add(baseLbl);
        _baseBtn.SetBounds(S(M) + baseLbl.PreferredWidth + S(8), y, S(240), S(30));
        _baseBtn.Click += (_, _) => { BuildBaseMenu(); _baseMenu.Show(_baseBtn, new Point(0, _baseBtn.Height + S(2))); };
        Controls.Add(_baseBtn);
        UpdateBaseText();
        var baseTip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 };
        baseTip.SetToolTip(baseLbl, L.T("curves.basetip"));
        baseTip.SetToolTip(_baseBtn, L.T("curves.basetip"));

        // Overclock: GPU core / memory offsets added to the original ones, only while Custom runs
        int ox = _baseBtn.Right + S(24);
        var ocLbl = new Label { Text = L.T("oc.title"), ForeColor = Muted, Font = Theme.Font(9.5f), AutoSize = true, Location = new Point(ox, y + S(6)) };
        Controls.Add(ocLbl);
        ox += ocLbl.PreferredWidth + S(8);
        foreach (var (id, core, mem) in GpuOverclock.Presets)
        {
            string pid = id;
            var b = new PillButton(L.T("oc." + id)) { Height = S(30), Tag = id };
            b.Width = TextRenderer.MeasureText(b.Text, Theme.Font(9.5f)).Width + S(24);
            b.Location = new Point(ox, y);
            new ToolTip().SetToolTip(b, L.T("oc.add", core, mem));
            b.Click += (_, _) => { _c.S.GpuOc = pid; _c.S.Save(); _c.RaiseOcChanged(); SyncOc(); };
            _ocPresets.Add(b);
            Controls.Add(b);
            ox = b.Right + S(5);
        }
        _ocToggle.SetBounds(ClientSize.Width - S(M) - S(150), y + S(2), S(150), S(26));
        _ocToggle.Toggled += (_, _) =>
        {
            if (_ocToggle.On == _c.S.GpuOcOn) return;
            _ocToggle.On = GpuOverclock.Toggle(_c, _ocToggle.On, this);   // stays off if the warning is declined
        };
        Controls.Add(_ocToggle);
        SyncOc();
        y += S(30) + S(10);

        // the two charts
        int w = (ClientSize.Width - 2 * S(M) - S(10)) / 2, h = S(310);
        _cpu.SetBounds(S(M), y, w, h);
        _gpu.SetBounds(S(M) + w + S(10), y, w, h);
        _cpu.Points = FanCurve.Copy(_c.S.CpuCurve);
        _gpu.Points = FanCurve.Copy(_c.S.GpuCurve);
        _cpu.Changed += () => { _c.S.CpuCurve = FanCurve.Copy(_cpu.Points); _c.S.Save(); UpdateProfileText(); };
        _gpu.Changed += () => { _c.S.GpuCurve = FanCurve.Copy(_gpu.Points); _c.S.Save(); UpdateProfileText(); };
        Controls.Add(_cpu);
        Controls.Add(_gpu);
        y += h + S(8);

        var hint = new Label
        {
            Text = L.T("curves.hint") + "  " + L.T("curves.addhint"), ForeColor = Muted, Font = Theme.Font(8.5f), AutoSize = false,
            Bounds = new Rectangle(S(M), y, ClientSize.Width - 2 * S(M), S(34)),
        };
        Controls.Add(hint);
        y += S(34);
        _state.SetBounds(S(M), y, ClientSize.Width - 2 * S(M), S(20));
        Controls.Add(_state);
        ClientSize = new Size(ClientSize.Width, y + S(20) + S(M - 4));

        UpdateProfileText();
        UpdateState();
        _mon.Updated += OnSample;
        // the window shows live temperatures: ask for them while it is open
        Shown += (_, _) => { _mon.Want(this, Need.Fans); OnSample(); };
        FormClosed += (_, _) => { _mon.Updated -= OnSample; _mon.Want(this, Need.None); };
        Disposed += (_, _) => { _mon.Updated -= OnSample; _mon.Want(this, Need.None); };
    }

    /// <summary>The main window or the tray switched Custom: mirror it.</summary>
    public void SyncCustom() { _customToggle.On = _c.S.FanCurve; SyncOc(); UpdateState(); }

    void UpdateState()
    {
        _state.Text = _c.S.FanCurve ? "" : L.T("curves.off");
    }

    void OnSample()
    {
        if (IsDisposed) return;
        var snap = _mon.Last;
        int Temp(string name) => snap?.Temps.FirstOrDefault(t => t.s.Name == name).temp ?? -1;
        Live(_cpu, Temp("CPU"), _driver.Cpu);
        Live(_gpu, Temp("GPU"), _driver.Gpu);
    }

    void Live(CurveEditor ed, int raw, (float temp, float boost, float target) d)
    {
        // while Custom runs show what the driver applies; otherwise what the curve would give at this temperature
        bool on = _c.S.FanCurve && d.temp > 0;
        ed.CurrentTemp = on ? d.temp : raw;
        ed.CurrentBoost = on ? d.boost : raw > 0 ? FanCurve.Eval(ed.Points, raw) : -1;
        ed.Invalidate();
    }

    void UpdateBaseText() { _baseBtn.Text = _c.ProfileName(_c.CustomBase) + "   ▾"; _baseBtn.Invalidate(); }

    void BuildBaseMenu()
    {
        _baseMenu.Items.Clear();
        foreach (var p in _c.Awcc?.Profiles ?? new List<byte>())
        {
            byte pp = p;
            _baseMenu.Items.Add(new ToolStripMenuItem(_c.ProfileName(p), null, (_, _) =>
            {
                _c.S.CustomBase = pp;
                _c.S.Save();
                UpdateBaseText();
                if (_c.S.FanCurve) Task.Run(() => _c.SelectMode(Controller.CustomMode));   // apply the new base now
            }) { Checked = _c.CustomBase == p, ForeColor = Fg });
        }
    }

    // ---------- profiles ----------

    static string PresetName(string id) => L.T("curve." + id);

    /// <summary>Current curves equal to the selected profile's, or edited since (shown with ●).</summary>
    (string name, bool modified, bool user) Selected()
    {
        string id = _c.S.CurveProfile;
        if (id.StartsWith("user:"))
        {
            var p = _c.S.UserCurves.FirstOrDefault(u => "user:" + u.Name == id);
            if (p != null) return (p.Name, !FanCurve.Same(p.Cpu, _c.S.CpuCurve) || !FanCurve.Same(p.Gpu, _c.S.GpuCurve), true);
        }
        var pre = FanCurve.Presets.FirstOrDefault(x => x.id == id);
        if (pre.id == null) pre = FanCurve.Presets.First(x => x.id == "balanced");
        return (PresetName(pre.id), !FanCurve.Same(pre.cpu, _c.S.CpuCurve) || !FanCurve.Same(pre.gpu, _c.S.GpuCurve), false);
    }

    void UpdateProfileText()
    {
        var (name, modified, user) = Selected();
        _profileBtn.Text = name + (modified ? "  ●" : "") + "   ▾";
        _profileBtn.Invalidate();
        _save.Enabled = user && modified;
        _delete.Enabled = user;
        _reset.Enabled = modified;
        foreach (var b in new[] { _save, _delete, _reset }) b.Invalidate();
    }

    void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new ToolStripMenuItem(L.T("curves.presets")) { Enabled = false });
        foreach (var (id, cpu, gpu) in FanCurve.Presets)
        {
            string pid = id;
            _menu.Items.Add(new ToolStripMenuItem("   " + PresetName(id), null, (_, _) => Apply(pid, cpu, gpu)) { Checked = _c.S.CurveProfile == id, ForeColor = Fg });
        }
        if (_c.S.UserCurves.Count > 0)
        {
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem(L.T("curves.mine")) { Enabled = false });
            foreach (var p in _c.S.UserCurves)
            {
                var up = p;
                _menu.Items.Add(new ToolStripMenuItem("   " + p.Name, null, (_, _) => Apply("user:" + up.Name, up.Cpu, up.Gpu)) { Checked = _c.S.CurveProfile == "user:" + p.Name, ForeColor = Fg });
            }
        }
    }

    /// <summary>Makes a profile's curves the active ones; the driver picks them up with the next sample.</summary>
    void Apply(string id, List<int[]> cpu, List<int[]> gpu)
    {
        _c.S.CurveProfile = id;
        _c.S.CpuCurve = FanCurve.Copy(cpu);
        _c.S.GpuCurve = FanCurve.Copy(gpu);
        _c.S.Save();
        Log.Info($"fan curve profile -> {id}");
        _cpu.Points = FanCurve.Copy(cpu);
        _gpu.Points = FanCurve.Copy(gpu);
        _cpu.Invalidate();
        _gpu.Invalidate();
        UpdateProfileText();
    }

    void Save()
    {
        var (name, _, user) = Selected();
        if (!user) { SaveAs(); return; }
        var p = _c.S.UserCurves.First(u => u.Name == name);
        p.Cpu = FanCurve.Copy(_c.S.CpuCurve);
        p.Gpu = FanCurve.Copy(_c.S.GpuCurve);
        _c.S.Save();
        UpdateProfileText();
    }

    void SaveAs()
    {
        int n = 1;
        while (_c.S.UserCurves.Any(u => u.Name == L.T("curves.newname", n))) n++;
        string? name = InputDialog.Ask(this, L.T("kb.saveas"), L.T("kb.save.name"), L.T("curves.newname", n));
        if (string.IsNullOrWhiteSpace(name)) return;
        var existing = _c.S.UserCurves.FirstOrDefault(u => u.Name == name);
        if (existing == null) _c.S.UserCurves.Add(existing = new CurveProfile { Name = name });
        existing.Cpu = FanCurve.Copy(_c.S.CpuCurve);
        existing.Gpu = FanCurve.Copy(_c.S.GpuCurve);
        _c.S.CurveProfile = "user:" + name;
        _c.S.Save();
        UpdateProfileText();
    }

    void Delete()
    {
        var (name, _, user) = Selected();
        if (!user) return;
        _c.S.UserCurves.RemoveAll(u => u.Name == name);
        var b = FanCurve.Presets.First(x => x.id == "balanced");
        Apply(b.id, b.cpu, b.gpu);
    }

    /// <summary>Drops the edits: back to the selected profile's curves.</summary>
    void Reset()
    {
        string id = _c.S.CurveProfile;
        var up = _c.S.UserCurves.FirstOrDefault(u => "user:" + u.Name == id);
        if (up != null) { Apply(id, up.Cpu, up.Gpu); return; }
        var pre = FanCurve.Presets.FirstOrDefault(x => x.id == id);
        if (pre.id == null) pre = FanCurve.Presets.First(x => x.id == "balanced");
        Apply(pre.id, pre.cpu, pre.gpu);
    }
}

/// <summary>
/// One fan curve: temperature across (30…100 °C), boost up (0…100 %). Points are dragged with the mouse; a double click
/// adds one, a right click removes one (at least two stay). Shows the live temperature and the boost applied.
/// </summary>
public sealed class CurveEditor : DrawnControl
{
    public string Title { get; set; } = "";
    public Color LineColor { get; set; } = Accent;
    public List<int[]> Points { get; set; } = new();
    public float CurrentTemp { get; set; } = -1;
    public float CurrentBoost { get; set; } = -1;
    /// <summary>A point was moved, added or removed (on mouse release).</summary>
    public event Action? Changed;

    int _drag = -1, _hover = -1;

    RectangleF Plot => new(S(44), S(40), Width - S(44) - S(18), Height - S(40) - S(30));
    float X(float t) => Plot.Left + Plot.Width * (t - FanCurve.MinTemp) / (FanCurve.MaxTemp - FanCurve.MinTemp);
    float Y(float b) => Plot.Bottom - Plot.Height * b / 100f;
    float T(float x) => FanCurve.MinTemp + (x - Plot.Left) / Plot.Width * (FanCurve.MaxTemp - FanCurve.MinTemp);
    float B(float y) => (Plot.Bottom - y) / Plot.Height * 100f;

    protected override void Draw(Graphics g)
    {
        var card = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var p = Round(card, Sf(12)))
        using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
        using (var p = Round(card, Sf(12)))
        using (var pen = new Pen(Border)) g.DrawPath(pen, p);

        using var tf = Semibold(11f);
        TextRenderer.DrawText(g, Title, tf, new Point(S(16), S(10)), Fg);
        if (CurrentTemp > 0)
        {
            using var nf = Theme.Font(9f);
            string now = L.T("curves.now", CurrentTemp.ToString("0"), CurrentBoost <= 0.5f ? L.T("auto") : $"+{CurrentBoost:0}%");
            var sz = TextRenderer.MeasureText(now, nf);
            TextRenderer.DrawText(g, now, nf, new Point(Width - S(16) - sz.Width, S(12)), TempColor((int)CurrentTemp));
        }

        var plot = Plot;
        using var grid = new Pen(Color.FromArgb(45, Muted)) { DashStyle = DashStyle.Dot };
        using var af = Theme.Font(8f);
        for (int t = FanCurve.MinTemp; t <= FanCurve.MaxTemp; t += 10)
        {
            float x = X(t);
            g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
            string s = $"{t}°";
            var sz = TextRenderer.MeasureText(s, af);
            TextRenderer.DrawText(g, s, af, new Point((int)(x - sz.Width / 2f), (int)plot.Bottom + S(4)), Muted);
        }
        for (int b = 0; b <= 100; b += 25)
        {
            float y = Y(b);
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            string s = $"{b}%";
            var sz = TextRenderer.MeasureText(s, af);
            TextRenderer.DrawText(g, s, af, new Point((int)plot.Left - sz.Width - S(4), (int)(y - sz.Height / 2f)), Muted);
        }
        if (Points.Count < 2) return;

        // curve, flat before the first point and after the last one (that is how it is applied)
        var pts = new List<PointF> { new(plot.Left, Y(Points[0][1])) };
        pts.AddRange(Points.Select(p => new PointF(X(p[0]), Y(p[1]))));
        pts.Add(new PointF(plot.Right, Y(Points[^1][1])));
        using (var area = new GraphicsPath())
        {
            area.AddLines(pts.ToArray());
            area.AddLine(pts[^1], new PointF(plot.Right, plot.Bottom));
            area.AddLine(new PointF(plot.Right, plot.Bottom), new PointF(plot.Left, plot.Bottom));
            area.CloseFigure();
            using var fill = AreaBrush(plot, LineColor, 60);
            g.FillPath(fill, area);
        }
        using (var pen = new Pen(LineColor, Sf(2.2f)) { LineJoin = LineJoin.Round })
            g.DrawLines(pen, pts.ToArray());

        // live temperature and the boost applied
        if (CurrentTemp > 0)
        {
            float cx = X(Math.Clamp(CurrentTemp, FanCurve.MinTemp, FanCurve.MaxTemp));
            using (var cur = new Pen(Color.FromArgb(170, Warm), Sf(1.4f)) { DashStyle = DashStyle.Dash })
                g.DrawLine(cur, cx, plot.Top, cx, plot.Bottom);
            if (CurrentBoost >= 0)
                using (var b = new SolidBrush(Warm))
                    FillDot(g, b, new RectangleF(cx - Sf(5), Y(CurrentBoost) - Sf(5), Sf(10), Sf(10)));
        }

        // draggable points, the active one labelled
        for (int i = 0; i < Points.Count; i++)
        {
            float r = Sf(i == _drag || i == _hover ? 7f : 5.5f);
            var c = new PointF(X(Points[i][0]), Y(Points[i][1]));
            using (var b = new SolidBrush(Knob)) FillDot(g, b, new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r));
            using (var pen = new Pen(LineColor, Sf(2f))) DrawDot(g, pen, new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r));
            if (i == _drag || i == _hover)
            {
                using var lf = Semibold(9f);
                string s = $"{Points[i][0]}° · {Points[i][1]}%";
                var sz = TextRenderer.MeasureText(s, lf);
                float lx = Math.Clamp(c.X - sz.Width / 2f, plot.Left, plot.Right - sz.Width);
                float ly = c.Y - r - sz.Height - S(2);
                if (ly < plot.Top) ly = c.Y + r + S(2);
                TextRenderer.DrawText(g, s, lf, new Point((int)lx, (int)ly), Fg);
            }
        }
    }

    int Hit(Point m)
    {
        for (int i = 0; i < Points.Count; i++)
            if (Math.Abs(m.X - X(Points[i][0])) <= S(10) && Math.Abs(m.Y - Y(Points[i][1])) <= S(10)) return i;
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = Hit(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (i >= 0 && Points.Count > 2) { Points.RemoveAt(i); _hover = -1; Invalidate(); Changed?.Invoke(); }
        }
        else if (i >= 0) { _drag = i; Capture = true; }
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Hit(e.Location) < 0 && Plot.Contains(e.Location) && Points.Count < 10)
        {
            int t = (int)Math.Round(T(e.X)), b = (int)Math.Round(Math.Clamp(B(e.Y), 0, 100));
            if (!Points.Any(p => p[0] == t))
            {
                Points.Add(new[] { t, b });
                Points.Sort((p, q) => p[0].CompareTo(q[0]));
                Invalidate();
                Changed?.Invoke();
            }
        }
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag >= 0)
        {
            // a point stays between its neighbours (1 °C apart) and inside the chart
            int lo = _drag > 0 ? Points[_drag - 1][0] + 1 : FanCurve.MinTemp;
            int hi = _drag < Points.Count - 1 ? Points[_drag + 1][0] - 1 : FanCurve.MaxTemp;
            Points[_drag][0] = Math.Clamp((int)Math.Round(T(e.X)), lo, hi);
            Points[_drag][1] = Math.Clamp((int)Math.Round(B(e.Y)), 0, 100);
            Invalidate();
        }
        else
        {
            int h = Hit(e.Location);
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drag >= 0) { _drag = -1; Capture = false; Invalidate(); Changed?.Invoke(); }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_drag < 0 && _hover >= 0) { _hover = -1; Invalidate(); }
        base.OnMouseLeave(e);
    }
}
