using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace AlienHelper;

/// <summary>
/// Task Scheduler tasks for the current user with highest privileges, so the elevated app starts without
/// a UAC prompt. Both are created only when the user switches the matching option on in the main window:
///  - "AlienHelper"        logon trigger, starts in the tray (autostart);
///  - "AlienHelper-NoUAC"  no trigger, started on demand by a non-elevated launch of the exe.
/// The test build's are "AlienHelperTest" and "AlienHelperTest-NoUAC" (<see cref="AppId.Key"/>).
/// No service is installed; turning an option off deletes its task.
/// </summary>
public static class Autostart
{
    public const string LogonTask = AppId.Key;
    public const string LaunchTask = AppId.Key + "-NoUAC";

    public static bool IsEnabled() => Exists(LogonTask);
    public static bool Enable() => Create(LogonTask, logon: true);
    public static bool Disable() => Delete(LogonTask);

    public static bool NoUacEnabled() => Exists(LaunchTask);
    public static bool EnableNoUac() => Create(LaunchTask, logon: false);
    public static bool DisableNoUac() => Delete(LaunchTask);

    public static bool Exists(string task) => Run("/Query", "/TN", task) == 0;
    public static bool Delete(string task) => Run("/Delete", "/F", "/TN", task) == 0;
    public static bool Start(string task) => Run("/Run", "/TN", task) == 0;

    /// <summary>Re-registers existing tasks so they point at the current exe path (e.g. after it was moved).</summary>
    public static void Refresh()
    {
        if (Exists(LogonTask)) Enable();
        if (Exists(LaunchTask)) EnableNoUac();
    }

    static bool Create(string task, bool logon)
    {
        string exe = ExePath();
        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        string trigger = logon
            ? $"<LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT10S</Delay></LogonTrigger>"
            : "";
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{AppId.Title}: {(logon ? "start in tray at logon" : "start without a UAC prompt")}</Description></RegistrationInfo>
              <Triggers>{trigger}</Triggers>
              <Principals>
                <Principal id="Author"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>--tray</Arguments></Exec>
              </Actions>
            </Task>
            """;
        string tmp = Path.Combine(Path.GetTempPath(), $"{task}-task.xml");
        File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
        try { return Run("/Create", "/F", "/TN", task, "/XML", tmp) == 0; }
        finally { File.Delete(tmp); }
    }

    /// <summary>
    /// The exe the tasks start: the installed one when the app is installed, so that running another copy never
    /// moves autostart to it. In the MSIX package the install folder changes with every version and is not meant
    /// to be started directly, so the tasks use the package's execution alias instead.
    /// </summary>
    static string ExePath()
    {
        if (!IsPackaged()) return InstalledExe() ?? Environment.ProcessPath!;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", AppId.Exe);
    }

    /// <summary>The exe under the installer's folder, or null when the app is not installed or the file is gone.</summary>
    static string? InstalledExe()
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId.InstallerGuid + "_is1";
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            if (k?.GetValue("InstallLocation") is not string dir) return null;
            string exe = Path.Combine(dir, AppId.Exe);
            return File.Exists(exe) ? exe : null;
        }
        catch { return null; }
    }

    /// <summary>Runs from an MSIX package (the Store build).</summary>
    public static bool IsPackaged()
    {
        int len = 0;
        return GetCurrentPackageFullName(ref len, null) != AppModelErrorNoPackage;
    }

    const int AppModelErrorNoPackage = 15700;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    static int Run(params string[] args)
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
}
