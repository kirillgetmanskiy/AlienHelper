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

    /// <summary>The in-game overlay: the plugin's own items in it, its mode and style.</summary>
    IPluginOverlay Overlay { get; }
}

/// <summary>When the overlay is shown.</summary>
public enum PluginOverlayMode
{
    /// <summary>Hidden.</summary>
    Off,
    /// <summary>Shown all the time.</summary>
    Always,
    /// <summary>Shown while a game is in front.</summary>
    InGames,
}

/// <summary>How the overlay looks.</summary>
public enum PluginOverlayStyle
{
    /// <summary>Rows with graphs.</summary>
    Full,
    /// <summary>A narrow column, values only.</summary>
    Mini,
    /// <summary>A horizontal bar with graphs.</summary>
    Bar,
    /// <summary>A thin strip with icons instead of names.</summary>
    Icons,
}

/// <summary>
/// The in-game overlay. Members can be used from any thread. Mode and Style are the user's choice and are saved:
/// change them when the user asks for it (a menu item, a hotkey of the plugin), not on your own.
/// </summary>
public interface IPluginOverlay
{
    /// <summary>True while the overlay is on screen.</summary>
    bool Visible { get; }

    /// <summary>Where the overlay is on screen; empty while hidden.</summary>
    Rectangle Bounds { get; }

    /// <summary>When the overlay is shown. Setting it is the same as the user picking it in the menu.</summary>
    PluginOverlayMode Mode { get; set; }

    /// <summary>How the overlay looks.</summary>
    PluginOverlayStyle Style { get; set; }

    /// <summary>Shows the overlay or hides it, like the overlay hotkey.</summary>
    void Toggle();

    /// <summary>
    /// Raised on the UI thread when the overlay is shown or hidden, or its mode, style or place changes.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// True: screenshots and screen recordings (this plugin's own and other apps') do not show the overlay; it stays
    /// on screen for the user. Hidden while any running plugin asks for it; the request goes when the plugin stops.
    /// Needs Windows 10 2004 or later; does nothing before.
    /// </summary>
    bool HiddenFromCapture { get; set; }

    /// <summary>
    /// Adds an item at the end of the overlay, after the app's own rows. Dispose it to remove it; the host removes
    /// the rest when the plugin stops.
    /// </summary>
    IPluginOverlayItem AddItem(string label);
}

/// <summary>
/// A plugin's item in the overlay: a row in the full style, a segment in the bar, a value in the minimal styles.
/// Properties can be set from any thread; the overlay redraws soon after.
/// </summary>
public interface IPluginOverlayItem : IDisposable
{
    /// <summary>The name, e.g. "REC". In the icon strip it comes before the value when there is no icon.</summary>
    string Label { get; set; }

    /// <summary>The value, e.g. "01:24".</summary>
    string Value { get; set; }

    /// <summary>A shorter value for the minimal styles; null (the default) for <see cref="Value"/>.</summary>
    string? Compact { get; set; }

    /// <summary>Small text next to the value in the full style and under it in the bar, e.g. "1080p 60".</summary>
    string Extra { get; set; }

    /// <summary>The value's color; null (the default) for the overlay's text color.</summary>
    Color? Color { get; set; }

    /// <summary>False: hidden for now. True by default.</summary>
    bool Visible { get; set; }

    /// <summary>Shown in the minimal styles (Mini, Icons) too. True by default.</summary>
    bool Essential { get; set; }

    /// <summary>
    /// True: a graph of the samples added with <see cref="AddSample"/> under the value, in the full style and the bar.
    /// </summary>
    bool Graph { get; set; }

    /// <summary>The graph's bottom value. 0 by default.</summary>
    float GraphMin { get; set; }

    /// <summary>The graph's top value. 100 by default.</summary>
    float GraphMax { get; set; }

    /// <summary>Adds a point to the graph; the last 240 are kept.</summary>
    void AddSample(float value);

    /// <summary>Removes the graph's points.</summary>
    void ClearSamples();

    /// <summary>
    /// Draws the item's icon for the icon strip: the graphics, a square to draw in and the item's color. Null (the
    /// default): no icon, the label is shown instead. Called on the UI thread while painting: keep it quick. If it
    /// throws, the plugin is stopped.
    /// </summary>
    Action<Graphics, RectangleF, Color>? DrawIcon { get; set; }
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
