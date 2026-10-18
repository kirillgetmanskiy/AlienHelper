using System.Management;
using System.Runtime.InteropServices;

namespace AlienHelper.Hardware;

/// <summary>Battery state from root\WMI BatteryStatus (the laptop's own battery gauge). Read-only.</summary>
public sealed record BatteryState(bool OnAc, bool Charging, float DischargeW, float ChargeW, int Percent, float RemainingWh)
{
    /// <summary>Hours left at the current draw on battery, or -1.</summary>
    public float HoursLeft => !OnAc && DischargeW > 0.5f ? RemainingWh / DischargeW : -1;

    static DateTime _fullAt = DateTime.MinValue;
    static float _fullWh = -1;

    public static BatteryState? Read()
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"root\WMI", "SELECT PowerOnline, Charging, DischargeRate, ChargeRate, RemainingCapacity FROM BatteryStatus");
            foreach (ManagementObject o in q.Get())
                using (o)
                {
                    bool ac = Convert.ToBoolean(o["PowerOnline"]);
                    bool charging = Convert.ToBoolean(o["Charging"]);
                    float dis = Convert.ToUInt32(o["DischargeRate"]) / 1000f, chg = Convert.ToUInt32(o["ChargeRate"]) / 1000f;
                    float rem = Convert.ToUInt32(o["RemainingCapacity"]) / 1000f;
                    if (DateTime.UtcNow - _fullAt > TimeSpan.FromMinutes(5)) { _fullAt = DateTime.UtcNow; _fullWh = FullWh(); }
                    int pct = _fullWh > 0 ? (int)Math.Round(Math.Clamp(rem / _fullWh * 100, 0, 100)) : -1;
                    return new BatteryState(ac, charging, dis, chg, pct, rem);
                }
        }
        catch { }
        return null;
    }

    static float FullWh()
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
            foreach (ManagementObject o in q.Get()) using (o) return Convert.ToUInt32(o["FullChargedCapacity"]) / 1000f;
        }
        catch { }
        return -1;
    }
}

/// <summary>
/// Windows power mode (Settings → System → Power → Power mode): the overlay on the Balanced plan, set through
/// powrprof the same way the Settings app does.
/// </summary>
public static class WindowsPowerMode
{
    public enum Mode { Unknown = -1, Efficiency, Balanced, Performance }

    static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    static readonly Guid BetterBattery = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    static readonly Guid Balanced = Guid.Empty;
    static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Mode Get()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out var g) != 0) return Mode.Unknown;
            if (g == Efficiency) return Mode.Efficiency;
            if (g == Performance) return Mode.Performance;
            if (g == Balanced || g == BetterBattery) return Mode.Balanced;
        }
        catch { }
        return Mode.Unknown;
    }

    public static bool Set(Mode m)
    {
        var g = m switch { Mode.Efficiency => Efficiency, Mode.Performance => Performance, _ => Balanced };
        try { return PowerSetActiveOverlayScheme(g) == 0; } catch { return false; }
    }

    [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid overlay);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid overlay);
}
