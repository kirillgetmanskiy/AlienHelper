using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace AlienHelper;

/// <summary>
/// The exe runs asInvoker. A non-elevated start never touches the firmware; it either
///  - asks an already running instance to show its window, or
///  - explains why admin rights are needed and relaunches itself elevated with the usual UAC prompt
///    (this build never bypasses it), or
///  - when the prompt is declined, runs in limited mode: what works without admin rights (NVIDIA, CPU load, network,
///    charts), with a button to restart elevated.
/// </summary>
public static class Launcher
{
    /// <summary><see cref="Run"/> result: start the app here, non-elevated, in limited mode.</summary>
    public const int Limited = -100;
    /// <summary>Passed to the elevated instance started from limited mode: it waits for the limited one to exit.</summary>
    public const string RestartedArg = "--restarted";

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>This process runs without admin rights (limited mode).</summary>
    public static bool IsLimited { get; private set; } = !IsElevated();

    /// <summary>Screenshot mode ("limited"): draws the windows as they look without admin rights.</summary>
    internal static void PretendLimited() => IsLimited = true;

    /// <summary>WinForms is already set up (the note before the UAC prompt was shown in this process).</summary>
    public static bool UiReady { get; private set; }

    public static int Run(string[] args)
    {
        bool tool = args.Length > 0 && args[0] is "--probe" or "--test" or "--screenshot" or "--fpstest" or "--overlaytest" or "--bench" or "--coretemps" or "--demandtest" or "--curvetest" or "--curvehw" or "--gpuclocks" or "--octest" or "--psys";
        bool wantWindow = !tool && !args.Contains("--tray");

        if (tool) return Elevate(args, wait: true) ? 0 : 1;
        if (ShowSignal.Exists())
        {
            if (wantWindow) ShowSignal.TrySignal();
            return 0;
        }
        ApplicationConfiguration.Initialize();
        UiReady = true;
        using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) UI.Theme.Scale = g.DpiX / 96f;
        var s = Settings.Load();
        L.Init(s.Language);
        UI.Theme.Apply(s.Look);
        // first the note on what the rights are for, then the UAC prompt; either declined: limited mode
        if (!ElevationNotice.Confirm(null, s)) return Limited;
        return Elevate(args, wait: false) ? 0 : Limited;
    }

    /// <summary>Starts this exe elevated (the UAC prompt); false when the prompt was declined.</summary>
    public static bool Elevate(IEnumerable<string> args, bool wait)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (wait) p?.WaitForExit();
            return true;
        }
        catch (Win32Exception) { return false; }   // UAC prompt declined
    }
}

/// <summary>
/// Named events the running instance waits on: one shows its window, the other closes the app
/// (used by the installer, so the app exits normally and resets the fan boost). The elevated instance creates them with
/// an ACL and a low integrity label so the non-elevated launcher can signal it (and nothing more).
/// </summary>
public static class ShowSignal
{
    const string Name = @"Local\AlienHelper.Show";
    public const string ExitName = @"Local\AlienHelper.Exit";

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
