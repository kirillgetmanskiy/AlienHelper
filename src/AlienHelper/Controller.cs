using System.Drawing;
using AlienHelper.Hardware;

namespace AlienHelper;

/// <summary>Owns the hardware handles and applies settings. All hardware calls go through here.</summary>
public sealed class Controller : IDisposable
{
    public Awcc? Awcc { get; }
    public string? AwccError { get; }
    public List<AlienFxDevice> Lights { get; } = new();
    public ScreenBrightness? Screen { get; }
    public Settings S { get; }

    public sealed record Snapshot(int Profile, List<(Awcc.Fan fan, int rpm, int pct, int boost)> Fans, List<(Awcc.Sensor s, int temp)> Temps);

    public Controller(Settings s)
    {
        S = s;
        Awcc = Awcc.TryOpen(out var err);
        AwccError = err;
        try { Lights.AddRange(AlienFxDevice.Enumerate()); } catch { }
        Screen = ScreenBrightness.TryOpen();
    }

    public AlienFxDevice? Keyboard => Lights.FirstOrDefault(l => l.Version == AlienFxDevice.Api.V5);
    public AlienFxDevice? Chassis => Lights.FirstOrDefault(l => l.Version == AlienFxDevice.Api.V4);

    public Snapshot? Read()
    {
        var a = Awcc;
        if (a == null) return null;
        // percent and boost are not read: this firmware answers -1 / 0 for them, and every WMI call costs a few ms
        var fans = a.Fans.Select(f => (f, a.GetFanRpm(f.Id), -1, 0)).ToList();
        var temps = a.Sensors.Select(x => (x, a.GetTemp(x.Id))).ToList();
        return new Snapshot(a.GetProfile(), fans, temps);
    }

    /// <summary>Saves the current power mode so the next start restores it.</summary>
    public void RememberMode()
    {
        try
        {
            int p = Awcc?.GetProfile() ?? -1;
            if (p >= 0 && Awcc!.Profiles.Contains((byte)p) && S.Profile != p) { S.Profile = p; S.GMode = p == Awcc.ProfileGMode; S.Save(); }
        }
        catch { }
    }

    /// <summary>The power mode only (one firmware call): used while nothing is on screen but a boost is active.</summary>
    public Snapshot? ReadMode()
    {
        var a = Awcc;
        return a == null ? null : new Snapshot(a.GetProfile(), new(), new());
    }

    public bool SetProfile(byte p)
    {
        if (Awcc == null) return false;
        using var timed = Hitches.Time("mode switch");
        bool ok = Awcc.SetProfile(p);
        if (ok)
        {
            S.Profile = p; S.GMode = p == Awcc.ProfileGMode; S.Save();
            Log.Info($"mode -> 0x{p:X2} {ProfileName(p)}");
            BoostForMode(p);
        }
        return ok;
    }

    int _lastProfile = -1;

    /// <summary>
    /// Called with every sensor sample. The first sample (app start) and any mode switched outside the app
    /// (G key, AWCC) set the boost that belongs to the mode.
    /// </summary>
    public void NotifyProfile(int p)
    {
        if (p < 0 || p == _lastProfile || _emergency) return;
        // right after sleep / unlock the firmware may switch modes on its own: put the chosen one back, do not adopt it
        if (Guarded && Awcc != null && p != WantedProfile) { Task.Run(RestoreMode); return; }
        // the mode was switched outside the app (G key, AWCC) away from Custom's base: Custom ends
        if (S.FanCurve && p != CustomBase) { S.FanCurve = false; S.Save(); Log.Info("Custom ended: mode switched outside the app"); CustomChanged?.Invoke(); }
        // remember the mode even when it was switched outside the app, so the next start restores it
        if (Awcc != null && Awcc.Profiles.Contains((byte)p) && S.Profile != p) { S.Profile = p; S.GMode = p == Awcc.ProfileGMode; S.Save(); }
        BoostForMode(p);
    }

    /// <summary>
    /// Each mode sets the boost slider: Alien Mode runs the fans flat out (100%), Cool at 75%, every other mode
    /// returns them to its own curve ("auto"). The user can still move the slider inside a mode.
    /// </summary>
    void BoostForMode(int p)
    {
        _lastProfile = p;
        CheckLightsSoon();
        if (S.FanCurve) return;   // Custom (on any base, Alien Mode included): the fan curves set the boost
        // Alien Mode runs the fans flat out, Cool keeps them high (75 %), the other modes leave them to the BIOS curve
        int target = p == Awcc.ProfileGMode ? 100 : p == ProfileCool ? 75 : 0;
        if (S.FanBoost == target && S.FanBoostCpu == target && S.FanBoostGpu == target) return;
        try { SetBoost(target); } catch { }
        BoostChanged?.Invoke();
    }

    /// <summary>Firmware "Cool" thermal profile.</summary>
    public const byte ProfileCool = 0xA2;

    /// <summary>Pseudo profile id for the Custom mode in mode lists.</summary>
    public const int CustomMode = 0x100;

    /// <summary>
    /// Firmware profile Custom runs on: it sets the CPU and GPU power limits (e.g. the GPU gets 80 W in Balanced and
    /// 175 W in Performance / Alien Mode on an RTX 5080 Laptop), the fans follow the curves on top of it.
    /// Default Alien Mode (full power); Balanced where the firmware has no Alien Mode.
    /// </summary>
    public byte CustomBase => Awcc != null && S.CustomBase is > 0 and < 0xFF && Awcc.Profiles.Contains((byte)S.CustomBase)
        ? (byte)S.CustomBase : Awcc != null && Awcc.Profiles.Contains(Hardware.Awcc.ProfileGMode) ? Hardware.Awcc.ProfileGMode : (byte)0xA0;

    /// <summary>Raised (any thread) when Custom was entered or left.</summary>
    public event Action? CustomChanged;

    /// <summary>Overclock switched or its preset changed (the tray app applies it).</summary>
    public event Action? OcChanged;
    public void RaiseOcChanged() => OcChanged?.Invoke();

    /// <summary>
    /// Switches to one mode: a firmware profile, or <see cref="CustomMode"/>. Only one is active at a time: choosing a
    /// firmware profile ends Custom, choosing Custom sets its base profile and hands the fans to the curves.
    /// </summary>
    public bool SelectMode(int mode)
    {
        if (Awcc == null) return false;
        if (_emergency && mode != EmergencyMode)
        {
            // the GPU is still hot: the choice is where the emergency mode returns to once it has cooled down
            _emergencyFrom = mode; S.EmergencyFrom = mode; S.Save();
            Log.Info($"emergency mode: will return to {ModeLogName(mode)}");
            Alert?.Invoke(L.T("emerg.title"), L.T("emerg.later", ModeLogName(mode), S.EmergencyCoolC));
            return true;
        }
        if (mode == CustomMode)
        {
            bool was = S.FanCurve;
            S.FanCurve = true;
            S.Save();
            bool ok = SetProfile(CustomBase);
            Log.Info("mode -> Custom");
            if (!was) CustomChanged?.Invoke();
            return ok;
        }
        bool left = S.FanCurve;
        if (left) { S.FanCurve = false; S.Save(); }
        bool r = SetProfile((byte)mode);
        if (left) CustomChanged?.Invoke();
        return r;
    }

    /// <summary>The mode currently chosen: a firmware profile id or <see cref="CustomMode"/>.</summary>
    public int CurrentMode(int firmwareProfile) => _emergency ? EmergencyMode : S.FanCurve ? CustomMode : firmwareProfile;

    /// <summary>The firmware profile the app wants (Custom's base, or the last mode chosen).</summary>
    int WantedProfile => S.FanCurve ? CustomBase : S.Profile;

    long _guardUntil;
    bool Guarded => Environment.TickCount64 < _guardUntil;

    /// <summary>For a while after sleep or unlock, a mode change by the firmware is undone instead of adopted.</summary>
    public void GuardMode(int seconds) => _guardUntil = Environment.TickCount64 + seconds * 1000L;

    /// <summary>Puts the chosen mode back if the firmware switched away from it.</summary>
    public void RestoreMode()
    {
        try
        {
            if (_emergency) return;   // Balanced on purpose until the GPU cools down
            int want = WantedProfile, cur = Awcc?.GetProfile() ?? -1;
            if (want < 0 || cur < 0 || cur == want) return;
            Log.Info($"mode changed to 0x{cur:X2} after sleep/unlock: back to 0x{want:X2}");
            SetProfile((byte)want);
        }
        catch (Exception ex) { Log.Warn("restore mode: " + ex.Message); }
    }

    /// <summary>Custom switched off: the current mode sets the boost again (Alien Mode 100 %, others auto).</summary>
    public void ModeBoostNow()
    {
        int p = Awcc?.GetProfile() ?? -1;
        if (p < 0) return;
        _lastProfile = -1;
        S.FanBoost = S.FanBoostCpu = S.FanBoostGpu = -1;   // force a write: the curves left their own values
        BoostForMode(p);
    }

    /// <summary>Raised (from any thread) when the app changed the boost on its own.</summary>
    public event Action? BoostChanged;

    public bool SetBoost(int value)
    {
        if (Awcc == null) return false;
        value = Math.Clamp(value, 0, 100);
        bool ok = true;
        foreach (var f in Awcc.Fans) ok &= Awcc.SetFanBoost(f.Id, (byte)value);
        S.FanBoost = S.FanBoostCpu = S.FanBoostGpu = value; S.Save();
        return ok;
    }

    /// <summary>Fan groups by the temperature sensor that drives them (CPU, GPU).</summary>
    public const byte CpuSensor = 0x01, GpuSensor = 0x06;

    /// <summary>Expert mode: boost only the fans of one group (<see cref="CpuSensor"/> or <see cref="GpuSensor"/>).</summary>
    /// <param name="save">false for the fan curves: they write often, the settings file need not follow</param>
    public bool SetBoostGroup(byte sensor, int value, bool save = true)
    {
        if (Awcc == null) return false;
        if (!save && !S.FanCurve) return false;   // a curve write that arrives after Custom ended must not undo the mode's boost
        value = Math.Clamp(value, 0, 100);
        bool ok = true;
        foreach (var f in Awcc.Fans.Where(f => f.SensorId == sensor)) ok &= Awcc.SetFanBoost(f.Id, (byte)value);
        if (sensor == CpuSensor) S.FanBoostCpu = value; else S.FanBoostGpu = value;
        S.FanBoost = Math.Max(S.FanBoostCpu, S.FanBoostGpu);
        if (!save) return ok;
        S.Save();
        Log.Info($"boost {(sensor == CpuSensor ? "CPU" : "GPU")} -> {value}");
        return ok;
    }

    public bool SetKeyboard(Color c, int brightness)
    {
        var k = Keyboard;
        if (k == null) return false;
        StopAnimation();
        bool ok;
        lock (_kbLock) ok = k.SetColor(c.R, c.G, c.B, S.KeyboardLights) & k.SetBrightness(brightness, S.KeyboardLights);
        S.LightsEnabled = true; S.KeyboardCustom = false; S.KeyboardAnimation = null;
        S.KeyboardColor = ColorTranslator.ToHtml(c); S.KeyboardBrightness = brightness; S.Save();
        return ok;
    }

    readonly object _kbLock = new();

    /// <summary>LED id of a layout key, honouring the user's calibration.</summary>
    public int LightId(KeyboardLayout.Key key) => S.KeyIds.TryGetValue(key.Name, out var id) ? id : key.DefaultId;

    /// <summary>Per-key scheme: colors by layout key index; keys not in the map are off.</summary>
    /// <param name="save">Remember as the user's static scheme (also stops any animation).</param>
    public bool SetKeyScheme(IReadOnlyDictionary<int, Color> colors, bool save = true)
    {
        if (save) { StopAnimation(); S.KeyboardAnimation = null; }
        return SendScheme(colors, fast: false, save);
    }

    bool SendScheme(IReadOnlyDictionary<int, Color> colors, bool fast, bool save)
    {
        var k = Keyboard;
        if (k == null) return false;
        var byId = new Dictionary<int, Color>();
        foreach (var key in KeyboardLayout.Keys)
        {
            if (!colors.TryGetValue(key.Index, out var c)) continue;
            byId[LightId(key)] = c;
            // extra LEDs of wide keys, unless the user remapped the key
            if (!S.KeyIds.ContainsKey(key.Name))
                foreach (var extra in key.ExtraIds) byId.TryAdd(extra, c);
        }
        bool ok;
        lock (_kbLock)
        {
            // while the fans are boosted the firmware lights the boost key (F6) itself: leave that LED to it
            var ids = fast ? MappedIds() : Enumerable.Range(0, S.KeyboardLights).ToList();
            if (BoostKeyLit) ids.Remove(BoostKeyId);
            ok = k.SetKeyColors(id => byId.TryGetValue(id, out var c) ? (c.R, c.G, c.B) : ((byte)0, (byte)0, (byte)0), S.KeyboardLights, fast, ids);
            if (!fast) ok &= k.SetBrightness(S.KeyboardBrightness, S.KeyboardLights);
        }
        if (save)
        {
            S.LightsEnabled = true; S.KeyboardCustom = true;
            S.KeyColors = colors.ToDictionary(kv => KeyboardLayout.Keys[kv.Key].Name, kv => ColorTranslator.ToHtml(kv.Value));
            S.Save();
        }
        return ok;
    }

    // ---------------- animated templates ----------------

    System.Threading.Timer? _animTimer;
    readonly System.Diagnostics.Stopwatch _animClock = new();
    int _animBusy;

    /// <summary>Frame statistics (diagnostics).</summary>
    public int FrameCount;
    public double FrameMsTotal;

    /// <summary>LED of the boost key (F6 on the Area-51), honouring the user's calibration.</summary>
    int BoostKeyId => KeyboardLayout.Keys.FirstOrDefault(k => k.Name == "F6") is { } f6 ? LightId(f6) : -1;

    /// <summary>
    /// The firmware shows the boost on the boost key's own light: in Alien Mode, with the fans boosted to 100 % and in
    /// the emergency mode. Keyboard colors and effects then leave that LED alone, or the two would take turns.
    /// </summary>
    bool BoostKeyLit => _emergency || _lastProfile == Hardware.Awcc.ProfileGMode
        || S.FanBoost >= 100 || S.FanBoostCpu >= 100 || S.FanBoostGpu >= 100;

    /// <summary>Every LED id the layout uses (main + extra), so animation frames skip unused ids.</summary>
    List<int> MappedIds() =>
        KeyboardLayout.Keys.SelectMany(k => S.KeyIds.ContainsKey(k.Name) ? new[] { LightId(k) } : k.ExtraIds.Prepend(LightId(k)))
            .Distinct().OrderBy(i => i).ToList();

    /// <summary>Id of the running animation, or null.</summary>
    public string? AnimationId { get; private set; }

    /// <summary>Raised from a worker thread with every frame sent to the keyboard (editor preview).</summary>
    public event Action<string, Dictionary<int, Color>>? AnimationFrame;

    /// <summary>Selects an animated template; it moves only if <see cref="Settings.AnimationOn"/> is set.</summary>
    public void UseAnimatedTemplate(string id)
    {
        Log.Info($"keyboard animation -> {id}");
        var t = KeyboardLayout.Find(id);
        if (t?.Animate == null || Keyboard == null) return;
        StopAnimation();
        var frame0 = KeyboardLayout.Render(t);
        SendScheme(frame0, fast: false, save: true);   // stops firmware effects, stores a static fallback
        S.KeyboardAnimation = id;
        S.Save();
        if (S.AnimationOn) StartAnimation(t);
    }

    public void SetAnimationOn(bool on)
    {
        S.AnimationOn = on; S.Save();
        var t = KeyboardLayout.Find(S.KeyboardAnimation);
        if (!on) StopAnimation();
        else if (t?.Animate != null && S.KeyboardCustom && S.LightsEnabled && AnimationId == null) StartAnimation(t);
    }

    int _animGen;   // bumped (under _kbLock) by every stop; a frame from an older generation is never written

    void StartAnimation(KeyboardLayout.Template t)
    {
        StopAnimation();
        int gen;
        lock (_kbLock) gen = _animGen;
        AnimationId = t.Id;
        _animClock.Restart();
        _animTimer = new System.Threading.Timer(_ =>
        {
            if (Interlocked.Exchange(ref _animBusy, 1) == 1) return;   // previous frame still being written
            try
            {
                var frame = KeyboardLayout.Render(t, _animClock.Elapsed.TotalSeconds);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                lock (_kbLock)
                {
                    // checked under the same lock StopAnimation takes: once it returns, no stale frame can follow
                    if (gen != _animGen) return;
                    using var timed = Hitches.Time("keyboard frame");
                    SendScheme(frame, fast: true, save: false);
                }
                FrameCount++; FrameMsTotal += sw.Elapsed.TotalMilliseconds;
                AnimationFrame?.Invoke(t.Id, frame);
            }
            catch { }
            finally { Volatile.Write(ref _animBusy, 0); }
        }, null, 0, 40);   // ~25 frames per second; a frame takes ~17 ms to write
    }

    /// <summary>Stops the animation; returns only after any frame being written has finished.</summary>
    public void StopAnimation()
    {
        lock (_kbLock) _animGen++;
        AnimationId = null;
        _animTimer?.Dispose();
        _animTimer = null;
    }

    /// <summary>Lights exactly one LED id (white) — used while calibrating the key map.</summary>
    public void LightSingleId(int id)
    {
        var k = Keyboard;
        if (k == null) return;
        lock (_kbLock) k.SetKeyColors(i => i == id ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0), S.KeyboardLights);
    }

    public Dictionary<int, Color> SavedKeyScheme()
    {
        var d = new Dictionary<int, Color>();
        foreach (var key in KeyboardLayout.Keys)
            if (S.KeyColors.TryGetValue(key.Name, out var hex))
                try { d[key.Index] = ColorTranslator.FromHtml(hex); } catch { }
        return d;
    }

    public bool SetKeyboardBrightness(int brightness)
    {
        var k = Keyboard;
        if (k == null) return false;
        bool ok;
        lock (_kbLock) ok = k.SetBrightness(brightness, S.KeyboardLights);
        S.KeyboardBrightness = brightness; S.Save();
        return ok;
    }

    public bool SetChassis(Color c, int brightness)
    {
        var ch = Chassis;
        if (ch == null) return false;
        bool ok = ch.SetColor(c.R, c.G, c.B, S.ChassisLights) & ch.SetBrightness(brightness, S.ChassisLights);
        S.LightsEnabled = true; S.ChassisColor = ColorTranslator.ToHtml(c); S.ChassisBrightness = brightness; S.Save();
        return ok;
    }

    public void ApplySaved()
    {
        // Fan boost is not restored: the mode applied below sets it (Alien Mode 100%, others auto).
        if (S.FanBoost != 0 || S.FanBoostCpu != 0 || S.FanBoostGpu != 0) { S.FanBoost = S.FanBoostCpu = S.FanBoostGpu = 0; S.Save(); }
        if (S.EmergencyFrom >= 0 && Awcc != null)
        {
            // the app ended (or crashed) in the emergency mode: Balanced and the fans at 100 % may still be on
            int from = S.EmergencyFrom;
            S.EmergencyFrom = -1;
            S.FanBoost = S.FanBoostCpu = S.FanBoostGpu = -1;   // the mode below writes its boost again
            S.Save();
            Log.Info($"start: the emergency mode was on: back to {ModeLogName(from)}");
            try { SelectMode(from); } catch (Exception ex) { Log.Warn("leave emergency mode: " + ex.Message); }
        }
        if (!S.ApplyOnStart) return;
        // The BIOS starts in its default mode after a reboot: bring back the last mode used.
        if (Awcc != null && S.Profile >= 0 && Awcc.Profiles.Contains((byte)S.Profile))
        {
            try
            {
                int cur = Awcc.GetProfile();
                if (cur != S.Profile) { Log.Info($"start: restoring mode 0x{S.Profile:X2} (was 0x{cur:X2})"); SetProfile((byte)S.Profile); }
            }
            catch (Exception ex) { Log.Warn("restore mode: " + ex.Message); }
        }
        ApplyLights();
    }

    /// <summary>On exit: hand the fans back to the BIOS so a boost never outlives the app.</summary>
    public void ReleaseFans()
    {
        if (_emergency) try { LeaveEmergency(); } catch (Exception ex) { Log.Warn("leave emergency mode: " + ex.Message); }
        if (Awcc == null || (S.FanBoost == 0 && S.FanBoostCpu == 0 && S.FanBoostGpu == 0)) return;
        try { SetBoost(0); } catch { }
    }

    /// <summary>Turns lights off without forgetting the saved colors.</summary>
    public void LightsOff()
    {
        StopAnimation();
        Keyboard?.SetBrightness(0, S.KeyboardLights);
        Chassis?.SetBrightness(0, S.ChassisLights);
        S.LightsEnabled = false; S.Save();
    }

    public void LightsOn() { S.LightsEnabled = true; S.Save(); ApplyLights(); }

    /// <summary>
    /// Puts our lighting back over whatever AWCC's lighting agent wrote (it repaints after sign-in, unlock, sleep and
    /// mode changes): our colors, or dark when the lights are turned off here. An animation is sent in full and restarted
    /// too: its fast frames do not stop a firmware effect the agent may have started.
    /// </summary>
    public void EnforceLights()
    {
        try
        {
            if (!S.LightsEnabled)
            {
                Keyboard?.SetBrightness(0, S.KeyboardLights);
                Chassis?.SetBrightness(0, S.ChassisLights);
            }
            else ApplyLights();
        }
        catch (Exception ex) { Log.Warn("lights check: " + ex.Message); }
    }

    int _lightsCheck;

    /// <summary>"Guard lighting from AWCC": a mode change can make the agent repaint, so ours follows it. The last change wins.</summary>
    void CheckLightsSoon()
    {
        if (!S.GuardLights || (Keyboard == null && Chassis == null)) return;
        int gen = Interlocked.Increment(ref _lightsCheck);
        Task.Run(async () =>
        {
            foreach (int ms in new[] { 2000, 6000 })   // at 2 and 8 s
            {
                await Task.Delay(ms);
                if (gen != Volatile.Read(ref _lightsCheck)) return;
                EnforceLights();
            }
        });
    }

    public void ApplyLights()
    {
        if (!S.LightsEnabled) return;
        try
        {
            var anim = KeyboardLayout.Find(S.KeyboardAnimation);
            if (S.KeyboardCustom && anim?.Animate != null)
            {
                SendScheme(KeyboardLayout.Render(anim), fast: false, save: false);
                if (S.AnimationOn) StartAnimation(anim);
            }
            else if (S.KeyboardCustom) SetKeyScheme(SavedKeyScheme(), save: false);
            else SetKeyboard(ColorTranslator.FromHtml(S.KeyboardColor), S.KeyboardBrightness);
        }
        catch { }
        try { SetChassis(ColorTranslator.FromHtml(S.ChassisColor), S.ChassisBrightness); } catch { }
    }

    /// <summary>What a mode does (tooltip), or "" for modes without a description.</summary>
    public static string ModeTip(int p)
    {
        string key = $"mode.tip.{p:X2}";
        string t = L.T(key);
        return t == key ? "" : t;
    }

    /// <summary>Name of the mode in use: "Custom" while it runs (the firmware then reports its base profile).</summary>
    public string CurrentModeName(int firmwareProfile) => _emergency ? L.T("mode.emergency") : S.FanCurve ? "Custom" : ProfileName(firmwareProfile);

    /// <summary>Display name of a firmware profile id, or "unknown" if the firmware reports something unexpected.</summary>
    public string ProfileName(int p)
    {
        bool known = p >= 0 && Awcc.ProfileNames.ContainsKey((byte)p) && (Awcc == null || p == 0 || Awcc.Profiles.Contains((byte)p));
        return known ? Awcc.ProfileNames[(byte)p] : L.T("mode.unknown");
    }

    // ---------------- emergency mode ----------------

    /// <summary>Pseudo mode id: the emergency mode, which only the app enters (GPU overheating), never the user.</summary>
    public const int EmergencyMode = 0x101;

    /// <summary>Firmware "Balanced" profile: the GPU gets its base power (80 W on an RTX 5080 Laptop).</summary>
    public const byte ProfileBalanced = 0xA0;

    volatile bool _emergency, _emergencyBusy;
    int _emergencyFrom = -1;
    long _hotSince = -1, _coolSince = -1, _warmAt = -1;

    /// <summary>The emergency mode is on: Balanced and the fans at 100 % until the GPU cools down.</summary>
    public bool EmergencyActive => _emergency;

    /// <summary>Raised (any thread) with a title and a text for a notification.</summary>
    public event Action<string, string>? Alert;

    /// <summary>Raised (any thread) when the emergency mode started or ended.</summary>
    public event Action? EmergencyChanged;

    /// <summary>Whether GPU temperatures are wanted in the background for the emergency mode.</summary>
    public bool WatchEmergency => (S.Emergency || _emergency) && Awcc != null && Awcc.Profiles.Contains(ProfileBalanced);

    string ModeLogName(int mode) => mode == CustomMode ? "Custom" : ProfileName(mode);

    /// <summary>
    /// Called with every GPU sample. The GPU at <see cref="Settings.EmergencyHotC"/> for 3 s, or the laptop's own
    /// protection (the GPU cut to the Balanced limit under load right after running hot), starts the emergency mode:
    /// Balanced and the fans at 100 %. Once the GPU has stayed at <see cref="Settings.EmergencyCoolC"/> or below for
    /// 10 s, the mode chosen before comes back.
    /// </summary>
    public void NotifyGpuTemp(int temp, float limitW, int load, int profile)
    {
        if (temp <= 0 || _emergencyBusy || Awcc == null) return;
        long now = Environment.TickCount64;
        if (temp >= S.EmergencyHotC - 3) _warmAt = now;
        if (_emergency)
        {
            if (!S.Emergency) { EndEmergency("switched off"); return; }
            if (temp > S.EmergencyCoolC) { _coolSince = -1; return; }
            if (_coolSince < 0) { _coolSince = now; return; }
            if (now - _coolSince >= 10000) EndEmergency($"GPU {temp} °C");
            return;
        }
        if (!WatchEmergency) { _hotSince = -1; return; }
        bool cut = profile >= 0 && profile != ProfileBalanced && limitW is > 0 and <= 90 && load >= 50 && _warmAt >= 0 && now - _warmAt < 60000;
        if (temp < S.EmergencyHotC && !cut) { _hotSince = -1; return; }
        if (_hotSince < 0) _hotSince = now;
        if (!cut && now - _hotSince < 3000) return;   // a short spike is not overheating
        StartEmergency(temp, cut);
    }

    void StartEmergency(int temp, bool cut)
    {
        _emergencyBusy = true;
        _hotSince = _coolSince = -1;
        Task.Run(() =>
        {
            try
            {
                var a = Awcc!;
                int cur = a.GetProfile();
                int from = S.FanCurve ? CustomMode : cur >= 0 && a.Profiles.Contains((byte)cur) ? cur : S.Profile;
                _emergencyFrom = from;
                S.EmergencyFrom = from; S.Save();
                _emergency = true;   // before the switch: Balanced must not become the user's mode
                a.SetProfile(ProfileBalanced);
                foreach (var f in a.Fans) a.SetFanBoost(f.Id, 100);
                Log.Warn($"emergency mode: GPU {temp} °C{(cut ? ", the laptop cut the GPU power" : "")}: Balanced, fans 100 % until {S.EmergencyCoolC} °C (was {ModeLogName(from)})");
                Alert?.Invoke(L.T("emerg.title"), L.T("emerg.on", temp, S.EmergencyCoolC));
            }
            catch (Exception ex) { Log.Warn("emergency mode: " + ex.Message); }
            finally { _emergencyBusy = false; EmergencyChanged?.Invoke(); }
        });
    }

    void EndEmergency(string why)
    {
        _emergencyBusy = true;
        Task.Run(() =>
        {
            try
            {
                int from = LeaveEmergency();
                Log.Info($"emergency mode over ({why}): back to {ModeLogName(from)}");
                Alert?.Invoke(L.T("emerg.title"), L.T("emerg.off", ModeLogName(from)));
            }
            catch (Exception ex) { Log.Warn("leave emergency mode: " + ex.Message); }
            finally { _emergencyBusy = false; EmergencyChanged?.Invoke(); }
        });
    }

    /// <summary>Back to the mode chosen before (it writes its own boost again); returns that mode.</summary>
    int LeaveEmergency()
    {
        int from = _emergencyFrom >= 0 ? _emergencyFrom : S.EmergencyFrom;
        _emergency = false;
        _emergencyFrom = -1;
        _hotSince = _coolSince = -1;
        _lastProfile = -1;
        S.EmergencyFrom = -1;
        S.FanBoost = S.FanBoostCpu = S.FanBoostGpu = -1;   // the fans ran at 100 %: make the mode write its boost
        S.Save();
        if (from == CustomMode || (from >= 0 && Awcc!.Profiles.Contains((byte)from))) SelectMode(from);
        else { from = S.Profile; SelectMode(from); }
        return from;
    }

    public void Dispose()
    {
        StopAnimation();
        Screen?.Dispose();
        Awcc?.Dispose();
        foreach (var l in Lights) l.Dispose();
    }
}
