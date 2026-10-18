using System.Management;

namespace AlienHelper.Hardware;

/// <summary>
/// Thin wrapper over the Alienware ACPI WMI interface (root\WMI:AWCCWmiMethodFunction),
/// the same one Alienware Command Center uses. Every call takes a packed uint32:
/// byte0 = operation, byte1 = arg1, byte2 = arg2.
/// Protocol reference: Linux drivers/platform/x86/dell/alienware-wmi-wmax.c, T-Troll/alienfx-tools.
/// </summary>
public sealed class Awcc : IDisposable
{
    public const byte ProfileManual = 0x00;
    public const byte ProfileGMode = 0xAB;

    public static readonly Dictionary<byte, string> ProfileNames = new()
    {
        [0x00] = "Manual",
        [0x96] = "Quiet",
        [0x97] = "Balanced",
        [0x98] = "Balanced Performance",
        [0x99] = "Performance",
        [0xA0] = "Balanced",
        [0xA1] = "Balanced Performance",
        [0xA2] = "Cool",
        [0xA3] = "Quiet",
        [0xA4] = "Performance",
        [0xA5] = "Battery Saver",
        [0xAB] = "Alien Mode",   // firmware "G-Mode" / Game Shift: max power limits and fans
    };

    public static readonly Dictionary<byte, string> SensorNames = new()
    {
        [0x01] = "CPU",
        [0x02] = "Sensor 2",
        [0x03] = "Front",
        [0x04] = "Sensor 4",
        [0x05] = "Sensor 5",
        [0x06] = "GPU",
    };

    public sealed record Fan(byte Id, byte SensorId, int MaxRpm);
    public sealed record Sensor(byte Id, string Name);

    readonly ManagementObject _obj;
    readonly object _lock = new();

    public List<Fan> Fans { get; } = new();
    public List<Sensor> Sensors { get; } = new();
    public List<byte> Profiles { get; } = new();
    public bool HasGMode { get; }

    Awcc(ManagementObject obj)
    {
        _obj = obj;
        HasGMode = HasMethod("GameShiftStatus");
        Enumerate();
    }

    public static Awcc? TryOpen(out string? error)
    {
        error = null;
        try
        {
            using var cls = new ManagementClass(@"root\WMI", "AWCCWmiMethodFunction", null);
            foreach (ManagementObject o in cls.GetInstances())
                return new Awcc(o);
            error = "AWCCWmiMethodFunction has no instances (run as administrator?)";
        }
        catch (Exception ex) { error = ex.Message; }
        return null;
    }

    bool HasMethod(string name)
    {
        try { using var p = _obj.GetMethodParameters(name); return true; }
        catch (ManagementException) { return false; }
    }

    /// <summary>Raw call. Returns argr, or -1 on WMI failure.</summary>
    public long Call(string method, byte op, byte arg1 = 0, byte arg2 = 0)
    {
        lock (_lock)
        {
            try
            {
                using var inp = _obj.GetMethodParameters(method);
                inp["arg2"] = (uint)(op | (arg1 << 8) | (arg2 << 16));
                using var outp = _obj.InvokeMethod(method, inp, null);
                return Convert.ToInt64(outp["argr"]);
            }
            catch { return -1; }
        }
    }

    static bool Failed(long r) => r < 0 || r >= 0xFFFFFFFE;

    void Enumerate()
    {
        for (byte i = 0; i < 64; i++)
        {
            long id = Call("Thermal_Information", 0x03, i);
            if (Failed(id) || id == 0) break;
            byte low = (byte)(id & 0xFF);
            if (id > 0x100 && id < 0x110)
                Sensors.Add(new Sensor(low, SensorNames.GetValueOrDefault(low, $"Sensor 0x{low:X2}")));
            else if (low > 0x8F)
                Profiles.Add(low);
            else
            {
                long sensor = Call("GetFanSensors", 0x02, low);
                long max = Call("Thermal_Information", 0x09, low);
                Fans.Add(new Fan(low, Failed(sensor) ? (byte)0 : (byte)sensor, Failed(max) ? 0 : (int)max));
            }
        }
        if (HasGMode && !Profiles.Contains(ProfileGMode)) Profiles.Add(ProfileGMode);
    }

    // ---------- reads ----------
    public int GetTemp(byte sensorId)
    {
        long t = Call("Thermal_Information", 0x04, sensorId);
        return Failed(t) || t > 150 ? -1 : (int)t;
    }

    public int GetFanRpm(byte fanId) { long r = Call("Thermal_Information", 0x05, fanId); return Failed(r) ? -1 : (int)r; }
    public int GetFanPercent(byte fanId) { long r = Call("Thermal_Information", 0x06, fanId); return Failed(r) ? -1 : (int)r; }
    public int GetFanBoost(byte fanId) { long r = Call("Thermal_Information", 0x0C, fanId); return Failed(r) ? -1 : (int)r; }
    public int GetProfile() { long r = Call("Thermal_Information", 0x0B); return Failed(r) ? -1 : (int)(r & 0xFF); }
    public int GetGMode() { if (!HasGMode) return -1; long r = Call("GameShiftStatus", 0x02); return Failed(r) ? -1 : (int)r; }

    // ---------- writes (same operations AWCC performs) ----------
    public bool SetProfile(byte profile) => !Failed(Call("Thermal_Control", 0x01, profile));

    /// <summary>Boost only raises fan speed above the BIOS curve; 0 returns control to the BIOS.</summary>
    public bool SetFanBoost(byte fanId, byte boost) => !Failed(Call("Thermal_Control", 0x02, fanId, boost));

    public bool SetGMode(bool on, byte fallbackProfile)
    {
        if (!HasGMode) return false;
        // Op 1 is a toggle on most firmware, so only send it when the state differs.
        bool ok = true;
        if (on) SetProfile(ProfileGMode);
        int cur = GetGMode();
        if (cur >= 0 && (cur != 0) != on)
            ok = !Failed(Call("GameShiftStatus", 0x01, (byte)(on ? 1 : 0)));
        if (!on) SetProfile(fallbackProfile);
        return ok;
    }

    public void Dispose() => _obj.Dispose();
}
