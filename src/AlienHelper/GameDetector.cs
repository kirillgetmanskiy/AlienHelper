using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AlienHelper;

/// <summary>
/// Tells whether the window in front is a game, not just any full-screen window. In order:
/// 1. the user's own word for the app (tray → Overlay → "Count as a game" / "Not a game");
/// 2. apps that are never games (browsers, players, the shell);
/// 3. Windows' own list of games it has seen run (Game Bar / Game Mode, HKCU\System\GameConfigStore);
/// 4. an exe inside a game library: Steam, Epic, GOG, EA, Ubisoft, Xbox / Game Pass;
/// 5. anything else counts only while it covers the screen and keeps the GPU's 3D engine busy, as a game does and a
///    maximized editor, messenger or video does not.
/// A known game (3, 4) counts in a window too, unless the window is small (a launcher or a dialog).
/// </summary>
public static class GameDetector
{
    internal static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "vlc", "mpc-hc64", "mpc-be64",
        "PotPlayerMini64", "Video.UI", "Microsoft.Media.Player", "POWERPNT", "AlienHelper", "ApplicationFrameHost",
        "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp",
        // launchers live next to their games but are not games
        "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "GalaxyClient", "EADesktop", "EALauncher",
        "Battle.net", "UbisoftConnect", "upc", "RiotClientServices", "RiotClientUx", "XboxPcApp", "GameBar",
    };

    static readonly string[] Libraries =
    {
        @"\steamapps\common\", @"\Epic Games\", @"\GOG Galaxy\Games\", @"\GOG Games\", @"\EA Games\",
        @"\Ubisoft Game Launcher\games\", @"\XboxGames\", @"\ModifiableWindowsApps\", @"\Riot Games\",
    };
    static readonly string[] NotLibraries = { @"\Epic Games\Launcher\", @"\Riot Games\Riot Client\" };

    const float Busy3D = 25f;           // % of a 3D engine an unknown full-screen app must keep busy
    const double SmallWindow = 0.4;     // a known game's window below this share of the monitor is a launcher or a dialog

    public enum Reason { None, User, NotUser, Never, Windows, Library, FullScreen3D }

    static Settings? _s;
    static long _until;                 // cache: the overlay asks once a second, the detector once every two
    static bool _last;
    static string _lastLogged = "";
    static HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    static long _knownUntil;
    static readonly Dictionary<int, (string name, string path)> _procs = new();
    static Dictionary<string, CounterSample> _prev3D = new();

    /// <summary>The app in front last time that was not the shell or the tray: what the tray menu offers to mark.</summary>
    public static string? LastApp { get; private set; }

    public static void Init(Settings s) => _s = s;

    /// <summary>True while a game is the foreground window.</summary>
    public static bool InFront()
    {
        long now = Environment.TickCount64;
        if (now < _until) return _last;
        _until = now + 900;
        var (game, reason, name) = Check();
        _last = game;
        // logged when a game comes or goes, not on every switch between ordinary windows
        string line = game ? $"{name} ({reason})" : "";
        if (line != _lastLogged) { _lastLogged = line; Log.Info(game ? $"game detect: {line}" : "game detect: no game in front"); }
        return game;
    }

    /// <summary>What the user said about an app: true a game, false not a game, null let the detector decide.</summary>
    public static bool? UserSays(string app) =>
        _s == null ? null : _s.GameApps.Contains(app, StringComparer.OrdinalIgnoreCase) ? true
        : _s.NotGameApps.Contains(app, StringComparer.OrdinalIgnoreCase) ? false : null;

    public static void Mark(string app, bool? game)
    {
        if (_s == null) return;
        _s.GameApps.RemoveAll(a => a.Equals(app, StringComparison.OrdinalIgnoreCase));
        _s.NotGameApps.RemoveAll(a => a.Equals(app, StringComparison.OrdinalIgnoreCase));
        if (game == true) _s.GameApps.Add(app);
        if (game == false) _s.NotGameApps.Add(app);
        _s.Save();
        _until = 0;
    }

    static (bool, Reason, string) Check()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return (false, Reason.None, "");
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "NotifyIconOverflowWindow" or "#32768") return (false, Reason.None, "");
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return (false, Reason.None, "");
        var (name, path) = Proc((int)pid);
        if (name.Length == 0) return (false, Reason.None, "");
        LastApp = name;

        if (UserSays(name) is { } said) return (said, said ? Reason.User : Reason.NotUser, name);
        if (NotGames.Contains(name)) return (false, Reason.Never, name);

        double share = Share(hwnd, out bool covers);
        if (share < 0) return (false, Reason.None, name);
        if (path.Length > 0)
        {
            if (Known().Contains(path)) return (share >= SmallWindow, Reason.Windows, name);
            if (InLibrary(path)) return (share >= SmallWindow, Reason.Library, name);
        }
        if (!covers) return (false, Reason.None, name);
        return (Gpu3D((int)pid) >= Busy3D, Reason.FullScreen3D, name);
    }

    static bool InLibrary(string path) =>
        Libraries.Any(l => path.Contains(l, StringComparison.OrdinalIgnoreCase))
        && !NotLibraries.Any(l => path.Contains(l, StringComparison.OrdinalIgnoreCase));

    /// <summary>Exe paths of the games Windows has seen run; re-read once a minute.</summary>
    static HashSet<string> Known()
    {
        long now = Environment.TickCount64;
        if (now < _knownUntil) return _known;
        _knownUntil = now + 60_000;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
            foreach (var id in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var k = root!.OpenSubKey(id);
                if (k?.GetValue("MatchedExeFullPath") is string p && p.Length > 0) set.Add(p);
            }
        }
        catch (Exception ex) { Log.Warn("game detect: GameConfigStore: " + ex.Message); }
        return _known = set;
    }

    /// <summary>The window's share of its monitor (0..1, -1 when unknown); covers = the whole monitor.</summary>
    static double Share(IntPtr hwnd, out bool covers)
    {
        covers = false;
        if (!GetWindowRect(hwnd, out var wr)) return -1;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi)) return -1;
        var m = mi.rcMonitor;
        covers = wr.Left <= m.Left && wr.Top <= m.Top && wr.Right >= m.Right && wr.Bottom >= m.Bottom;
        double w = Math.Max(0, Math.Min(wr.Right, m.Right) - Math.Max(wr.Left, m.Left));
        double h = Math.Max(0, Math.Min(wr.Bottom, m.Bottom) - Math.Max(wr.Top, m.Top));
        double area = (double)(m.Right - m.Left) * (m.Bottom - m.Top);
        return area > 0 ? w * h / area : -1;
    }

    /// <summary>
    /// How busy the process keeps its busiest 3D engine, %, from the "GPU Engine" counters (no admin needed). The first
    /// call for a process only takes the starting sample and returns 0; the detector asks again two seconds later.
    /// </summary>
    static float Gpu3D(int pid)
    {
        var cur = new Dictionary<string, CounterSample>();
        float best = 0;
        try
        {
            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            if (data == null) return 0;
            string prefix = $"pid_{pid}_";
            foreach (InstanceData d in data.Values)
            {
                if (!d.InstanceName.StartsWith(prefix, StringComparison.Ordinal) || !d.InstanceName.EndsWith("engtype_3D", StringComparison.Ordinal)) continue;
                cur[d.InstanceName] = d.Sample;
                if (_prev3D.TryGetValue(d.InstanceName, out var p)) best = Math.Max(best, CounterSample.Calculate(p, d.Sample));
            }
        }
        catch (Exception ex) { Log.Warn("game detect: GPU counters: " + ex.Message); }
        _prev3D = cur;
        return best;
    }

    static (string name, string path) Proc(int pid)
    {
        if (_procs.TryGetValue(pid, out var c)) return c;
        string name = "", path = "";
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
            var h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION: works on others' processes without admin
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int len = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref len)) path = sb.ToString();
                CloseHandle(h);
            }
        }
        catch { }
        if (_procs.Count > 200) _procs.Clear();
        if (name.Length > 0) _procs[pid] = (name, path);
        return (name, path);
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
