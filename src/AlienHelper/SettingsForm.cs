using System.Drawing;
using AlienHelper.UI;
using static AlienHelper.UI.Theme;

namespace AlienHelper;

/// <summary>Settings: overlay, start-up behaviour, language, about.</summary>
public sealed class SettingsForm : Form
{
    readonly Controller _c;
    readonly OverlayManager _overlay;
    readonly PluginManager _plugins;
    readonly Action<string> _setLanguage;
    readonly Func<IReadOnlyList<int>> _applyHotkeys;
    readonly Action _collectReport;
    readonly Label _hkStatus = new() { AutoSize = false, ForeColor = Warm, Font = Theme.Font(8.5f) };
    readonly List<PillButton> _modePills = new();
    readonly List<PillButton> _stylePills = new();

    const int W = 392, M = 12, Pad = 14, Gap = 8;
    int Inner => S(W - 2 * M - 2 * Pad);
    int CardW => S(W - 2 * M);

    public SettingsForm(Controller c, OverlayManager overlay, PluginManager plugins, Action<string> setLanguage, Action showAbout,
        Func<IReadOnlyList<int>> applyHotkeys, Action collectReport)
    {
        _c = c; _overlay = overlay; _plugins = plugins; _setLanguage = setLanguage; _applyHotkeys = applyHotkeys; _collectReport = collectReport;
        Text = Log.AppTitle + " — " + L.T("settings");
        Style(this);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // two columns so everything fits without scrolling: window and monitoring on the left, keys, start-up and help on the right
        int left = S(M);
        left = BuildCards(left);
        left = BuildPoll(left);
        left = BuildOverlay(left);
        left = BuildLanguage(left);
        _x = S(M) + CardW + S(Gap);
        int right = S(M);
        right = BuildHotkeys(right);
        right = BuildStartup(right);
        right = BuildDiagnostics(right);
        right = BuildPlugins(right);
        int y = Math.Max(left, right);
        int width = S(2 * M) + 2 * CardW + S(Gap);

        var footer = new Label
        {
            Text = L.T("disclaimer.short") + "  ·  " + L.T("about"), ForeColor = Muted, Font = Theme.Font(8f),
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            Bounds = new Rectangle(S(M), y - S(2), width - S(2 * M), S(20)),
        };
        footer.Click += (_, _) => showAbout();
        Controls.Add(footer);
        // still taller than the screen (large text scaling, small display): cap the height and scroll
        int content = y + S(20) + S(M - Gap), maxClient = Screen.PrimaryScreen!.WorkingArea.Height - S(60);
        if (content > maxClient)
        {
            AutoScroll = true;
            AutoScrollMinSize = new Size(0, content);
            ClientSize = new Size(width + SystemInformation.VerticalScrollBarWidth, maxClient);
        }
        else ClientSize = new Size(width, content);

        UpdateOverlayLabel();
        _overlay.Changed += OnOverlayChanged;
        FormClosed += (_, _) => _overlay.Changed -= OnOverlayChanged;
        OnOverlayChanged();
    }

    /// <summary>Left edge of the column being built.</summary>
    int _x = S(M);

    Card NewCard(string caption, int y, out int cy)
    {
        var card = new Card { Caption = caption };
        card.Location = new Point(_x, y);
        cy = S(36);
        Controls.Add(card);
        return card;
    }

    int Finish(Card card, int cy)
    {
        card.Size = new Size(CardW, cy + S(6));
        return card.Bottom + S(Gap);
    }

    Toggle AddToggle(Card card, ref int cy, Toggle t)
    {
        t.SetBounds(S(Pad), cy, Inner, S(26));
        card.Controls.Add(t);
        cy += S(28);
        return t;
    }

    /// <summary>Called after the theme or the numbers-only mode changed: windows are rebuilt.</summary>
    public Action? LookChanged { get; set; }

    /// <summary>Called after a main window section was turned on or off.</summary>
    public Action? CardsChanged { get; set; }

    /// <summary>Which main window sections are shown; the last switch that is on cannot be turned off.</summary>
    int BuildCards(int y)
    {
        var card = NewCard(L.T("card.cards"), y, out int cy);
        var items = new List<(string id, string text)> { ("cpu", "CPU"), ("gpu", "GPU"), ("ssd", "SSD"), ("net", L.T("card.net")), ("power", L.T("card.power")) };
        if (_c.Awcc != null) items.Add(("mode", L.T("card.mode")));
        if (_c.Screen != null) items.Add(("screen", L.T("card.screen")));
        if (_c.Keyboard != null || _c.Chassis != null) items.Add(("lights", L.T("card.lights")));
        var toggles = new List<(string id, Toggle t)>();
        void Guard()
        {
            var on = toggles.Where(x => x.t.On).ToList();
            foreach (var (_, t) in toggles) t.Enabled = !(on.Count == 1 && t.On);
        }
        for (int i = 0; i < items.Count; i++)
        {
            var (id, text) = items[i];
            var t = new Toggle(text) { On = !_c.S.HiddenCards.Contains(id) };
            t.SetBounds(S(Pad), cy + i * S(28), Inner, S(26));
            t.Toggled += (_, _) =>
            {
                if (!t.On && toggles.All(x => !x.t.On)) { t.On = true; return; }   // one section always stays
                _c.S.HiddenCards.Remove(id);
                if (!t.On) _c.S.HiddenCards.Add(id);
                _c.S.Save();
                Guard();
                CardsChanged?.Invoke();
            };
            toggles.Add((id, t));
            card.Controls.Add(t);
        }
        Guard();
        cy += items.Count * S(28) + S(2);
        var numbers = new Toggle(L.T("opt.numbers")) { On = _c.S.NumbersOnly };
        numbers.SetBounds(S(Pad), cy, Inner, S(26));
        numbers.Toggled += (_, _) => { _c.S.NumbersOnly = numbers.On; _c.S.Save(); BeginInvoke(() => LookChanged?.Invoke()); };
        card.Controls.Add(numbers);
        cy += S(30);
        var cardsHint = new Label { Text = L.T("card.cards.hint"), ForeColor = Muted, Font = Theme.Font(8f), AutoSize = false };
        // two lines when a wide font (a theme's) does not fit it on one
        int hintH = Math.Max(S(18), TextRenderer.MeasureText(cardsHint.Text, cardsHint.Font, new Size(Inner, 0), TextFormatFlags.WordBreak).Height + S(2));
        cardsHint.Bounds = new Rectangle(S(Pad), cy, Inner, hintH);
        card.Controls.Add(cardsHint);
        cy += hintH + S(2);
        return Finish(card, cy);
    }

    /// <summary>Called with the new refresh period in ms when the slider is released.</summary>
    public Action<int>? IntervalChanged { get; set; }

    /// <summary>Metrics refresh: 0.1 … 10 s in 0.1 s steps.</summary>
    int BuildPoll(int y)
    {
        var card = NewCard(L.T("opt.poll"), y, out int cy);
        const int step = 100;
        static string Text(int ms) => $"{ms / 1000.0:0.0} {L.T("u.sec")}";
        int now = Math.Clamp(_c.S.PollIntervalMs, SensorMonitor.MinIntervalMs, SensorMonitor.MaxIntervalMs);
        card.Hint = Text(now);
        var slider = new Slider { Minimum = SensorMonitor.MinIntervalMs / step, Maximum = SensorMonitor.MaxIntervalMs / step, Value = now / step };
        slider.SetBounds(S(Pad - 10), cy, Inner + S(20), S(24));   // the slider keeps 10 px of its own padding
        slider.ValueChanged += (_, _) => { card.Hint = Text(slider.Value * step); card.Invalidate(); };
        slider.ValueCommitted += (_, _) =>
        {
            int ms = slider.Value * step;
            if (ms == _c.S.PollIntervalMs) return;
            _c.S.PollIntervalMs = ms;
            _c.S.Save();
            IntervalChanged?.Invoke(ms);
        };
        card.Controls.Add(slider);
        cy += S(26);
        var hint = new Label { Text = L.T("opt.poll.hint"), ForeColor = Muted, Font = Theme.Font(8f), AutoSize = false };
        hint.Bounds = new Rectangle(S(Pad), cy, Inner, TextRenderer.MeasureText(hint.Text, hint.Font, new Size(Inner, 0), TextFormatFlags.WordBreak).Height + S(2));
        card.Controls.Add(hint);
        cy = hint.Bottom + S(2);
        return Finish(card, cy);
    }

    int BuildOverlay(int y)
    {
        var card = NewCard(L.T("btn.overlay"), y, out int cy);
        // display mode: off / always / only in games
        var modeLbl = new Label { Text = L.T("ov.mode"), ForeColor = Muted, Font = Theme.Font(9f), AutoSize = true, Location = new Point(S(Pad), cy + S(4)) };
        card.Controls.Add(modeLbl);
        string[] modes = { L.T("ov.mode.off"), L.T("ov.mode.on"), L.T("ov.mode.games") };
        int mgap = S(4), mx = S(Pad) + Math.Max(modeLbl.PreferredWidth, TextRenderer.MeasureText(L.T("ov.style"), Theme.Font(9f)).Width) + S(10);
        int mw = (S(Pad) + Inner - mx - mgap * 2) / 3;
        for (int i = 0; i < 3; i++)
        {
            int mi = i;
            var b = new PillButton(modes[i]) { Bounds = new Rectangle(mx + i * (mw + mgap), cy, mw, S(26)) };
            b.Click += (_, _) => _overlay.Mode = mi;
            _modePills.Add(b);
            card.Controls.Add(b);
        }
        cy += S(32);

        var styleLbl = new Label { Text = L.T("ov.style"), ForeColor = Muted, Font = Theme.Font(9f), AutoSize = true, Location = new Point(S(Pad), cy + S(4)) };
        new ToolTip().SetToolTip(styleLbl, L.T("ov.move"));
        card.Controls.Add(styleLbl);
        int sgap = S(4), sx = S(Pad) + Math.Max(styleLbl.PreferredWidth, TextRenderer.MeasureText(L.T("ov.mode"), Theme.Font(9f)).Width) + S(10);
        int sw = (S(Pad) + Inner - sx - sgap * (OverlayManager.StyleCount - 1)) / OverlayManager.StyleCount;
        for (int i = 0; i < OverlayManager.StyleCount; i++)
        {
            int si = i;
            var b = new PillButton(L.T($"ov.s{i}")) { Bounds = new Rectangle(sx + i * (sw + sgap), cy, sw, S(26)) };
            new ToolTip().SetToolTip(b, L.T("ov.move"));
            b.Click += (_, _) => _overlay.Style = si;
            _stylePills.Add(b);
            card.Controls.Add(b);
        }
        cy += S(30);
        card.Controls.Add(new Label
        {
            Text = L.T("ov.move"), ForeColor = Muted, Font = Theme.Font(8f), AutoSize = false,
            Bounds = new Rectangle(S(Pad), cy, Inner, S(18)),
        });
        cy += S(20);
        return Finish(card, cy);
    }

    int BuildHotkeys(int y)
    {
        var card = NewCard(L.T("hk.title"), y, out int cy);
        HotkeyBox Row(string label, int value, Action<int> save)
        {
            card.Controls.Add(new Label { Text = label, ForeColor = Fg, Font = Theme.Font(9.5f), AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(S(Pad), cy, Inner - S(160), S(28)) });
            var box = new HotkeyBox { Value = value, EmptyText = L.T("hk.none"), PromptText = L.T("hk.press"),
                Bounds = new Rectangle(S(Pad) + Inner - S(150), cy, S(150), S(28)) };
            box.ValueChanged += (_, _) => { save(box.Value); _c.S.Save(); ReRegister(); };
            card.Controls.Add(box);
            cy += S(34);
            return box;
        }
        Row(L.T("hk.overlay"), _c.S.OverlayHotkey, v => _c.S.OverlayHotkey = v);
        Row(L.T("hk.style"), _c.S.OverlayStyleHotkey, v => _c.S.OverlayStyleHotkey = v);
        if (_c.Awcc != null) Row(L.T("hk.mode"), _c.S.ModeHotkey, v => _c.S.ModeHotkey = v);
        if (_c.Awcc != null) Row(L.T("hk.oc"), _c.S.OcHotkey, v => _c.S.OcHotkey = v);
        card.Controls.Add(new Label { Text = L.T("hk.hint"), ForeColor = Muted, Font = Theme.Font(8f), AutoSize = false,
            Bounds = new Rectangle(S(Pad), cy, Inner, S(32)) });
        cy += S(32);
        _hkStatus.Bounds = new Rectangle(S(Pad), cy, Inner, S(18));
        card.Controls.Add(_hkStatus);
        cy += S(18);
        return Finish(card, cy);
    }

    /// <summary>Applies the hotkeys right away and says which combination Windows refused.</summary>
    void ReRegister()
    {
        var busy = _applyHotkeys().Select(HotKey.Describe).ToList();
        _hkStatus.Text = busy.Count > 0 ? L.T("hk.busy", string.Join(", ", busy)) : "";
        UpdateOverlayLabel();
    }

    void UpdateOverlayLabel()
    {
        string hk = HotKey.Describe(_c.S.OverlayHotkey);
        if (_modePills.Count > 1) { _modePills[1].Text = L.T("ov.mode.on"); new ToolTip().SetToolTip(_modePills[1], hk); }
    }

    int BuildStartup(int y)
    {
        var card = NewCard(L.T("settings.start"), y, out int cy);
        var apply = AddToggle(card, ref cy, new Toggle(L.T("opt.apply")) { On = _c.S.ApplyOnStart });
        apply.Toggled += (_, _) => { _c.S.ApplyOnStart = apply.On; _c.S.Save(); };
        var minimized = AddToggle(card, ref cy, new Toggle(L.T("opt.minimized")) { On = _c.S.StartMinimized });
        minimized.Toggled += (_, _) => { _c.S.StartMinimized = minimized.On; _c.S.Save(); };
        // the AWCC options exist only on Dell / Alienware laptops; elsewhere AWCC is not there to interfere
        bool dell = Hardware.SystemInfo.IsDell;
        if (dell && (_c.Keyboard != null || _c.Chassis != null))
        {
            var guard = AddToggle(card, ref cy, new Toggle(L.T("opt.guardlights")) { On = _c.S.GuardLights });
            guard.Toggled += (_, _) => { _c.S.GuardLights = guard.On; _c.S.Save(); if (guard.On) Task.Run(_c.EnforceLights); };
            new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 }.SetToolTip(guard, L.T("opt.guardlights.tip"));
        }
        if (dell && AwccService.Installed)
        {
            var awcc = AddToggle(card, ref cy, new Toggle(L.T("opt.awccoff")) { Enabled = !Launcher.IsLimited, On = AwccService.IsOff });
            new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 }.SetToolTip(awcc, L.T("opt.awccoff.tip"));
            awcc.Toggled += async (_, _) =>
            {
                bool off = awcc.On;
                if (off && MessageBox.Show(this, L.T("awccoff.confirm"), Log.AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    awcc.On = false;
                    return;
                }
                bool ok = await Task.Run(() => off ? AwccService.TurnOff(_c.S) : AwccService.TurnOn(_c.S));
                awcc.On = await Task.Run(() => AwccService.IsOff);
                if (!ok) MessageBox.Show(this, L.T("err.awcc"), Log.AppTitle);
            };
        }
        if (dell && _c.Awcc?.Profiles.Contains(Hardware.Awcc.ProfileGMode) == true)
        {
            var hold = AddToggle(card, ref cy, new Toggle(L.T("opt.holdgpu")) { On = _c.S.HoldGpuPower });
            hold.Toggled += (_, _) => { _c.S.HoldGpuPower = hold.On; _c.S.Save(); };
            new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 }.SetToolTip(hold, L.T("opt.holdgpu.tip"));
        }
        if (Autostart.Supported)
        {
            // starts at sign-in through the Run key; Windows then shows the UAC prompt
            var startup = AddToggle(card, ref cy, new Toggle(L.T("opt.autostart")) { On = Autostart.IsEnabled() });
            startup.Toggled += async (_, _) =>
            {
                bool want = startup.On;
                bool ok = await Task.Run(() => want ? Autostart.Enable() : Autostart.Disable());
                startup.On = await Task.Run(Autostart.IsEnabled);
                if (!ok) MessageBox.Show(this, L.T("err.task"), Log.AppTitle);
            };
        }
        else
        {
            // MSIX: the package declares a startup task (off by default); Windows turns it on in Startup apps
            var open = new PillButton(L.T("opt.autostart.windows")) { Bounds = new Rectangle(S(Pad), cy, Inner, S(28)) };
            open.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
                catch (Exception ex) { Log.Warn("open startup apps: " + ex.Message); }
            };
            card.Controls.Add(open);
            cy += S(32);
        }
        return Finish(card, cy);
    }

    int BuildLanguage(int y)
    {
        var card = new Card();
        card.Location = new Point(_x, y);
        Controls.Add(card);
        int cy = S(10);
        card.Controls.Add(new Label { Text = L.T("opt.lang"), ForeColor = Muted, Font = Theme.Font(9f), AutoSize = true, Location = new Point(S(Pad), cy + S(4)) });
        string current = L.Languages.First(l => l.code == L.Current).name;
        var langBtn = new PillButton(current + "   ▾") { Height = S(26), Width = S(140) };
        langBtn.Location = new Point(S(Pad) + Inner - langBtn.Width, cy);
        var langMenu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        foreach (var (code, name) in L.Languages)
        {
            string lang = code;
            langMenu.Items.Add(new ToolStripMenuItem(name, null, (_, _) => { if (L.Current != lang) BeginInvoke(() => _setLanguage(lang)); })
                { Checked = L.Current == code, ForeColor = Fg });
        }
        langBtn.Click += (_, _) => langMenu.Show(langBtn, new Point(0, langBtn.Height + S(2)));
        card.Controls.Add(langBtn);
        cy += S(26) + S(8);

        card.Controls.Add(new Label { Text = L.T("opt.theme"), ForeColor = Muted, Font = Theme.Font(9f), AutoSize = true, Location = new Point(S(Pad), cy + S(4)) });
        string currentTitle = Theme.Current.IsFile ? Theme.Current.Title : L.T("theme." + Theme.Current.Key);
        var themeBtn = new PillButton(currentTitle + "   ▾") { Height = S(26), Width = S(160) };
        themeBtn.Location = new Point(S(Pad) + Inner - themeBtn.Width, cy);
        var themeMenu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
        void Pick(string key)
        {
            if (_c.S.Look == key) return;
            _c.S.Look = key;
            _c.S.Save();
            BeginInvoke(() => LookChanged?.Invoke());
        }
        ToolStripMenuItem Item(string text, EventHandler click, bool check = false) => new(text, null, click) { Checked = check, ForeColor = Fg };
        // the menu is filled on every open, so files put into the themes folder show up without a restart
        themeMenu.Opening += (_, _) =>
        {
            themeMenu.Items.Clear();
            foreach (var look in Enum.GetValues<Theme.Look>())
            {
                string key = Theme.Name(look);
                themeMenu.Items.Add(Item(L.T("theme." + key), (_, _) => Pick(key), _c.S.Look == key));
            }
            var files = ThemeFiles.List();
            if (files.Count > 0) themeMenu.Items.Add(new ToolStripSeparator());
            foreach (var f in files)
            {
                string key = ThemeFiles.Prefix + f.Name;
                themeMenu.Items.Add(Item(f.Title, (_, _) => Pick(key), _c.S.Look == key));
            }
            themeMenu.Items.Add(new ToolStripSeparator());
            themeMenu.Items.Add(Item(L.T("theme.add"), (_, _) => BeginInvoke(ImportTheme)));
            themeMenu.Items.Add(Item(L.T("theme.folder"), (_, _) =>
            {
                try { Directory.CreateDirectory(ThemeFiles.Dir); System.Diagnostics.Process.Start("explorer.exe", ThemeFiles.Dir); }
                catch (Exception ex) { Log.Warn("themes folder: " + ex.Message); }
            }));
            themeMenu.Items.Add(Item(L.T("theme.export"), (_, _) => BeginInvoke(ExportTheme)));
        };
        themeMenu.Items.Add(new ToolStripMenuItem("…"));   // a menu without items does not open
        themeBtn.Click += (_, _) => themeMenu.Show(themeBtn, new Point(0, themeBtn.Height + S(2)));
        card.Controls.Add(themeBtn);
        cy += S(26) + S(6);
        return Finish(card, cy);
    }

    /// <summary>Settings → Theme → Add theme: checks a .json theme, copies it into the themes folder and switches to it.</summary>
    void ImportTheme()
    {
        using var dlg = new OpenFileDialog { Filter = L.T("theme.filter") + " (*.json)|*.json", Title = L.T("theme.add") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var key = ThemeFiles.Import(dlg.FileName, out var message);
        if (key == null) { MessageBox.Show(this, L.T("theme.err", message), Log.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (message.Length > 0) MessageBox.Show(this, L.T("theme.warn", message), Log.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        _c.S.Look = key;
        _c.S.Save();
        LookChanged?.Invoke();
    }

    /// <summary>Settings → Theme → Save as file: the current theme as a template to change and import.</summary>
    void ExportTheme()
    {
        using var dlg = new SaveFileDialog { Filter = L.T("theme.filter") + " (*.json)|*.json", FileName = "my-theme.json", Title = L.T("theme.export") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { ThemeFiles.Export(dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(this, L.T("theme.err", ex.Message), Log.AppTitle); }
    }

    int BuildDiagnostics(int y)
    {
        var card = NewCard(L.T("diag.title"), y, out int cy);
        var hint = new Label { Text = L.T("diag.hint"), ForeColor = Muted, Font = Theme.Font(8.5f), AutoSize = false };
        hint.Bounds = new Rectangle(S(Pad), cy, Inner, TextRenderer.MeasureText(hint.Text, hint.Font, new Size(Inner, 0), TextFormatFlags.WordBreak).Height + S(4));
        card.Controls.Add(hint);
        cy = hint.Bottom + S(8);
        int x = S(Pad), bw = (Inner - 2 * S(6)) / 3;
        foreach (var (text, act) in new (string, Action)[]
        {
            (L.T("diag.collect"), _collectReport),
            (L.T("diag.mail"), Diagnostics.MailDeveloper),
            (L.T("diag.logs"), () => { Directory.CreateDirectory(Log.LogDir); System.Diagnostics.Process.Start("explorer.exe", Log.LogDir); }),
        })
        {
            var b = new PillButton(text) { Bounds = new Rectangle(x, cy, bw, S(28)) };
            b.Click += (_, _) => act();
            card.Controls.Add(b);
            x += b.Width + S(6);
        }
        cy += S(28) + S(6);
        return Finish(card, cy);
    }

    /// <summary>Plugins in the plugins folder: a switch each, with what it is and how it is doing.</summary>
    int BuildPlugins(int y)
    {
        var card = NewCard(L.T("plugins.title"), y, out int cy);
        var font = Theme.Font(8.5f);
        int Lines(string text, int width) => TextRenderer.MeasureText(text, font, new Size(width, 0), TextFormatFlags.WordBreak).Height + S(2);
        Label Note(string text, ref int cy, Color color)
        {
            var l = new Label { Text = text, ForeColor = color, Font = font, AutoSize = false };
            l.Bounds = new Rectangle(S(Pad), cy, Inner, Lines(text, Inner));
            card.Controls.Add(l);
            cy = l.Bottom + S(6);
            return l;
        }
        var refresh = new List<Action>();
        if (_plugins.Plugins.Count == 0) Note(L.T("plugins.none"), ref cy, Muted);
        foreach (var p in _plugins.Plugins)
        {
            var t = AddToggle(card, ref cy, new Toggle(p.Title) { On = _plugins.IsEnabled(p) });
            var open = SmallButtonFor(L.T("plugins.settings"));
            open.Location = new Point(S(Pad) + Inner - open.Width, cy);
            open.Click += (_, _) => _plugins.OpenSettings(p, this);
            card.Controls.Add(open);
            // description and author, then the state; room for two lines of each
            string about = string.Join(" · ", new[] { p.Manifest?.Description, p.Manifest?.Author }.Where(s => !string.IsNullOrWhiteSpace(s)));
            int w = Inner - open.Width - S(6);
            var info = new Label { Font = font, AutoSize = false, Bounds = new Rectangle(S(Pad), cy, w, Lines("1\n2\n3", w)) };
            card.Controls.Add(info);
            cy = info.Bottom + S(6);
            void Show()
            {
                string state = p.Problem ?? (p.Running ? L.T("plugins.running") : p.Unloading ? L.T("plugins.restart") : L.T("plugins.off"));
                info.Text = about.Length > 0 ? about + "\n" + state : state;
                info.ForeColor = p.Problem != null ? Warn : Muted;
                open.Visible = p.Running;
                t.Enabled = p.Problem == null || t.On;   // a broken plugin can only be turned off
            }
            t.Toggled += (_, _) => { _plugins.SetEnabled(p, t.On); Show(); };
            refresh.Add(Show);
            Show();
        }
        void OnChanged() { if (!IsDisposed) foreach (var r in refresh) r(); }
        _plugins.Changed += OnChanged;
        FormClosed += (_, _) => _plugins.Changed -= OnChanged;
        Note(L.T("plugins.hint"), ref cy, Muted);
        var path = Note(PluginManager.Dir, ref cy, Blend(Muted, Fg, 0.5f));
        path.Top -= S(4);
        cy = path.Bottom + S(4);
        int x = S(Pad), bw = (Inner - S(6)) / 2;
        foreach (var (text, act) in new (string, Action)[]
        {
            (L.T("plugins.folder"), () =>
            {
                try { Directory.CreateDirectory(PluginManager.Dir); } catch { }   // under Program Files: needs admin rights
                if (Directory.Exists(PluginManager.Dir)) System.Diagnostics.Process.Start("explorer.exe", PluginManager.Dir);
            }),
            (L.T("plugins.docs"), () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.RepoUrl + "/blob/test/docs/plugins.md") { UseShellExecute = true })),
        })
        {
            var b = new PillButton(text) { Bounds = new Rectangle(x, cy, bw, S(28)) };
            b.Click += (_, _) => act();
            card.Controls.Add(b);
            x += b.Width + S(6);
        }
        cy += S(28) + S(6);
        return Finish(card, cy);
    }

    static PillButton SmallButtonFor(string text)
    {
        var b = new PillButton(text) { Height = S(24) };
        b.Width = TextRenderer.MeasureText(text, Theme.Font(9.5f)).Width + S(22);
        return b;
    }

    void OnOverlayChanged()
    {
        if (IsDisposed) return;
        for (int i = 0; i < _modePills.Count; i++) _modePills[i].Active = _overlay.Mode == i;
        for (int i = 0; i < _stylePills.Count; i++) _stylePills[i].Active = _overlay.Style == i;
    }
}
