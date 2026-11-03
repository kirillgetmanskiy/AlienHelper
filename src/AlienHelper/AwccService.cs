using System.Diagnostics;
using Microsoft.Win32;

namespace AlienHelper;

/// <summary>
/// Switches Alienware Command Center off and back on. AWCC's agents (modes and power, lighting) are started by Dell's
/// TechHub service, which starts again any agent that is closed, so the service itself is stopped and disabled; turning
/// it back on restores its old start type and starts it. Nothing is uninstalled. This app talks to the BIOS's WMI
/// interface, which works without AWCC. Needs admin rights.
/// </summary>
static class AwccService
{
    public const string Service = "DellTechHub";
    const string Key = @"SYSTEM\CurrentControlSet\Services\" + Service;
    const int Disabled = 4;

    // what can be left once the service stopped: its agents and the AWCC window
    static readonly string[] Leftovers = { "AWCC.SCSubAgent", "AWCC.UCSubAgent", "AlienFXSubAgent", "AWCC" };

    /// <summary>The service's start type (2 automatic, 3 manual, 4 disabled), -1 when there is no such service.</summary>
    static int StartType()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(Key);
            return k?.GetValue("Start") is int v ? v : -1;
        }
        catch { return -1; }
    }

    public static bool Installed => StartType() > 0;

    public static bool IsOff => StartType() == Disabled;

    public static bool TurnOff(Settings s)
    {
        int start = StartType();
        if (start < 0) return false;
        if (start != Disabled) { s.AwccStartType = start; s.Save(); }
        if (Sc("config", Service, "start=", "disabled") != 0) { Log.Warn("awcc off: the service could not be disabled"); return false; }
        Sc("stop", Service);
        // the service stops its agents itself; whatever is still running after a while is closed
        var until = Environment.TickCount64 + 10000;
        while (Environment.TickCount64 < until && Process.GetProcessesByName("Dell.TechHub").Length > 0) Thread.Sleep(300);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.ProcessName.StartsWith("Dell.TechHub", StringComparison.OrdinalIgnoreCase) || Leftovers.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                    p.Kill();
            }
            catch { }
            finally { p.Dispose(); }
        }
        Log.Info($"awcc off: {Service} stopped and disabled (was start type {start})");
        return true;
    }

    public static bool TurnOn(Settings s)
    {
        string type = s.AwccStartType == 3 ? "demand" : "auto";
        if (Sc("config", Service, "start=", type) != 0) { Log.Warn("awcc on: the service could not be enabled"); return false; }
        Sc("start", Service);
        Log.Info($"awcc on: {Service} start type {type}, started");
        return true;
    }

    static int Sc(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return p.ExitCode;
        }
        catch { return -1; }
    }
}
