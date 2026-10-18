using AlienHelper.Hardware;

namespace AlienHelper;

/// <summary>
/// GPU overclock for the Custom mode: core and memory clock offsets through the NVIDIA driver, in cautious presets
/// added on top of whatever offsets were set before (by AWCC, NVIDIA App or another tool). Those original offsets are
/// saved before the first change and put back when Custom or Overclock is switched off, when the app exits, after a
/// crash (next start) and after sleep. The CPU is not touched.
/// </summary>
public sealed class GpuOverclock
{
    /// <summary>Presets: MHz added to the original core and memory offsets.</summary>
    public static readonly (string id, int core, int mem)[] Presets =
    {
        ("mild", 50, 150),
        ("medium", 100, 300),
        ("strong", 150, 500),
    };

    readonly Controller _c;
    readonly object _lock = new();
    GpuClocks? _gc;
    Nvml? _wake;
    bool _opened;

    /// <summary>What is applied now, for the UI ("" when nothing).</summary>
    public string State { get; private set; } = "";

    public GpuOverclock(Controller c) { _c = c; }

    public static (string id, int core, int mem) Preset(string? id) => Presets.FirstOrDefault(p => p.id == id) is { id: not null } p ? p : Presets[0];

    /// <summary>
    /// Switches Overclock. The first time it is turned on a warning asks to confirm (owner given: a window can show it;
    /// without an owner, e.g. the hotkey, an unconfirmed first switch is refused). Returns the new state.
    /// </summary>
    public static bool Toggle(Controller c, bool on, IWin32Window? owner)
    {
        if (on && !c.S.OcWarned)
        {
            if (owner == null) return c.S.GpuOcOn;
            var r = MessageBox.Show(owner, L.T("oc.warn"), "Overclock", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return c.S.GpuOcOn;
            c.S.OcWarned = true;
        }
        c.S.GpuOcOn = on;
        c.S.Save();
        Log.Info("overclock " + (on ? "on" : "off"));
        c.RaiseOcChanged();
        return on;
    }

    /// <summary>Overclock wanted right now: Custom mode with Overclock on.</summary>
    public bool Wanted => _c.S.FanCurve && _c.S.GpuOcOn;

    /// <summary>Brings the GPU in line with the settings (in the background: a dozing dGPU takes a moment to wake).</summary>
    public void Sync() => Task.Run(SyncNow);

    /// <summary>Puts the original offsets back now (exit).</summary>
    public void RestoreNow()
    {
        lock (_lock) Restore();
    }

    void SyncNow()
    {
        lock (_lock)
        {
            try
            {
                if (Wanted) ApplyPreset();
                else Restore();
            }
            catch (Exception ex) { Log.Warn("gpu overclock: " + ex.Message); }
        }
    }

    bool Open()
    {
        if (!_opened)
        {
            _opened = true;
            _gc = GpuClocks.TryOpen(out var why);
            _wake = Nvml.TryOpen();
            if (_gc == null) Log.Info("gpu overclock unavailable: " + why);
        }
        return _gc != null;
    }

    void Wake() => _wake?.Read(0);

    void ApplyPreset()
    {
        if (!Open()) return;
        var s = _c.S;
        if (!s.GpuOcApplied)
        {
            // remember what was there before the first change, so it can be put back exactly
            var now = _gc!.Offsets(Wake);
            if (now == null) { Log.Warn($"gpu overclock: offsets unreadable (NVAPI {_gc.LastStatus})"); return; }
            s.GpuOcBaseCore = now.Value.core;
            s.GpuOcBaseMem = now.Value.mem;
            s.GpuOcApplied = true;
            s.Save();
        }
        var p = Preset(s.GpuOc);
        int core = s.GpuOcBaseCore + p.core, mem = s.GpuOcBaseMem + p.mem;
        bool ok = _gc!.ApplyWithRetry(core, mem, Wake);
        State = ok ? L.T("oc.state", Sign(core), Sign(mem)) : L.T("oc.refused");
        Log.Info($"gpu overclock {p.id}: core {Sign(core)} MHz, memory {Sign(mem)} MHz (was {Sign(s.GpuOcBaseCore)}/{Sign(s.GpuOcBaseMem)}) {(ok ? "ok" : $"refused, NVAPI {_gc.LastStatus}")}");
    }

    void Restore()
    {
        var s = _c.S;
        State = "";
        if (!s.GpuOcApplied || !Open()) return;
        bool ok = _gc!.ApplyWithRetry(s.GpuOcBaseCore, s.GpuOcBaseMem, Wake);
        Log.Info($"gpu overclock off: back to core {Sign(s.GpuOcBaseCore)} / memory {Sign(s.GpuOcBaseMem)} MHz {(ok ? "ok" : $"refused, NVAPI {_gc.LastStatus}")}");
        if (ok) { s.GpuOcApplied = false; s.Save(); }
    }

    static string Sign(int v) => v > 0 ? $"+{v}" : v.ToString();
}
