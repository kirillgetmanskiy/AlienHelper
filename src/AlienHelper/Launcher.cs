using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace AlienHelper;

/// <summary>
/// The exe runs asInvoker. A non-elevated start never touches hardware; it either
///  - asks an already running (elevated) instance to show its window,
///  - starts the on-demand scheduled task the user enabled (no UAC prompt), or
///  - relaunches itself elevated (one UAC prompt).
/// </summary>
public static class Launcher
{
    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Run(string[] args)
    {
        bool tool = args.Length > 0 && args[0] is "--probe" or "--test" or "--screenshot" or "--fpstest" or "--overlaytest" or "--bench" or "--coretemps" or "--demandtest" or "--curvetest" or "--curvehw" or "--gpuclocks" or "--octest" or "--psys";
        bool wantWindow = !tool && !args.Contains("--tray");

        if (!tool)
        {
            if (ShowSignal.Exists())
            {
                if (wantWindow) ShowSignal.TrySignal();
                return 0;
            }
            if (Autostart.Exists(Autostart.LaunchTask) && Autostart.Start(Autostart.LaunchTask))
            {
                // the task starts the app in the tray; ask it to open the window once it is up,
                // unless the user chose to start minimized
                if (wantWindow && !Settings.Load().StartMinimized)
                    for (int i = 0; i < 50 && !ShowSignal.TrySignal(); i++) Thread.Sleep(200);
                return 0;
            }
        }
        return Elevate(args, wait: tool);
    }

    static int Elevate(string[] args, bool wait)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (wait) p?.WaitForExit();
            return 0;
        }
        catch (Win32Exception) { return 1; }   // UAC prompt cancelled
    }
}

/// <summary>
/// Named events the running instance waits on: one shows its window, the other closes the app
/// (used by the installer, so the app exits normally and resets the fan boost). The elevated instance creates them with
/// an ACL and a low integrity label so the non-elevated launcher can signal it (and nothing more).
/// </summary>
public static class ShowSignal
{
    const string Name = @"Local\" + AppId.Key + ".Show";
    public const string ExitName = @"Local\" + AppId.Key + ".Exit";

    public static EventWaitHandle Create(string name = Name)
    {
        // Everyone: SYNCHRONIZE | EVENT_MODIFY_STATE; mandatory label Low, no-write-up.
        // Passed at creation time, so no SeSecurityPrivilege is needed for the label.
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:(A;;0x100002;;;WD)S:(ML;;NW;;;LW)", 1, out var sd, IntPtr.Zero))
            throw new Win32Exception();
        try
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            var h = CreateEvent(ref sa, false, false, name);
            if (h.IsInvalid) throw new Win32Exception();
            return new EventWaitHandle(false, EventResetMode.AutoReset) { SafeWaitHandle = h };
        }
        finally { LocalFree(sd); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr sd, IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeWaitHandle CreateEvent(ref SECURITY_ATTRIBUTES sa, bool manualReset, bool initialState, string name);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);

    public static bool Exists()
    {
        if (!EventWaitHandle.TryOpenExisting(Name, out var h)) return false;
        h.Dispose();
        return true;
    }

    public static bool TrySignal(string name = Name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var h)) return false;
        using (h) return h.Set();
    }
}
