using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AlienHelper;

/// <summary>
/// Autostart without bypassing UAC: an entry under HKCU\...\Run starts the exe non-elevated at sign-in, and the
/// launcher then asks for elevation with the usual UAC prompt (see <see cref="Launcher"/>). No scheduled task with
/// highest privileges is created; tasks left by a build that used them are removed on start.
/// </summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AlienHelper";

    // scheduled tasks created by builds that started the app elevated without a prompt
    const string LegacyLogonTask = "AlienHelper", LegacyLaunchTask = "AlienHelper-NoUAC";

    /// <summary>
    /// False in the MSIX package: there writes to HKCU are kept in the package's private registry, so Windows would never
    /// see a Run entry. The option is hidden there.
    /// </summary>
    public static bool Supported => !IsPackaged();

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string;
    }

    public static bool Enable()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            // the installed exe when there is one, so that running another copy never moves autostart to it
            k.SetValue(ValueName, $"\"{InstalledExe() ?? Environment.ProcessPath}\" --tray");
            return true;
        }
        catch (Exception ex) { Log.Warn("autostart on: " + ex.Message); return false; }
    }

    public static bool Disable()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            k?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) { Log.Warn("autostart off: " + ex.Message); return false; }
    }

    /// <summary>
    /// On start: keeps the Run entry pointing at this exe (e.g. after it was moved), and replaces the elevated scheduled
    /// tasks of a build that bypassed UAC: autostart moves to the Run entry, starting without a prompt is dropped.
    /// </summary>
    public static void Refresh()
    {
        if (!Supported) return;
        if (TaskExists(LegacyLogonTask))
        {
            Enable();
            DeleteTask(LegacyLogonTask);
            Log.Info("autostart: moved from the elevated scheduled task to the Run entry (UAC prompt at sign-in)");
        }
        if (TaskExists(LegacyLaunchTask))
        {
            DeleteTask(LegacyLaunchTask);
            Log.Info("removed the scheduled task that started the app without a UAC prompt");
        }
        if (IsEnabled()) Enable();
    }

    static bool TaskExists(string task) => Schtasks("/Query", "/TN", task) == 0;
    static void DeleteTask(string task) => Schtasks("/Delete", "/F", "/TN", task);

    static int Schtasks(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    /// <summary>The exe under the installer's folder, or null when the app is not installed or the file is gone.</summary>
    static string? InstalledExe()
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F3C2B1E-8A47-4D5C-9E21-A1E7F0B4C3D9}_is1";
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            if (k?.GetValue("InstallLocation") is not string dir) return null;
            string exe = Path.Combine(dir, "AlienHelper.exe");
            return File.Exists(exe) ? exe : null;
        }
        catch { return null; }
    }

    static bool IsPackaged()
    {
        int len = 0;
        return GetCurrentPackageFullName(ref len, null) != AppModelErrorNoPackage;
    }

    const int AppModelErrorNoPackage = 15700;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, char[]? name);
}
