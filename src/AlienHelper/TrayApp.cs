using System.Drawing;
using System.Drawing.Drawing2D;
using AlienHelper.UI;
using Microsoft.Win32;

namespace AlienHelper;

public sealed class TrayApp : ApplicationContext
{
    readonly Controller _c;
    readonly SensorMonitor _mon;
    readonly NotifyIcon _tray;
    readonly OverlayManager _overlay;
    HotKey? _overlayKey, _modeKey, _styleKey, _ocKey;
    ModeOsd? _osd;
    readonly Control _invoker = new();
    readonly EventWaitHandle _show;
    readonly RegisteredWaitHandle _showWait;
    readonly EventWaitHandle _exit;
    readonly RegisteredWaitHandle _exitWait;
    MainForm? _form;
    ChartsForm? _charts;

    public TrayApp(bool showWindow)
    {
        _c = new Controller(Settings.Load());
        Theme.Apply(_c.S.Look);
        Theme.NumbersOnly = _c.S.NumbersOnly;
        _c.ApplySaved();
        // Dell's AlienFXSubAgent repaints its own lighting shortly after logon; repeat ours once it is done
        if (_c.S.ApplyOnStart && _c.S.LightsEnabled)
            Task.Run(async () =>
            {
                foreach (int sec in new[] { 8, 25 })
                {
                    await Task.Delay(TimeSpan.FromSeconds(sec));
                    try { _c.ApplyLights(); } catch (Exception ex) { Log.Warn("reapply lights: " + ex.Message); }
                }
            });
        Log.Info($"start: model='{Hardware.SystemInfo.Model}' awcc={_c.Awcc != null} lights={_c.Lights.Count} screen={_c.Screen != null} profile=0x{_c.Awcc?.GetProfile() ?? -1:X2}");
        _mon = new SensorMonitor(_c);
        _mon.Want(MainScreen, AlienHelper.MainForm.NeedFor(_c));   // main window metrics: always, whether the window is open or not
        _curves = new FanCurveDriver(_c, _mon);                      // Custom fan curves (idle while Custom is off)
        _c.CustomChanged += OnCustomChanged;
        _oc = new GpuOverclock(_c);
        _c.OcChanged += _oc.Sync;
        _oc.Sync();   // after a crash the original GPU offsets come back here; with Custom + Overclock it is reapplied
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _overlay = new OverlayManager(_c, _mon);
        ApplyHotkeys();

        // a second (possibly non-elevated) launch signals this event to bring the window up
        _ = _invoker.Handle;
        _show = ShowSignal.Create();
        _showWait = ThreadPool.RegisterWaitForSingleObject(_show,
            (_, _) => { try { _invoker.BeginInvoke(() => ToggleWindow(true)); } catch { } }, null, -1, false);
        _exit = ShowSignal.Create(ShowSignal.ExitName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exit,
            (_, _) => { try { _invoker.BeginInvoke(ExitThread); } catch { } }, null, -1, true);
        Task.Run(Autostart.Refresh);   // keep the Run entry pointing at this exe; drop elevated tasks of older builds
        _invoker.BeginInvoke(CheckHardware);   // after the message loop starts
        GuardLightsAfterWake("start");          // the agent also repaints right after sign-in

        _tray = new NotifyIcon { Icon = MakeIcon(), Visible = true, Text = Log.AppTitle };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleWindow(); };
        // nothing is polled in the background: the tooltip asks for one fresh reading when the mouse is over the icon
        _tray.MouseMove += (_, _) =>
        {
            long now = SensorMonitor.Clock.ElapsedMilliseconds;
            if (now - _tipAt < 2000) return;
            _tipAt = now;
            _mon.Once(Need.Fans);
        };
        _tray.ContextMenuStrip = new ContextMenuStrip { Renderer = new Theme.MenuRenderer(), ShowImageMargin = true };
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        BuildMenu();
        _plugins = new PluginManager(_c.S, _invoker, (title, text) => _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.None));
        _plugins.Start();

        _mon.Updated += UpdateTooltip;
        SystemEvents.PowerModeChanged += OnPowerMode;
        if (showWindow && !_c.S.StartMinimized) ToggleWindow();   // "start minimized": tray only
    }

    void OnPowerMode(object? s, PowerModeChangedEventArgs e)
    {
        // Lighting controllers reset to firmware defaults after sleep; reapply our colors.
        if (e.Mode == PowerModes.Resume)
        {
            Task.Run(async () => { await Task.Delay(3000); _c.ApplyLights(); _oc.Sync(); });
            GuardModeAfterWake("resume");
            GuardLightsAfterWake("resume");
        }
    }

    void OnSessionSwitch(object? s, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.SessionLogon)
        {
            GuardModeAfterWake("unlock");
            GuardLightsAfterWake("unlock");
        }
    }

    void BuildMenu()
    {
        var m = _tray.ContextMenuStrip!;
        m.ShowItemToolTips = true;
        m.Items.Clear();
        var a = _c.Awcc;
        if (a != null)
        {
            int cur = _c.CurrentMode(_c.Awcc?.GetProfile() ?? -1);   // read now: nothing is polled while the window is hidden
            foreach (var p in a.Profiles.Select(x => (int)x).Append(Controller.CustomMode))
            {
                int pp = p;
                var item = new ToolStripMenuItem(ModeName(p)) { Checked = cur == p, ToolTipText = p == Controller.CustomMode ? L.T("boost.custom.tip") : Controller.ModeTip((byte)p) };
                item.Click += async (_, _) => { await Task.Run(() => _c.SelectMode(pp)); await _mon.Poll(); };
                m.Items.Add(item);
            }
            m.Items.Add(new ToolStripSeparator());
            var boost = new ToolStripMenuItem(L.T("tray.boost"));
            foreach (int v in new[] { 0, 25, 50, 75, 100 })
            {
                int vv = v;
                bool manual = cur != Controller.CustomMode && cur != Hardware.Awcc.ProfileGMode;   // Custom: curves, Alien Mode: 100 %
                var it = new ToolStripMenuItem(v == 0 ? L.T("tray.autobios") : $"+{v}%") { Checked = manual && _c.S.FanBoost == v, Enabled = manual };
                it.Click += (_, _) => Task.Run(() => _c.SetBoost(vv));
                boost.DropDownItems.Add(it);
            }
            boost.DropDownItems.Add(new ToolStripSeparator());
            boost.DropDownItems.Add(new ToolStripMenuItem(L.T("curves.btn"), null, (_, _) => OpenCurves()));
            var oc = new ToolStripMenuItem("Overclock") { Checked = _c.S.GpuOcOn, Enabled = cur == Controller.CustomMode, ToolTipText = L.T("oc.tip", L.T("oc." + GpuOverclock.Preset(_c.S.GpuOc).id), HotKey.Describe(_c.S.OcHotkey)) };
            oc.Click += (_, _) => { GpuOverclock.Toggle(_c, !_c.S.GpuOcOn, _form is { IsDisposed: false, Visible: true } ? _form : null); if (_form is { IsDisposed: false }) _form.RefreshBoost(); };
            boost.DropDownItems.Add(oc);
            m.Items.Add(boost);
        }
        else m.Items.Add(new ToolStripMenuItem(L.T("tray.noawcc")) { Enabled = false });

        if (_c.Lights.Count > 0)
        {
            var light = new ToolStripMenuItem(L.T("card.lights")) { Checked = _c.S.LightsEnabled };
            light.Click += (_, _) => { bool on = !_c.S.LightsEnabled; Task.Run(() => { if (on) _c.LightsOn(); else _c.LightsOff(); }); };
            m.Items.Add(light);
        }
        m.Items.Add(new ToolStripSeparator());
        var ov = new ToolStripMenuItem(L.T("btn.overlay")) { Checked = _overlay.Visible };
        // display mode: one of three
        ov.DropDownItems.Add(new ToolStripMenuItem(L.T("ov.mode")) { Enabled = false });
        string[] modes = { L.T("ov.mode.off"), L.T("ov.mode.on") + (HotKey.Describe(_c.S.OverlayHotkey) is { Length: > 0 } hk ? $"  ({hk})" : ""), L.T("ov.mode.games") };
        for (int i = 0; i < modes.Length; i++)
        {
            int mi = i;
            ov.DropDownItems.Add(new ToolStripMenuItem("   " + modes[i], null, (_, _) => _overlay.Mode = mi) { Checked = _overlay.Mode == i });
        }
        ov.DropDownItems.Add(new ToolStripSeparator());
        string[] corners = { L.T("corner.0"), L.T("corner.1"), L.T("corner.2"), L.T("corner.3") };
        for (int i = 0; i < corners.Length; i++)
        {
            int ci = i;
            ov.DropDownItems.Add(new ToolStripMenuItem(corners[i], null, (_, _) => _overlay.SetCorner(ci))
                { Checked = _c.S.OverlayX == null && _c.S.OverlayCorner == i });
        }
        ov.DropDownItems.Add(new ToolStripMenuItem(L.T("ov.move")) { Enabled = false });
        ov.DropDownItems.Add(new ToolStripSeparator());
        ov.DropDownItems.Add(new ToolStripMenuItem(L.T("ov.style")) { Enabled = false });
        for (int i = 0; i < OverlayManager.StyleCount; i++)
        {
            int si = i;
            ov.DropDownItems.Add(new ToolStripMenuItem("   " + L.T($"ov.s{i}"), null, (_, _) => _overlay.Style = si) { Checked = _overlay.Style == i });
        }
        // the detector got the app in front wrong: settle it by hand (a second click hands it back to the detector)
        if (GameDetector.LastApp is { } app)
        {
            bool? said = GameDetector.UserSays(app);
            ov.DropDownItems.Add(new ToolStripSeparator());
            ov.DropDownItems.Add(new ToolStripMenuItem(L.T("ov.app", app)) { Enabled = false });
            ov.DropDownItems.Add(new ToolStripMenuItem("   " + L.T("ov.app.game"), null, (_, _) => GameDetector.Mark(app, said == true ? null : true)) { Checked = said == true });
            ov.DropDownItems.Add(new ToolStripMenuItem("   " + L.T("ov.app.notgame"), null, (_, _) => GameDetector.Mark(app, said == false ? null : false)) { Checked = said == false });
        }
        foreach (ToolStripItem i in ov.DropDownItems) i.ForeColor = Theme.Fg;
        m.Items.Add(ov);
        if (_plugins != null) foreach (var pi in _plugins.MenuItems(p => _plugins.OpenSettings(p, null))) m.Items.Add(pi);
        var lang = new ToolStripMenuItem(L.T("opt.lang"));
        foreach (var (code, name) in L.Languages)
        {
            string lc = code;
            lang.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => SetLanguage(lc)) { Checked = L.Current == code, ForeColor = Theme.Fg });
        }
        m.Items.Add(lang);
        if (Launcher.IsLimited) m.Items.Add(L.T("limited.restart"), null, (_, _) => RestartElevated());
        m.Items.Add(L.T("btn.charts"), null, (_, _) => OpenCharts());
        m.Items.Add(L.T("tray.open"), null, (_, _) => ToggleWindow(true));
        m.Items.Add(L.T("settings"), null, (_, _) => OpenSettings());
        m.Items.Add(L.T("diag.collect"), null, (_, _) => CollectReport());
        m.Items.Add(L.T("about"), null, (_, _) => ShowAbout());
        m.Items.Add(L.T("tray.exit"), null, (_, _) => ExitThread());
        foreach (ToolStripItem i in m.Items) i.ForeColor = Theme.Fg;
    }

    void ToggleWindow(bool forceShow = false)
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new MainForm(_c, _mon, OpenCharts) { OpenKeys = OpenKeys, OpenSettings = OpenSettings, OpenCurves = OpenCurves, RestartElevated = RestartElevated };
            _form.Show();
            _form.Activate();
            return;
        }
        if (_form.Visible && !forceShow) _form.Hide();
        else { _form.Show(); _form.WindowState = FormWindowState.Normal; _form.Activate(); }
    }

    /// <summary>Switches the UI language and rebuilds the open windows (they read strings at build time).</summary>
    void SetLanguage(string code)
    {
        L.Set(code);
        _plugins?.LanguageChanged();
        _c.S.Language = code;
        _c.S.Save();
        RebuildWindows();
    }

    /// <summary>Theme or numbers-only mode changed in Settings: apply it and rebuild the open windows.</summary>
    void ApplyLook()
    {
        Theme.Apply(_c.S.Look);
        Theme.NumbersOnly = _c.S.NumbersOnly;
        RebuildWindows();
        _tray.ContextMenuStrip!.Renderer = new Theme.MenuRenderer();
        _plugins?.ThemeChanged();
    }

    /// <summary>Recreates the open windows in place (language, theme or layout changed).</summary>
    void RebuildWindows()
    {
        bool formWasVisible = _form is { IsDisposed: false, Visible: true };
        var loc = _form?.Location;
        _form?.Dispose();
        _form = null;
        if (formWasVisible)
        {
            ToggleWindow(true);
            if (loc != null) _form!.Location = loc.Value;
        }
        if (_charts is { IsDisposed: false })
        {
            var bounds = _charts.Bounds;
            _charts.Dispose();
            _charts = null;
            OpenCharts();
            _charts!.Bounds = bounds;
        }
        if (_settings is { IsDisposed: false })
        {
            var settingsLoc = _settings.Location;
            _settings.Dispose();
            _settings = null;
            OpenSettings();
            _settings!.Location = settingsLoc;
        }
        if (_curvesForm is { IsDisposed: false })
        {
            var curvesLoc = _curvesForm.Location;
            _curvesForm.Dispose();
            _curvesForm = null;
            OpenCurves();
            _curvesForm!.Location = curvesLoc;
        }
        if (_keys is { IsDisposed: false })
        {
            var bounds = _keys.Bounds;
            _keys.Dispose();
            _keys = null;
            OpenKeys();
            _keys!.Bounds = bounds;
        }
        UpdateTooltip();
    }

    /// <summary>(Re)registers the configurable global hotkeys; returns the combinations Windows refused.</summary>
    public IReadOnlyList<int> ApplyHotkeys()
    {
        _overlayKey?.Dispose();
        _modeKey?.Dispose();
        _styleKey?.Dispose();
        _ocKey?.Dispose();
        _ocKey?.Dispose();
        _overlayKey = HotKey.FromCombo(_c.S.OverlayHotkey, () => _overlay.Toggle());
        _modeKey = _c.Awcc != null ? HotKey.FromCombo(_c.S.ModeHotkey, () => _ = CycleMode()) : null;
        _styleKey = HotKey.FromCombo(_c.S.OverlayStyleHotkey, CycleOverlayStyle);
        _ocKey = _c.Awcc != null ? HotKey.FromCombo(_c.S.OcHotkey, ToggleOverclock) : null;
        var busy = new List<int>();
        foreach (var (key, combo) in new[] { (_overlayKey, _c.S.OverlayHotkey), (_modeKey, _c.S.ModeHotkey), (_styleKey, _c.S.OverlayStyleHotkey), (_ocKey, _c.S.OcHotkey) })
            if (key is { Registered: false })
            {
                busy.Add(combo);
                Log.Warn($"hotkey busy: {HotKey.Describe(combo)}");
            }
        return busy;
    }

    /// <summary>Overclock hotkey: on / off inside Custom, with a short on-screen hint.</summary>
    void ToggleOverclock()
    {
        _osd ??= new ModeOsd();
        if (!_c.S.FanCurve) { _osd.Flash(L.T("oc.onlycustom"), false); return; }
        if (!_c.S.GpuOcOn && !_c.S.OcWarned) { _osd.Flash(L.T("oc.confirmfirst"), false); return; }   // the warning needs a window
        bool on = GpuOverclock.Toggle(_c, !_c.S.GpuOcOn, null);
        _osd.Flash("Overclock: " + L.T(on ? "oc.on" : "oc.off"), on);
        if (_form is { IsDisposed: false }) _form.RefreshBoost();
    }

    /// <summary>Next overlay style (full → mini → bar → icons); shows the overlay if it is hidden.</summary>
    void CycleOverlayStyle()
    {
        _overlay.Style = (_overlay.Style + 1) % OverlayManager.StyleCount;
        if (!_overlay.Visible) _overlay.Toggle();
    }

    /// <summary>Next power mode in the firmware list (wraps around), with a short on-screen hint.</summary>
    async Task CycleMode()
    {
        var a = _c.Awcc;
        if (a == null || a.Profiles.Count == 0) return;
        int cur = _c.CurrentMode(_c.Awcc?.GetProfile() ?? -1);   // read now: nothing is polled while the window is hidden
        var modes = a.Profiles.Select(x => (int)x).Append(Controller.CustomMode).ToList();
        int i = modes.IndexOf(cur);
        int next = modes[(i + 1) % modes.Count];
        _osd ??= new ModeOsd();
        _osd.Flash(ModeName(next), next == Hardware.Awcc.ProfileGMode);
        await Task.Run(() => _c.SelectMode(next));
        await _mon.Poll();
    }

    SettingsForm? _settings;
    PluginManager? _plugins;   // created once the tray icon exists: plugins may show notifications from it

    void OpenSettings()
    {
        if (_settings == null || _settings.IsDisposed)
        {
            _settings = new SettingsForm(_c, _overlay, _plugins!, SetLanguage, ShowAbout, ApplyHotkeys, CollectReport);
            _settings.LookChanged = ApplyLook;
            _settings.CardsChanged = () =>
            {
                _mon.Want(MainScreen, AlienHelper.MainForm.NeedFor(_c));
                if (_form is { IsDisposed: false }) _form.ApplyCards();
            };
            _settings.IntervalChanged = ms => _mon.SetInterval(ms);
            _settings.Show();
        }
        else _settings.Activate();
    }

    readonly FanCurveDriver _curves;
    readonly GpuOverclock _oc;
    FanCurveForm? _curvesForm;

    void OpenCurves()
    {
        if (_curvesForm == null || _curvesForm.IsDisposed)
        {
            _curvesForm = new FanCurveForm(_c, _mon, _curves);
            _curvesForm.Show();
        }
        else _curvesForm.Activate();
    }

    string ModeName(int mode) => mode == Controller.CustomMode ? "Custom" : _c.ProfileName((byte)mode);

    /// <summary>Custom entered or left (any thread): curves on or off, windows updated.</summary>
    void OnCustomChanged()
    {
        if (_invoker.InvokeRequired) { _invoker.BeginInvoke(OnCustomChanged); return; }
        _curves.Refresh();
        _oc.Sync();   // Overclock lives inside Custom
        UpdateTooltip();   // "Custom" or the firmware mode right away
        if (_curvesForm is { IsDisposed: false }) _curvesForm.SyncCustom();
        if (_form is { IsDisposed: false }) _form.RefreshBoost();
    }

    /// <summary>After sleep or unlock the firmware may switch the mode: watch for a while and put it back.</summary>
    void GuardModeAfterWake(string why)
    {
        if (_c.Awcc == null) return;
        Log.Info("watching the mode after " + why);
        _c.GuardMode(40);
        Task.Run(async () =>
        {
            foreach (int ms in new[] { 1500, 3000, 5000, 10000, 20000 })
            {
                await Task.Delay(ms);
                _c.RestoreMode();
            }
        });
    }

    /// <summary>
    /// "Guard lighting from AWCC": AWCC's lighting agent repaints the keyboard a few seconds after sign-in, unlock and
    /// sleep, at no fixed moment, so ours is put back several times over the first minute.
    /// </summary>
    void GuardLightsAfterWake(string why)
    {
        if (!_c.S.GuardLights || (_c.Keyboard == null && _c.Chassis == null)) return;
        Log.Info("guarding the lighting after " + why);
        Task.Run(async () =>
        {
            foreach (int ms in new[] { 2000, 6000, 12000, 25000 })   // at 2, 8, 20 and 45 s
            {
                await Task.Delay(ms);
                _c.EnforceLights();
            }
        });
    }

    KeyboardForm? _keys;

    void OpenKeys()
    {
        if (_keys == null || _keys.IsDisposed)
        {
            _keys = new KeyboardForm(_c);
            _keys.Show();
        }
        else
        {
            if (_keys.WindowState == FormWindowState.Minimized) _keys.WindowState = FormWindowState.Normal;
            _keys.Activate();
        }
    }

    /// <summary>Explains limited mode on unsupported hardware; warns once about untested Alienware models.</summary>
    /// <summary>
    /// Limited mode: after the note on what the rights are for, starts an elevated instance (the UAC prompt) and exits
    /// once it is running; it waits for this one to close. Declined: nothing changes.
    /// </summary>
    void RestartElevated()
    {
        var owner = _form is { IsDisposed: false, Visible: true } ? _form : null;
        if (!ElevationNotice.Confirm(owner, _c.S)) return;
        if (!Launcher.Elevate(new[] { Launcher.RestartedArg }, wait: false)) return;
        Log.Info("limited mode: restarting with admin rights");
        ExitThread();
    }

    void CheckHardware()
    {
        if (Log.TakeCrashFlag()) OfferCrashReport();
        var model = Hardware.SystemInfo.Model;
        if (Launcher.IsLimited) return;   // without admin rights the firmware is closed anyway; the main window says so
        if (_c.Awcc == null)
        {
            // said by the red "Limited mode" card in the main window, not by a message at every start
            Log.Info($"no Alienware firmware on '{model}': limited mode");
            return;
        }
        if (!Hardware.SystemInfo.IsTestedModel && _c.S.UntestedModelWarned != model)
        {
            MessageBox.Show(L.T("hw.untested", model), Log.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _c.S.UntestedModelWarned = model;
            _c.S.Save();
        }
    }

    void ShowAbout()
    {
        using var about = new AboutForm();
        about.ShowDialog(_form is { IsDisposed: false, Visible: true } ? _form : null);
    }

    /// <summary>The previous run ended with an unhandled exception: offer to collect a report.</summary>
    void OfferCrashReport()
    {
        if (MessageBox.Show(L.T("diag.crashed"), Log.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            CollectReport();
    }

    /// <summary>Saves the diagnostics ZIP on the Desktop, shows it and offers to write to the developer.</summary>
    public void CollectReport()
    {
        try
        {
            var path = Diagnostics.CreateReport(_c, _mon);
            Diagnostics.Reveal(path);
            if (MessageBox.Show(L.T("diag.saved", path, Log.ContactEmail), Log.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Diagnostics.MailDeveloper();
        }
        catch (Exception ex)
        {
            Log.Error("report failed", ex);
            MessageBox.Show(ex.Message, Log.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void OpenCharts()
    {
        if (_charts == null || _charts.IsDisposed)
        {
            _charts = new ChartsForm(_c, _mon);
            _charts.Show();
        }
        else
        {
            if (_charts.WindowState == FormWindowState.Minimized) _charts.WindowState = FormWindowState.Normal;
            _charts.Activate();
        }
    }

    void UpdateTooltip()
    {
        var snap = _mon.Last;
        if (snap == null) return;
        var temps = string.Join(" ", snap.Temps.Where(t => t.temp > 0).Take(3).Select(t => $"{t.s.Name} {t.temp}°"));
        var rpm = string.Join("/", snap.Fans.Select(f => f.rpm));
        var text = $"{_c.CurrentModeName(snap.Profile)}\n{temps}\nFans {rpm}";
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    static Icon MakeIcon() => Theme.AppIcon(SystemInformation.SmallIconSize);

    long _tipAt = -10000;
    /// <summary>Demand key for the main window's sections (independent of the window being open).</summary>
    static readonly object MainScreen = new();

    protected override void ExitThreadCore()
    {
        _plugins?.Dispose();   // first: a plugin may still use the hardware or the tray
        _c.RememberMode();   // the mode may have been switched with the G key while nothing was polled
        Log.Info("exit");
        _curves.Dispose();   // no curve write may follow the reset below
        _oc.RestoreNow();    // the GPU gets its original clock offsets back
        _c.ReleaseFans();
        SystemEvents.PowerModeChanged -= OnPowerMode;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _overlayKey?.Dispose();
        _modeKey?.Dispose();
        _styleKey?.Dispose();
        _osd?.Dispose();
        _showWait.Unregister(null);
        _show.Dispose();
        _exitWait.Unregister(null);
        _exit.Dispose();
        _invoker.Dispose();
        _overlay.Dispose();
        _mon.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _charts?.Dispose();
        _form?.Dispose();
        _c.Dispose();
        base.ExitThreadCore();
    }
}
