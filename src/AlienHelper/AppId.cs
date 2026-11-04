namespace AlienHelper;

/// <summary>
/// Prod or test build: build.ps1 builds the test channel on the test branches (test, test-no-uac). The test build
/// installs and runs next to the prod one, with its own exe, settings folder, autostart entry, named events and
/// uninstall entry. Only one of them drives the hardware at a time: starting one closes the other (<see cref="TakeOver"/>).
/// </summary>
public static class AppId
{
#if TEST_CHANNEL
    public const bool Test = true;
    /// <summary>Name of the exe, folders, tasks and events.</summary>
    public const string Key = "AlienHelperTest";
    /// <summary>Name shown to people.</summary>
    public const string Title = "Alien Helper Test";
    /// <summary>Inno Setup AppId: the uninstall entry is Uninstall\{guid}_is1.</summary>
    public const string InstallerGuid = "{B2D84E6A-5C1F-4A93-8E7B-3F6D0C9A2E15}";
    const string OtherKey = "AlienHelper";
#else
    public const bool Test = false;
    public const string Key = "AlienHelper";
    public const string Title = "Alien Helper";
    public const string InstallerGuid = "{6F3C2B1E-8A47-4D5C-9E21-A1E7F0B4C3D9}";
    const string OtherKey = "AlienHelperTest";
#endif
    public const string Exe = Key + ".exe";

    /// <summary>
    /// The other channel's build is running: started at sign-in (--tray), this one gives way to it; started by hand, it
    /// asks the other one to exit (it resets the fan boost on the way out) and waits for it. False: do not start.
    /// </summary>
    public static bool TakeOver(bool tray)
    {
        string show = $@"Local\{OtherKey}.Show";
        if (!Running(show)) return true;
        if (tray) { Log.Info($"{OtherKey} is running: not starting at sign-in"); return false; }
        Log.Info($"{OtherKey} is running: asking it to exit");
        ShowSignal.TrySignal($@"Local\{OtherKey}.Exit");
        for (int i = 0; i < 40 && Running(show); i++) Thread.Sleep(250);
        return true;
    }

    static bool Running(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var h)) return false;
        h.Dispose();
        return true;
    }

    /// <summary>The test build's first start: begins with a copy of the prod settings and themes.</summary>
    public static void SeedSettings()
    {
        if (!Test) return;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string from = Path.Combine(appData, OtherKey), to = Path.Combine(appData, Key);
        if (Directory.Exists(to) || !File.Exists(Path.Combine(from, "settings.json"))) return;
        try
        {
            Directory.CreateDirectory(to);
            File.Copy(Path.Combine(from, "settings.json"), Path.Combine(to, "settings.json"));
            if (Directory.Exists(Path.Combine(from, "themes")))
            {
                Directory.CreateDirectory(Path.Combine(to, "themes"));
                foreach (var f in Directory.GetFiles(Path.Combine(from, "themes"), "*.json"))
                    File.Copy(f, Path.Combine(to, "themes", Path.GetFileName(f)));
            }
        }
        catch { }   // a fresh start with defaults is fine too
    }
}
