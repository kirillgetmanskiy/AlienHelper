using System.Text.Json;

namespace AlienHelper;

public sealed class UserTemplate
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Colors { get; set; } = new();   // key name -> #RRGGBB
}

public sealed class Settings
{
    public int Profile { get; set; } = -1;          // -1 = don't touch on start
    public int PollIntervalMs { get; set; } = 2000;  // sensor refresh, 100..10000 ms
    public bool GMode { get; set; }
    public int FanBoost { get; set; }               // 0..100, applied to every fan
    public bool FanCurve { get; set; }                       // Custom: boost follows the CPU / GPU curves
    public List<int[]> CpuCurve { get; set; } = AlienHelper.FanCurve.DefaultCpu();   // [°C, boost %]
    public List<int[]> GpuCurve { get; set; } = AlienHelper.FanCurve.DefaultGpu();
    public bool GpuOcOn { get; set; }                         // Overclock inside Custom
    public string GpuOc { get; set; } = "mild";               // preset id (GpuOverclock.Presets)
    public bool GpuOcApplied { get; set; }                     // offsets changed by the app: put the originals back
    public int GpuOcBaseCore { get; set; }                     // original offsets (MHz) before the app changed them
    public int GpuOcBaseMem { get; set; }
    public int CustomBase { get; set; } = 0xAB;                // firmware profile under Custom (default Alien Mode)
    public string CurveProfile { get; set; } = "balanced";   // preset id or "user:<name>" the curves came from
    public List<CurveProfile> UserCurves { get; set; } = new();
    public bool FanExpert { get; set; }             // separate boost for CPU and GPU fans
    public int FanBoostCpu { get; set; }
    public int FanBoostGpu { get; set; }
    public bool ApplyOnStart { get; set; } = true;
    public bool StartMinimized { get; set; } = true;   // start in the tray without opening the window
    public bool GuardLights { get; set; }               // put our lighting back after AWCC's lighting agent repaints it
    public int AwccStartType { get; set; } = 2;          // DellTechHub's start type before AWCC was switched off here
    public bool HoldGpuPower { get; set; } = true;      // re-apply Alien Mode when the GPU is held at the low power limit in it
    // global hotkeys as System.Windows.Forms.Keys values (key | Control | Shift | Alt), 0 = none
    public int OverlayHotkey { get; set; } = (int)(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.O);
    public int ModeHotkey { get; set; } = (int)(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.M);
    public int OcHotkey { get; set; } = (int)(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.C);
    public bool OcWarned { get; set; }                        // the overclock warning was accepted once
    public int OverlayStyleHotkey { get; set; } = (int)(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.O);
    public string? Language { get; set; }           // null = follow Windows
    public string? UntestedModelWarned { get; set; } // model we already warned about (warn once)
    public bool OverlayAuto { get; set; } = true;   // show the overlay while a game is in front
    public List<string> GameApps { get; set; } = new();      // process names the user counts as games (tray → Overlay)
    public List<string> NotGameApps { get; set; } = new();   // and as not games, whatever the detector thinks
    public int OverlayCorner { get; set; }          // 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
    public int OverlayStyle { get; set; }           // 0 full, 1 minimal, 2 horizontal, 3 icon strip
    public int? OverlayX { get; set; }              // hand-placed position (Ctrl+Shift+Alt+arrows), null = corner
    public int? OverlayY { get; set; }

    public bool LightsEnabled { get; set; }         // false = never touch lighting
    public string KeyboardColor { get; set; } = "#00A0FF";
    public int KeyboardBrightness { get; set; } = 100;
    public string ChassisColor { get; set; } = "#00A0FF";
    public int ChassisBrightness { get; set; } = 100;
    public int KeyboardLights { get; set; } = 144;  // v5 keyboard light ids (Area-51 2025 uses 0..135)
    public int ChassisLights { get; set; } = 48;    // v4 AW-ELC ids: back bar 0..30, logo 32, fans 33..34, touchpad 35..44
    public bool KeyboardCustom { get; set; }                   // true = per-key scheme below instead of one color
    public Dictionary<string, string> KeyColors { get; set; } = new();   // key name -> #RRGGBB
    public Dictionary<string, int> KeyIds { get; set; } = new();         // calibration: key name -> LED id
    public string? KeyboardAnimation { get; set; }    // id of the animated template in use (fire, ocean), null = static
    public bool AnimationOn { get; set; } = true;    // animated templates move only when this is on
    public List<UserTemplate> UserTemplates { get; set; } = new();
    public List<string> HiddenTemplates { get; set; } = new();   // built-in templates the user deleted
    public string Look { get; set; } = "dark";              // theme: dark, light, strict
    public bool NumbersOnly { get; set; }                   // main window without graphs
    public List<string> SeenCards { get; set; } = new();         // off-by-default sections already applied once
    public List<string> HiddenCards { get; set; } = new();       // main window cards turned off in Settings: cpu, gpu, ssd, mode, screen, lights

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId.Key);
    static string File => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Settings Load()
    {
        Settings s;
        try { s = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File)) ?? new(); }
        catch { s = new(); }
        s.CpuCurve = AlienHelper.FanCurve.Clean(s.CpuCurve, AlienHelper.FanCurve.DefaultCpu);
        s.GpuCurve = AlienHelper.FanCurve.Clean(s.GpuCurve, AlienHelper.FanCurve.DefaultGpu);
        // sections added later that start hidden: hide each once, then leave it to the user
        foreach (var id in OffByDefault)
            if (!s.SeenCards.Contains(id)) { s.SeenCards.Add(id); if (!s.HiddenCards.Contains(id)) s.HiddenCards.Add(id); }
        return s;
    }

    /// <summary>Main window sections that are off until the user turns them on in Settings.</summary>
    static readonly string[] OffByDefault = { "net", "power" };

    [System.Text.Json.Serialization.JsonIgnore] public bool Transient { get; set; }

    public void Save()
    {
        if (Transient) return;
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, Opts));
        }
        catch { /* settings are a convenience */ }
    }
}
