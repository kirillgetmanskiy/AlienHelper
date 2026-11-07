using System.Diagnostics;

namespace AlienHelper;

/// <summary>
/// Diagnostics for game stutter. The app's own work (sensor reads, overlay paints, keyboard frames, mode switches)
/// is noted in a short ring; when a game's frame takes long (<see cref="FrameMonitor"/> sees it while the overlay is
/// on), the log says what the app was doing at that moment.
/// </summary>
public static class Hitches
{
    readonly record struct Act(string Name, DateTime Start, double Ms);

    static readonly Act[] _ring = new Act[1024];
    static int _next;
    static readonly object _lock = new();
    static DateTime _lastLogged;

    /// <summary>Notes a piece of work that started at <paramref name="start"/> and took <paramref name="ms"/>.</summary>
    public static void Mark(string name, DateTime start, double ms)
    {
        lock (_lock) { _ring[_next] = new Act(name, start, ms); _next = (_next + 1) % _ring.Length; }
    }

    /// <summary>Times the work until the result is disposed: <c>using var _ = Hitches.Time("name");</c></summary>
    public static Span Time(string name) => new(name);

    public readonly struct Span : IDisposable
    {
        readonly string _name; readonly DateTime _start; readonly long _t0;
        public Span(string name) { _name = name; _start = DateTime.Now; _t0 = Stopwatch.GetTimestamp(); }
        public void Dispose() => Mark(_name, _start, Stopwatch.GetElapsedTime(_t0).TotalMilliseconds);
    }

    /// <summary>A frame of <paramref name="process"/> that ended at <paramref name="end"/> took <paramref name="ms"/>.</summary>
    public static void Frame(string process, DateTime end, double ms, double usualMs)
    {
        if ((end - _lastLogged).TotalMilliseconds < 1000) return;   // one line a second at most
        _lastLogged = end;
        var from = end.AddMilliseconds(-ms - 100);
        List<Act> near;
        lock (_lock)
            near = _ring.Where(a => a.Name != null && a.Start <= end && a.Start.AddMilliseconds(a.Ms) >= from).ToList();
        string what = near.Count == 0 ? "nothing"
            : string.Join(", ", near.GroupBy(a => a.Name).OrderByDescending(g => g.Max(a => a.Ms))
                .Select(g => g.Count() > 1 ? $"{g.Key} {g.Count()}x up to {g.Max(a => a.Ms):0} ms" : $"{g.Key} {g.Max(a => a.Ms):0} ms"));
        Log.Info($"hitch: {process} frame {ms:0} ms (usually {usualMs:0} ms) at {end.AddMilliseconds(-ms):HH:mm:ss.fff}; the app meanwhile: {what}");
    }
}
