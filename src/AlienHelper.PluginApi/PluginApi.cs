using System.Drawing;

namespace AlienHelper.Plugins;

/// <summary>The version of this API.</summary>
public static class PluginApi
{
    /// <summary>
    /// Major version of the API. A plugin states the one it was built for in plugin.json ("api"); the app loads it only
    /// when they are equal. Additions that keep old plugins working do not change it.
    /// </summary>
    public const int Version = 1;
}

/// <summary>
/// A plugin. The app creates the first public class that implements this, with a public constructor without
/// parameters, from the assembly plugin.json names. Every call comes on the UI thread.
/// </summary>
public interface IPlugin
{
    /// <summary>Called after loading at start-up, or when the user turns the plugin on in Settings.</summary>
    void Start(IPluginHost host);

    /// <summary>
    /// Called when the user turns the plugin off and when the app exits. Stop threads, close files, give up devices.
    /// Hotkeys, menu items and event handlers the host gave out are removed by the host itself. The plugin's code
    /// stays loaded until the app restarts.
    /// </summary>
    void Stop();

    /// <summary>Items of the plugin's submenu in the tray, asked each time the menu opens. None by default.</summary>
    IEnumerable<PluginMenuItem> MenuItems() => Array.Empty<PluginMenuItem>();

    /// <summary>
    /// The plugin's settings, shown in a window opened from Settings → Plugins and from the tray; null (the default) for
    /// none. Asked each time the window opens; the control's size is the window's.
    /// </summary>
    Control? CreateSettingsView() => null;
}

/// <summary>An item of a plugin's submenu in the tray.</summary>
/// <param name="Text">Shown text.</param>
/// <param name="OnClick">Called on the UI thread when the item is clicked.</param>
public sealed record PluginMenuItem(string Text, Action OnClick)
{
    /// <summary>Drawn with a check mark.</summary>
    public bool Checked { get; init; }

    /// <summary>False: shown greyed out.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>What the app offers a plugin. One per plugin; valid between <see cref="IPlugin.Start"/> and <see cref="IPlugin.Stop"/>.</summary>
public interface IPluginHost
{
    /// <summary>The app's version, e.g. "0.1.19".</summary>
    string AppVersion { get; }

    /// <summary>True in the test build (Alien Helper Test).</summary>
    bool IsTestBuild { get; }

    /// <summary>True when the app runs with admin rights.</summary>
    bool IsElevated { get; }

    /// <summary>The UI language: "ru", "en", "fr", "es", "ja" or "zh".</summary>
    string Language { get; }

    /// <summary>Raised when the user switches the language.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>A folder only this plugin uses, for its settings and data; it exists and the user can write to it.</summary>
    string DataDirectory { get; }

    /// <summary>Writes a line to the app's log, marked with the plugin's id.</summary>
    void Log(PluginLogLevel level, string message);

    /// <summary>Runs the action on the UI thread (call it from the plugin's own threads).</summary>
    void RunOnUi(Action action);

    /// <summary>
    /// Registers a global hotkey, e.g. Keys.Control | Keys.Shift | Keys.R. Null when Windows refused it (another app
    /// uses it). Dispose the result to remove it; the host removes the rest when the plugin stops.
    /// </summary>
    IDisposable? RegisterHotkey(Keys combo, Action action);

    /// <summary>Shows a notification from the tray icon.</summary>
    void Notify(string title, string text);

    /// <summary>True while a game is the window in front (the same detection the overlay uses).</summary>
    bool GameInFront { get; }

    /// <summary>Process name of the last app in front, e.g. "Cyberpunk2077"; null when not known yet.</summary>
    string? ForegroundApp { get; }

    /// <summary>Raised on the UI thread when a game comes to the front or leaves it.</summary>
    event EventHandler<GameEventArgs>? GameChanged;

    /// <summary>The app's colors and font, to make the plugin's windows look like the app's.</summary>
    PluginTheme Theme { get; }

    /// <summary>Raised when the user switches the theme.</summary>
    event EventHandler? ThemeChanged;
}

/// <summary>Level of a log line.</summary>
public enum PluginLogLevel
{
    /// <summary>Normal events.</summary>
    Info,
    /// <summary>Something went wrong, the plugin goes on.</summary>
    Warning,
    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>A game came to the front or left it.</summary>
public sealed class GameEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    public GameEventArgs(bool inFront, string? app) { InFront = inFront; App = app; }

    /// <summary>True: a game is in front now.</summary>
    public bool InFront { get; }

    /// <summary>Process name of the app in front.</summary>
    public string? App { get; }
}

/// <summary>The app's look.</summary>
/// <param name="IsDark">Dark theme.</param>
/// <param name="Background">Window background.</param>
/// <param name="Card">Card (section) background.</param>
/// <param name="Text">Main text.</param>
/// <param name="Muted">Captions, secondary text.</param>
/// <param name="Accent">Active buttons and switches.</param>
/// <param name="Warning">Warnings.</param>
/// <param name="FontFamily">The font the app uses.</param>
public sealed record PluginTheme(bool IsDark, Color Background, Color Card, Color Text, Color Muted, Color Accent, Color Warning, string FontFamily);
