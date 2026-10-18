using System.Management;

namespace AlienHelper.Hardware;

/// <summary>
/// Built-in panel brightness through Windows WMI (root\WMI: WmiMonitorBrightness / WmiMonitorBrightnessMethods).
/// <see cref="Changed"/> fires from WmiMonitorBrightnessEvent, so changes made with the brightness keys,
/// the Windows slider or this app show up immediately without polling.
/// </summary>
public sealed class ScreenBrightness : IDisposable
{
    ManagementEventWatcher? _watcher;

    /// <summary>Raised on a WMI thread with the new level (0..100).</summary>
    public event Action<int>? Changed;

    public static ScreenBrightness? TryOpen()
    {
        if (Read() < 0) return null;   // no internal panel that supports WMI brightness
        var s = new ScreenBrightness();
        try
        {
            s._watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"),
                new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            s._watcher.EventArrived += (_, e) =>
            {
                try { s.Changed?.Invoke(Convert.ToInt32(e.NewEvent["Brightness"])); } catch { }
            };
            s._watcher.Start();
        }
        catch { s._watcher = null; }   // reading and setting still work; the UI then just polls
        return s;
    }

    /// <summary>Current level 0..100, or -1 if unavailable.</summary>
    public static int Read()
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness, Active FROM WmiMonitorBrightness");
            foreach (ManagementObject o in q.Get())
                using (o)
                    if (o["Active"] is true) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return -1;
    }

    public bool Set(int level)
    {
        level = Math.Clamp(level, 0, 100);
        try
        {
            using var q = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    if (o["Active"] is not true) continue;
                    o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)level });   // timeout 1 s, level
                    return true;
                }
        }
        catch { }
        return false;
    }

    public bool LiveUpdates => _watcher != null;

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { }
        _watcher?.Dispose();
    }
}
