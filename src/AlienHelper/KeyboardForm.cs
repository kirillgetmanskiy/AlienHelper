using System.Drawing;
using System.Drawing.Drawing2D;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>
/// Per-key keyboard editor: paint keys, pick / create / save / delete templates from a dropdown
/// (fire and ocean are animated), fix the key → LED map.
/// </summary>
public sealed class KeyboardForm : Form
{
    static readonly Color[] Palette =
    {
        Hex("#00B4FF"), Hex("#FFFFFF"), Hex("#FF1E1E"), Hex("#FF7A00"), Hex("#FFD200"), Hex("#00FF6A"),
        Hex("#00E5FF"), Hex("#2A5BFF"), Hex("#8A2BE2"), Hex("#FF2D95"),
    };

    /// <summary>A template: built-in (by id) or user-saved.</summary>
    sealed record Entry(string Title, string? BuiltinId, UserTemplate? User)
    {
        public KeyboardLayout.Template? Builtin => KeyboardLayout.Find(BuiltinId);
        public bool Animated => Builtin?.Animate != null;
    }

    readonly Controller _c;
    readonly Dictionary<int, Color> _colors;
    readonly KeyboardCanvas _canvas = new();
    readonly List<Swatch> _swatches = new();
    readonly Swatch _custom;
    readonly Swatch _eraser = new(Color.Black);
    readonly Slider _bright = new();
    readonly Label _brightValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = Fg, Font = Theme.Font(9f) };
    readonly Label _hint = new() { AutoSize = false, ForeColor = Muted, Font = Theme.Font(9f) };
    readonly PillButton _calibBtn = new(L.T("kb.calib")) { Subtle = true };
    readonly FlowLayoutPanel _calibBar = new() { AutoSize = true, WrapContents = false, Visible = false, BackColor = Bg };
    readonly Label _calibInfo = new() { AutoSize = true, ForeColor = Fg, Font = Semibold(10f) };
    readonly PillButton _pick = new("") { Height = S(30) };
    readonly ContextMenuStrip _menu = new() { Renderer = new Theme.MenuRenderer(), ShowImageMargin = true, ShowCheckMargin = true };
    readonly Toggle _animToggle = new(L.T("kb.anim"));
    readonly System.Windows.Forms.Timer _push = new() { Interval = 120 };
    Entry? _current;
    bool _modified;
    Color _brush;
    bool _pushing, _dirty;
    int _calibKey = -1;

    readonly bool _preview;

    /// <param name="preview">Render only, never write to the keyboard (screenshot mode).</param>
    public KeyboardForm(Controller c, bool preview = false, int template = -1)
    {
        _c = c;
        _preview = preview;
        Text = Log.AppTitle + " — " + L.T("kb.title");
        Style(this);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(S(980), S(520));
        Size = new Size(S(1180), S(640));

        var anim = KeyboardLayout.Find(c.S.KeyboardAnimation);
        _colors = !c.S.KeyboardCustom ? AllKeys(SafeColor(c.S.KeyboardColor))
            : anim?.Animate != null ? KeyboardLayout.Render(anim) : c.SavedKeyScheme();
        _brush = Palette[0];
        _custom = new Swatch(_brush) { IsCustom = true };

        // --- row 1: palette, eraser, brightness, fill / clear ---
        var top = Row(S(54), S(12));
        foreach (var p in Palette)
        {
            var s = new Swatch(p);
            s.Click += (_, _) => PickBrush(s.Color, s);
            _swatches.Add(s);
            Add(top, s, S(4));
        }
        _custom.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _brush, FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK) { _custom.Color = dlg.Color; PickBrush(dlg.Color, _custom); }
        };
        Add(top, _custom, S(10));
        _eraser.Click += (_, _) => PickBrush(Color.Black, _eraser);
        new ToolTip().SetToolTip(_eraser, L.T("kb.off"));
        Add(top, _eraser, S(22));

        Add(top, Caption(L.T("kb.brightness")), S(6));
        _bright.Width = S(160);
        _bright.Value = c.S.KeyboardBrightness;
        _brightValue.Size = new Size(S(46), S(26));
        _brightValue.Text = $"{_bright.Value}%";
        _bright.ValueChanged += (_, _) => _brightValue.Text = $"{_bright.Value}%";
        _bright.ValueCommitted += (_, _) => { int v = _bright.Value; if (!_preview) Task.Run(() => _c.SetKeyboardBrightness(v)); };
        Add(top, _bright, 0);
        Add(top, _brightValue, S(18));
        Add(top, Button(L.T("kb.fill"), () => { foreach (var k in KeyboardLayout.Keys) Set(k.Index, _brush); Edited(); }), S(6));
        Add(top, Button(L.T("kb.clear"), () => { _colors.Clear(); Edited(); }), 0);

        // --- row 2: template dropdown + actions + animation switch ---
        var tpl = Row(S(46), S(4));
        Add(tpl, Caption(L.T("kb.template")), S(8));
        _pick.Click += (_, _) => { FillMenu(); _menu.Show(_pick, new Point(0, _pick.Height + S(2))); };
        Add(tpl, _pick, S(14));
        Add(tpl, Button("＋ " + L.T("kb.new"), NewTemplate), S(6));
        Add(tpl, Button(L.T("kb.save"), SaveTemplate), S(6));
        Add(tpl, Button(L.T("kb.saveas"), () => SaveAs()), S(6));
        Add(tpl, Button(L.T("kb.delete"), DeleteTemplate), S(6));
        Add(tpl, Button(L.T("kb.restore"), () => { _c.S.HiddenTemplates.Clear(); _c.S.Save(); }, subtle: true), S(22));
        _animToggle.Size = new Size(TextRenderer.MeasureText(_animToggle.Text, Theme.Font(9.5f)).Width + S(56), S(26));
        _animToggle.On = c.S.AnimationOn;
        _animToggle.Toggled += (_, _) =>
        {
            bool on = _animToggle.On;
            if (_preview) { _c.S.AnimationOn = on; return; }
            Task.Run(() => _c.SetAnimationOn(on));
        };
        new ToolTip().SetToolTip(_animToggle, L.T("kb.anim.tip"));
        Add(tpl, _animToggle, 0);

        // --- bottom: hint + calibration ---
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = S(50), BackColor = Bg };
        _hint.Text = L.T("kb.hint");
        _calibBtn.Click += (_, _) => SetCalibration(_calibKey < 0 && !_calibBar.Visible);
        var prev = new PillButton("◀") { Size = new Size(S(40), S(30)) };
        var next = new PillButton("▶") { Size = new Size(S(40), S(30)) };
        var reset = Button(L.T("kb.reset"), () => { if (_calibKey >= 0) { _c.S.KeyIds.Remove(KeyboardLayout.Keys[_calibKey].Name); _c.S.Save(); ShowCalib(); } });
        var done = Button(L.T("kb.done"), () => SetCalibration(false));
        done.Active = true;
        prev.Click += (_, _) => ShiftId(-1);
        next.Click += (_, _) => ShiftId(+1);
        _calibBar.Height = S(30);
        foreach (var (ctl, gap) in new (Control, int)[] { (_calibInfo, S(12)), (prev, S(4)), (next, S(10)), (reset, S(6)), (done, 0) })
        {
            int h = ctl.Height > 0 && ctl is not Label ? ctl.Height : ctl.PreferredSize.Height;
            ctl.Margin = new Padding(0, Math.Max(0, (S(30) - h) / 2), gap, 0);
            _calibBar.Controls.Add(ctl);
        }
        bottom.Controls.AddRange(new Control[] { _hint, _calibBtn, _calibBar });
        bottom.Resize += (_, _) =>
        {
            int m = S(14), y = (bottom.Height - S(30)) / 2;
            _calibBtn.Width = TextRenderer.MeasureText(_calibBtn.Text, Theme.Font(9.5f)).Width + S(28);
            _calibBtn.Height = S(30);
            _calibBtn.Location = new Point(bottom.Width - m - _calibBtn.Width, y);
            _hint.SetBounds(m, y, _calibBtn.Left - 2 * m, S(30));
            _hint.TextAlign = ContentAlignment.MiddleLeft;
            _calibBar.Location = new Point(m, y);
        };

        _canvas.Dock = DockStyle.Fill;
        _canvas.Colors = _colors;
        _canvas.KeyPressed += OnKey;
        Controls.Add(_canvas);
        Controls.Add(bottom);
        Controls.Add(tpl);
        Controls.Add(top);

        PickBrush(_brush, _swatches[0]);
        _push.Tick += (_, _) => Push();
        _c.AnimationFrame += OnAnimationFrame;
        FormClosed += (_, _) => { _push.Dispose(); _c.AnimationFrame -= OnAnimationFrame; };

        if (template >= 0)
        {
            var t = KeyboardLayout.Templates[template];
            _colors.Clear();
            foreach (var (k, v) in KeyboardLayout.Render(t)) _colors[k] = v;
            _current = new Entry(L.T(t.NameKey), t.Id, null);
        }
        else if (c.S.KeyboardCustom && anim != null) _current = new Entry(L.T(anim.NameKey), anim.Id, null);
        UpdatePick();
    }

    // ---------------- layout helpers (rows keep every control vertically centred) ----------------

    FlowLayoutPanel Row(int height, int padTop) =>
        new() { Dock = DockStyle.Top, Height = height, Padding = new Padding(S(14), padTop, S(14), 0), BackColor = Bg, WrapContents = false };

    static void Add(FlowLayoutPanel row, Control c, int gapAfter)
    {
        int rowH = row.Height - row.Padding.Top - row.Padding.Bottom;
        int h = c is Label { AutoSize: true } ? c.PreferredSize.Height : c.Height;
        c.Margin = new Padding(0, Math.Max(0, (Math.Min(rowH, S(32)) - h) / 2), gapAfter, 0);
        row.Controls.Add(c);
    }

    static Label Caption(string text) => new() { Text = text, ForeColor = Muted, Font = Theme.Font(9f), AutoSize = true };

    static PillButton Button(string text, Action click, bool subtle = false)
    {
        var b = new PillButton(text) { Height = S(30), Subtle = subtle };
        b.Width = TextRenderer.MeasureText(text, Theme.Font(9.5f)).Width + S(28);
        b.Click += (_, _) => click();
        return b;
    }

    static Color SafeColor(string hex) { try { return ColorTranslator.FromHtml(hex); } catch { return Palette[0]; } }

    static Dictionary<int, Color> AllKeys(Color c) => KeyboardLayout.Keys.ToDictionary(k => k.Index, _ => c);

    // ---------------- templates ----------------

    IEnumerable<Entry> Entries()
    {
        foreach (var t in KeyboardLayout.Templates.Where(t => !_c.S.HiddenTemplates.Contains(t.Id)))
            yield return new Entry(L.T(t.NameKey), t.Id, null);
        foreach (var u in _c.S.UserTemplates)
            yield return new Entry(u.Name, null, u);
    }

    Dictionary<int, Color> ColorsOf(Entry e) => e.Builtin is { } t ? KeyboardLayout.Render(t) : FromNames(e.User!.Colors);

    void UpdatePick()
    {
        string name = _current?.Title ?? L.T("kb.none");
        _pick.Text = (_modified ? "● " : "") + name + (_current?.Animated == true ? "  ▶" : "") + "   ▾";
        _pick.Width = Math.Max(S(200), TextRenderer.MeasureText(_pick.Text, Theme.Font(9.5f)).Width + S(36));
        // only the animated templates (fire, ocean) can be animated
        _animToggle.Enabled = _current?.Animated == true;
    }

    void FillMenu()
    {
        _menu.Items.Clear();
        bool userHeader = false;
        foreach (var e in Entries())
        {
            if (e.User != null && !userHeader)
            {
                _menu.Items.Add(new ToolStripSeparator());
                userHeader = true;
            }
            var item = new ToolStripMenuItem(e.Title + (e.Animated ? "   ▶ " + L.T("kb.anim") : ""))
            {
                Image = Preview(ColorsOf(e)),
                Checked = _current != null && _current.Title == e.Title && _current.BuiltinId == e.BuiltinId,
                ForeColor = Fg,
            };
            var entry = e;
            item.Click += (_, _) => ApplyEntry(entry);
            _menu.Items.Add(item);
        }
    }

    /// <summary>Small strip of key colors for the dropdown.</summary>
    static Bitmap Preview(Dictionary<int, Color> colors)
    {
        int w = S(64), h = S(12);
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var keys = KeyboardLayout.Keys.Where(k => k.Row is 2 or 3).OrderBy(k => k.X).ToList();
        float dx = w / (float)keys.Count;
        for (int i = 0; i < keys.Count; i++)
        {
            var c = colors.TryGetValue(keys[i].Index, out var col) ? col : Color.FromArgb(40, 44, 52);
            using var b = new SolidBrush(c);
            g.FillRectangle(b, i * dx, h * 0.2f, Math.Max(1, dx - 1), h * 0.6f);
        }
        return bmp;
    }

    static Dictionary<int, Color> FromNames(Dictionary<string, string> byName)
    {
        var d = new Dictionary<int, Color>();
        foreach (var k in KeyboardLayout.Keys)
            if (byName.TryGetValue(k.Name, out var hex))
                try { d[k.Index] = ColorTranslator.FromHtml(hex); } catch { }
        return d;
    }

    void ApplyEntry(Entry e)
    {
        _current = e;
        _modified = false;
        UpdatePick();
        _colors.Clear();
        foreach (var (k, v) in ColorsOf(e)) _colors[k] = v;
        if (e.Builtin is { Animate: not null } t)
        {
            // animated: the controller drives the keyboard; the canvas follows its frames
            _push.Stop();
            _canvas.Invalidate();
            if (!_preview) Enqueue(() => _c.UseAnimatedTemplate(t.Id));
            return;
        }
        // a picked template goes to the keyboard right away (no debounce)
        _canvas.Invalidate();
        _push.Stop();
        _dirty = true;
        Push();
    }

    // every write from the editor runs in order on one worker chain, so quick clicks cannot overtake each other
    Task _chain = Task.CompletedTask;
    readonly object _chainLock = new();

    Task Enqueue(Action work)
    {
        lock (_chainLock) return _chain = _chain.ContinueWith(_ => { try { work(); } catch { } }, TaskScheduler.Default);
    }

    void NewTemplate()
    {
        string? name = InputDialog.Ask(this, L.T("kb.new"), L.T("kb.save.name"), NextName());
        if (string.IsNullOrWhiteSpace(name)) return;
        var u = Upsert(name, new Dictionary<string, string>());
        _current = new Entry(u.Name, null, u);
        _modified = false;
        _colors.Clear();
        UpdatePick();
        Changed();
    }

    void SaveTemplate()
    {
        if (_current?.User != null)
        {
            _current.User.Colors = Snapshot();
            _c.S.Save();
            _modified = false;
            UpdatePick();
        }
        else SaveAs();
    }

    void SaveAs()
    {
        string? name = InputDialog.Ask(this, L.T("kb.saveas"), L.T("kb.save.name"), NextName());
        if (string.IsNullOrWhiteSpace(name)) return;
        var u = Upsert(name, Snapshot());
        _current = new Entry(u.Name, null, u);
        _modified = false;
        UpdatePick();
    }

    string NextName() => L.T("kb.save.default", _c.S.UserTemplates.Count + 1);

    Dictionary<string, string> Snapshot() =>
        _colors.ToDictionary(kv => KeyboardLayout.Keys[kv.Key].Name, kv => ColorTranslator.ToHtml(kv.Value));

    UserTemplate Upsert(string name, Dictionary<string, string> colors)
    {
        name = name.Trim();
        var u = _c.S.UserTemplates.FirstOrDefault(x => x.Name == name);
        if (u == null) _c.S.UserTemplates.Add(u = new UserTemplate { Name = name });
        u.Colors = colors;
        _c.S.Save();
        return u;
    }

    void DeleteTemplate()
    {
        if (_current == null) return;
        if (_current.User != null) _c.S.UserTemplates.Remove(_current.User);
        else if (_current.BuiltinId != null && !_c.S.HiddenTemplates.Contains(_current.BuiltinId)) _c.S.HiddenTemplates.Add(_current.BuiltinId);
        _c.S.Save();
        _current = null;
        _modified = false;
        UpdatePick();
    }

    void OnAnimationFrame(string id, Dictionary<int, Color> frame)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                // only frames of the animation that is selected right now; a late frame must not replace a new template
                if (_calibBar.Visible || _current?.BuiltinId != id || !_current.Animated) return;
                _colors.Clear();
                foreach (var (k, v) in frame) _colors[k] = v;
                _canvas.Invalidate();
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    // ---------------- painting ----------------

    void PickBrush(Color c, Swatch from)
    {
        _brush = c;
        foreach (var s in _swatches) s.Selected = s == from;
        _custom.Selected = _custom == from;
        _eraser.Selected = _eraser == from;
    }

    void Set(int index, Color c)
    {
        if (c.R + c.G + c.B == 0) _colors.Remove(index);
        else _colors[index] = c;
    }

    void OnKey(int index, MouseButtons button)
    {
        if (_calibBar.Visible)
        {
            _calibKey = index;
            ShowCalib();
            return;
        }
        Set(index, button == MouseButtons.Right ? Color.Black : _brush);
        Edited();
    }

    /// <summary>A user edit: painting stops any animation and marks the template as modified.</summary>
    void Edited()
    {
        if (_current?.Animated == true) _current = null;   // an animation frame became a static drawing
        _modified = true;
        UpdatePick();
        Changed();
    }

    void Changed()
    {
        _canvas.Invalidate();
        _dirty = true;
        _push.Stop();
        _push.Start();
    }

    /// <summary>Debounced write to the keyboard on a worker thread.</summary>
    async void Push()
    {
        _push.Stop();
        if (_preview) return;
        if (_pushing || _calibBar.Visible) { if (_dirty) _push.Start(); return; }
        _pushing = true;
        _dirty = false;
        var snapshot = new Dictionary<int, Color>(_colors);
        try { await Enqueue(() => _c.SetKeyScheme(snapshot)); }
        finally { _pushing = false; }
        if (_dirty) _push.Start();
    }

    // ---------------- calibration ----------------

    void SetCalibration(bool on)
    {
        _calibBar.Visible = on;
        _hint.Visible = !on;
        _calibBtn.Visible = !on;
        _calibKey = -1;
        _canvas.Selected = -1;
        _calibInfo.Text = L.T("kb.calib.hint");
        if (on) _c.StopAnimation();
        if (!on) Changed();
        else Task.Run(() => _c.LightSingleId(-1));   // all dark until a key is picked
        _canvas.Invalidate();
    }

    void ShowCalib()
    {
        if (_calibKey < 0) return;
        var key = KeyboardLayout.Keys[_calibKey];
        int id = _c.LightId(key);
        _canvas.Selected = _calibKey;
        _canvas.Invalidate();
        string label = key.Label.Length > 0 ? key.Label : "Space";
        _calibInfo.Text = $"{label}  →  LED {id}" + (id != key.DefaultId ? "  *" : "");
        Task.Run(() => _c.LightSingleId(id));
    }

    void ShiftId(int delta)
    {
        if (_calibKey < 0) return;
        var key = KeyboardLayout.Keys[_calibKey];
        int id = Math.Clamp(_c.LightId(key) + delta, 0, _c.S.KeyboardLights - 1);
        if (id == key.DefaultId) _c.S.KeyIds.Remove(key.Name); else _c.S.KeyIds[key.Name] = id;
        _c.S.Save();
        ShowCalib();
    }
}

/// <summary>Small dark text prompt.</summary>
public static class InputDialog
{
    public static string? Ask(IWin32Window owner, string title, string prompt, string initial)
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(S(360), S(132)), ShowInTaskbar = false,
        };
        Style(f);
        var lbl = new Label { Text = prompt, ForeColor = Muted, AutoSize = true, Location = new Point(S(14), S(14)) };
        var box = new TextBox
        {
            Text = initial, Bounds = new Rectangle(S(14), S(40), S(332), S(28)),
            BackColor = CardHi, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10f),
        };
        var ok = new PillButton("OK") { Active = true, Bounds = new Rectangle(S(166), S(86), S(86), S(32)) };
        var cancel = new PillButton("×") { Bounds = new Rectangle(S(260), S(86), S(86), S(32)) };
        ok.Click += (_, _) => { f.DialogResult = DialogResult.OK; f.Close(); };
        cancel.Click += (_, _) => { f.DialogResult = DialogResult.Cancel; f.Close(); };
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.OK; f.Close(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { f.DialogResult = DialogResult.Cancel; f.Close(); }
        };
        f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        f.Shown += (_, _) => { box.Focus(); box.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

/// <summary>Draws the keyboard; reports clicks and drags over keys.</summary>
public sealed class KeyboardCanvas : DrawnControl
{
    public Dictionary<int, Color> Colors { get; set; } = new();
    public int Selected { get; set; } = -1;
    public event Action<int, MouseButtons>? KeyPressed;
    int _lastKey = -1;
    MouseButtons _dragButton;

    RectangleF Board()
    {
        float m = Sf(18);
        float unit = Math.Min((Width - 2 * m) / KeyboardLayout.Width, (Height - 2 * m) / KeyboardLayout.Height);
        float w = unit * KeyboardLayout.Width, h = unit * KeyboardLayout.Height;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, unit, 0);   // X,Y = origin, Width = unit
    }

    RectangleF KeyRect(KeyboardLayout.Key k, RectangleF b)
    {
        float u = b.Width, gap = u * 0.08f;
        return new RectangleF(b.X + k.X * u + gap / 2, b.Y + k.Y * u + gap / 2, k.W * u - gap, k.H * u - gap);
    }

    int HitTest(Point p)
    {
        var b = Board();
        foreach (var k in KeyboardLayout.Keys)
            if (KeyRect(k, b).Contains(p)) return k.Index;
        return -1;
    }

    protected override void Draw(Graphics g)
    {
        var b = Board();
        float u = b.Width;
        // chassis plate
        var plate = new RectangleF(b.X - u * 0.25f, b.Y - u * 0.25f, u * (KeyboardLayout.Width + 0.5f), u * (KeyboardLayout.Height + 0.5f));
        using (var pp = Round(plate, u * 0.3f))
        using (var pb = new SolidBrush(Theme.Card)) g.FillPath(pb, pp);

        foreach (var k in KeyboardLayout.Keys)
        {
            var r = KeyRect(k, b);
            bool lit = Colors.TryGetValue(k.Index, out var c);
            using var path = Round(r, u * 0.12f);
            if (lit)
            {
                // soft glow under lit keys
                var glow = RectangleF.Inflate(r, u * 0.06f, u * 0.06f);
                using var gp = Round(glow, u * 0.16f);
                using var gb = new SolidBrush(Color.FromArgb(70, c));
                g.FillPath(gb, gp);
            }
            using (var fb = new SolidBrush(lit ? Blend(Hex("#14171D"), c, 0.85f) : Hex("#1C2027"))) g.FillPath(fb, path);
            bool sel = k.Index == Selected;
            using (var pen = new Pen(sel ? Fg : lit ? Color.FromArgb(160, c) : Border, sel ? Sf(2.2f) : 1f)) g.DrawPath(pen, path);

            if (k.Label.Length > 0)
            {
                float lum = lit ? (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f : 0;
                var tc = lit ? (lum > 0.6f ? Color.FromArgb(20, 20, 20) : Color.White) : Muted;
                float pt = k.Label.Length > 3 ? 7.5f : 9f;
                using var f = Theme.Font(pt * Math.Min(1.4f, u / Sf(48)));
                TextRenderer.DrawText(g, k.Label, f, Rectangle.Round(r), tc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragButton = e.Button;
        _lastKey = HitTest(e.Location);
        if (_lastKey >= 0) KeyPressed?.Invoke(_lastKey, e.Button);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.None && _dragButton != MouseButtons.None)
        {
            int k = HitTest(e.Location);
            if (k >= 0 && k != _lastKey) { _lastKey = k; KeyPressed?.Invoke(k, _dragButton); }
        }
        Cursor = HitTest(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { _dragButton = MouseButtons.None; _lastKey = -1; base.OnMouseUp(e); }
}
