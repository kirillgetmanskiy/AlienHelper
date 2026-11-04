using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using AlienHelper.Hardware;

namespace AlienHelper;

/// <summary>
/// Builds a ZIP for the developer: system/hardware summary, recent logs, settings and snapshots of the
/// app's own open windows (never the whole screen). Saved on the Desktop.
/// </summary>
public static class Diagnostics
{
    public static string CreateReport(Controller c, SensorMonitor? mon)
    {
        string name = $"{AppId.Key}-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        string zipPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddText(zip, "report.txt", Summary(c, mon));

        // recent logs (last 7 days) and the settings file
        if (Directory.Exists(Log.LogDir))
            foreach (var f in Directory.EnumerateFiles(Log.LogDir, "*.log").OrderBy(f => f))
                AddFile(zip, f, "logs/" + Path.GetFileName(f));
        AddFile(zip, Path.Combine(Log.Dir, "error.log"), "logs/error.log");
        AddFile(zip, Path.Combine(Log.Dir, "settings.json"), "settings.json");

        // snapshots of the app's own visible windows
        int n = 0;
        foreach (Form f in Application.OpenForms)
        {
            if (!f.Visible || f.Width < 10 || f.Height < 10) continue;
            try
            {
                using var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                var e = zip.CreateEntry($"screenshots/{++n:00}-{f.GetType().Name}.png");
                using var s = e.Open();
                bmp.Save(s, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex) { Log.Warn($"snapshot of {f.GetType().Name} failed: {ex.Message}"); }
        }
        Log.Info($"diagnostics report saved: {zipPath} ({n} snapshots)");
        return zipPath;
    }

    /// <summary>Opens Explorer with the report selected.</summary>
    public static void Reveal(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }

    /// <summary>Opens the mail app with the developer's address and a subject.</summary>
    public static void MailDeveloper()
    {
        try
        {
            var v = typeof(Diagnostics).Assembly.GetName().Version;
            string subject = Uri.EscapeDataString($"{Log.AppTitle} {v?.ToString(3)} — {SystemInfo.Model}");
            Process.Start(new ProcessStartInfo($"mailto:{Log.ContactEmail}?subject={subject}") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("mailto failed: " + ex.Message); }
    }

    static string Summary(Controller c, SensorMonitor? mon)
    {
        var sb = new StringBuilder();
        var v = typeof(Diagnostics).Assembly.GetName().Version;
        sb.AppendLine($"{Log.AppTitle} {v}   report {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
        sb.AppendLine($"Elevated: {Launcher.IsElevated()}   UI language: {L.Current}");
        sb.AppendLine($"Computer: {SystemInfo.Manufacturer} / {SystemInfo.Model}");
        sb.AppendLine($"BIOS: {Wmi("Win32_BIOS", "SMBIOSBIOSVersion")}   CPU: {Wmi("Win32_Processor", "Name")}");
        sb.AppendLine($"GPU: {Wmi("Win32_VideoController", "Name", all: true)}");
        sb.AppendLine();

        var a = c.Awcc;
        sb.AppendLine(a == null ? $"AWCC WMI: unavailable ({c.AwccError})" : "AWCC WMI: available");
        if (a != null)
        {
            sb.AppendLine($"  profiles: {string.Join(", ", a.Profiles.Select(p => $"0x{p:X2}"))}  current: 0x{a.GetProfile():X2}  G-Mode: {a.GetGMode()}");
            sb.AppendLine($"  fans: {string.Join(", ", a.Fans.Select(f => $"0x{f.Id:X2} rpm={a.GetFanRpm(f.Id)} max={f.MaxRpm}"))}");
            sb.AppendLine($"  sensors: {string.Join(", ", a.Sensors.Select(s => $"{s.Name}={a.GetTemp(s.Id)}°"))}");
        }
        sb.AppendLine($"Lighting devices: {(c.Lights.Count == 0 ? "none" : string.Join(", ", c.Lights.Select(l => $"{l.Name} {l.Vid:X4}:{l.Pid:X4} v{(int)l.Version}")))}");
        sb.AppendLine($"Keyboard: custom={c.S.KeyboardCustom} animation={c.S.KeyboardAnimation ?? "-"} running={c.AnimationId ?? "-"} remapped keys={c.S.KeyIds.Count}");
        sb.AppendLine($"Screen brightness: {ScreenBrightness.Read()} (live events: {c.Screen?.LiveUpdates})");
        if (mon != null)
        {
            string gpus = mon.Gpus.Count == 0 ? "-" : string.Join(", ", mon.Gpus.Select(x => x.Stats is { } g ? $"{x.Name} {g.Load}% {g.CoreMhz}MHz {g.PowerW:0}W vram={g.MemTemp}°" : x.Name));
            sb.AppendLine($"Monitor: cpuLoad={mon.CpuLoad:0} pClock={mon.PClockMhz:0} cpuW={mon.CpuPowerW:0} gpus=[{gpus}] disks={string.Join(" ", mon.Drives.Select(d => $"{d.Label}={d.Temp}°/{d.ReadIops:0}r/{d.WriteIops:0}w"))}");
        }
        sb.AppendLine($"Fan boost: {c.S.FanBoost}   apply on start: {c.S.ApplyOnStart}   start minimized: {c.S.StartMinimized}");
        sb.AppendLine($"Hotkeys: overlay={HotKey.Describe(c.S.OverlayHotkey)} style={HotKey.Describe(c.S.OverlayStyleHotkey)} mode={HotKey.Describe(c.S.ModeHotkey)}");
        sb.AppendLine($"Tasks: autostart={Autostart.IsEnabled()} noUAC={Autostart.NoUacEnabled()}");
        return sb.ToString();
    }

    static string Wmi(string cls, string prop, bool all = false)
    {
        try
        {
            using var q = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}");
            var values = q.Get().Cast<ManagementObject>().Select(o => o[prop]?.ToString()?.Trim() ?? "").Where(s => s.Length > 0).ToList();
            return all ? string.Join("; ", values) : values.FirstOrDefault() ?? "?";
        }
        catch { return "?"; }
    }

    static void AddText(ZipArchive zip, string name, string text)
    {
        var e = zip.CreateEntry(name);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(true));
        w.Write(text);
    }

    static void AddFile(ZipArchive zip, string path, string name)
    {
        try
        {
            if (!File.Exists(path)) return;
            // the log may be open for appending; read it shared
            using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = zip.CreateEntry(name).Open();
            src.CopyTo(dst);
        }
        catch { }
    }
}
