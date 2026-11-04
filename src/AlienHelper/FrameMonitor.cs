using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace AlienHelper;

/// <summary>
/// FPS and frame times from ETW Present events (the same source PresentMon uses):
/// Microsoft-Windows-DXGI Present_Start (DX10/11/12 and most Vulkan/OpenGL titles on NVIDIA's DXGI layer)
/// and Microsoft-Windows-D3D9 Present_Start. Needs admin. Runs only while the overlay is visible.
/// </summary>
public sealed class FrameMonitor : IDisposable
{
    static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    const string SessionName = AppId.Key + "-Frames";
    const double WindowMs = 1000;

    public sealed record Stats(int Pid, string Process, double Fps, double FrameMs, double Low1Fps, IReadOnlyList<float> FrameTimes);

    readonly object _lock = new();
    readonly Dictionary<int, List<double>> _presents = new();   // pid -> present timestamps (ms), last ~2 s
    readonly Stopwatch _clock = Stopwatch.StartNew();
    TraceEventSession? _session;
    Thread? _thread;
    readonly Dictionary<int, string> _names = new();
    readonly Dictionary<int, double> _lastWall = new();   // pid -> wall clock of its latest event

    public string? Error { get; private set; }
    public bool Running => _session != null;

    public void Start()
    {
        if (_session != null) return;
        try
        {
            TraceEventSession.GetActiveSession(SessionName)?.Stop(true);   // leftover from a crash
            _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            _session.EnableProvider(Dxgi, TraceEventLevel.Informational);
            _session.EnableProvider(D3D9, TraceEventLevel.Informational);
            _session.Source.AllEvents += OnEvent;
            _thread = new Thread(() => { try { _session?.Source.Process(); } catch { } }) { IsBackground = true, Name = "ETW frames" };
            _thread.Start();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _session?.Dispose();
            _session = null;
        }
    }

    public void Stop()
    {
        _session?.Dispose();
        _session = null;
        lock (_lock) _presents.Clear();
    }

    void OnEvent(TraceEvent e)
    {
        int id = (int)e.ID;
        bool present = (e.ProviderGuid == Dxgi && (id == 42 || id == 55)) || (e.ProviderGuid == D3D9 && id == 1);
        if (!present) return;
        // use the event's own timestamp: real-time ETW delivers events in batches
        double ts = e.TimeStampRelativeMSec;
        lock (_lock)
        {
            if (!_presents.TryGetValue(e.ProcessID, out var list)) _presents[e.ProcessID] = list = new List<double>(512);
            list.Add(ts);
            _lastWall[e.ProcessID] = _clock.Elapsed.TotalMilliseconds;
            if (list.Count > 4000) list.RemoveRange(0, 2000);
        }
    }

    /// <summary>Stats for the foreground process, or the busiest presenting process if the foreground doesn't present.</summary>
    public Stats? Read()
    {
        try { _session?.Flush(); } catch { }
        double wall = _clock.Elapsed.TotalMilliseconds;
        int fg = ForegroundPid();
        int pid = -1;
        double[] times;
        lock (_lock)
        {
            // forget processes that stopped presenting, trim the rest to the last 2 s of their own timeline
            foreach (var k in _presents.Keys.Where(k => wall - _lastWall.GetValueOrDefault(k) > 2000).ToList()) { _presents.Remove(k); _lastWall.Remove(k); }
            foreach (var l in _presents.Values) { double end = l[^1]; l.RemoveAll(t => t < end - 2000); }
            if (_presents.TryGetValue(fg, out var f) && f.Count > 2) pid = fg;
            else
            {
                int dwm = DwmPid();
                var best = _presents.Where(kv => kv.Key != dwm && kv.Key != Environment.ProcessId)
                    .OrderByDescending(kv => kv.Value.Count).FirstOrDefault();
                if (best.Value is { Count: > 2 }) pid = best.Key;
            }
            if (pid < 0) return null;
            times = _presents[pid].ToArray();
        }

        double last = times[^1];
        var recent = times.Where(t => t > last - WindowMs).ToArray();
        double fps = recent.Length * 1000.0 / WindowMs;
        var intervals = new List<float>();
        for (int i = 1; i < times.Length; i++) intervals.Add((float)(times[i] - times[i - 1]));
        double frameMs = recent.Length > 1 ? (recent[^1] - recent[0]) / (recent.Length - 1) : 0;
        double low1 = 0;
        if (intervals.Count >= 20)
        {
            // 1% low = FPS equivalent of the 99th percentile frame time
            var sorted = intervals.OrderBy(x => x).ToList();
            double p99 = sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(sorted.Count * 0.99) - 1)];
            low1 = p99 > 0 ? 1000.0 / p99 : 0;
        }
        return new Stats(pid, Name(pid), fps, frameMs, low1, intervals.TakeLast(180).ToList());
    }

    string Name(int pid)
    {
        if (_names.TryGetValue(pid, out var n)) return n;
        try { using var p = Process.GetProcessById(pid); n = p.ProcessName; } catch { n = $"pid {pid}"; }
        return _names[pid] = n;
    }

    static int _dwm = -1;
    static int DwmPid()
    {
        if (_dwm > 0) return _dwm;
        try { _dwm = Process.GetProcessesByName("dwm").FirstOrDefault()?.Id ?? -1; } catch { }
        return _dwm;
    }

    static int ForegroundPid()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return (int)pid;
    }

    public List<(int pid, string name, int count)> Counts()
    {
        lock (_lock) return _presents.Select(kv => (kv.Key, Name(kv.Key), kv.Value.Count)).OrderByDescending(x => x.Item3).ToList();
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
