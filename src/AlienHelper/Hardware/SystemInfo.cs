using System.Management;

namespace AlienHelper.Hardware;

/// <summary>Computer manufacturer and model from Win32_ComputerSystem (read once).</summary>
public static class SystemInfo
{
    /// <summary>The only model this app has been tested on.</summary>
    public const string TestedModel = "Alienware 16 Area-51";

    static readonly Lazy<(string maker, string model)> _info = new(() =>
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (ManagementObject o in q.Get())
                using (o) return ((o["Manufacturer"]?.ToString() ?? "").Trim(), (o["Model"]?.ToString() ?? "").Trim());
        }
        catch { }
        return ("", "");
    });

    public static string Manufacturer => _info.Value.maker;
    public static string Model => _info.Value.model;

    /// <summary>Model name without the model code, e.g. "Alienware 16 Area-51".</summary>
    public static string DisplayModel
    {
        get
        {
            var m = Model;
            if (m.Length == 0) return "?";
            var code = ModelCode;
            return code.Length > 0 ? m[..^code.Length].TrimEnd() : m;
        }
    }

    /// <summary>Model code at the end of the name (e.g. "AA16250"), or "" if the name has none.</summary>
    public static string ModelCode
    {
        get
        {
            var parts = Model.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "";
            var last = parts[^1];
            // codes look like letters followed by at least three digits: AA16250, AC16251, AW1234
            return System.Text.RegularExpressions.Regex.IsMatch(last, @"^[A-Za-z]{1,3}\d{3,}$") ? last : "";
        }
    }

    static readonly Lazy<string> _cpu = new(() => ShortName(Query("Win32_Processor").FirstOrDefault() ?? ""));
    static readonly Lazy<string> _gpu = new(() =>
    {
        // the discrete card, not the integrated one
        var names = Query("Win32_VideoController");
        return ShortName(names.FirstOrDefault(n => n.Contains("NVIDIA") || n.Contains("Radeon RX")) ?? names.LastOrDefault() ?? "");
    });

    /// <summary>Short processor name, e.g. "Core Ultra 9 275HX".</summary>
    public static string CpuName => _cpu.Value;
    /// <summary>Short discrete GPU name, e.g. "RTX 5080 Laptop".</summary>
    public static string GpuName => _gpu.Value;

    static List<string> Query(string cls)
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"root\CIMV2", $"SELECT Name FROM {cls}");
            return q.Get().Cast<ManagementObject>().Select(o => (o["Name"]?.ToString() ?? "").Trim()).Where(s => s.Length > 0).ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Drops vendor and marketing words: "NVIDIA GeForce RTX 5080 Laptop GPU" → "RTX 5080 Laptop".</summary>
    public static string ShortName(string name)
    {
        foreach (var junk in new[] { "Intel(R)", "(TM)", "(R)", "AMD", "NVIDIA", "GeForce", "Processor", "GPU", "with Radeon Graphics" })
            name = name.Replace(junk, " ");
        return string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool IsTestedModel => Model.StartsWith(TestedModel, StringComparison.OrdinalIgnoreCase);
}
