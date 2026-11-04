using System.Reflection;
using System.Runtime.Loader;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlienHelper.Plugins;
using AlienHelper.UI;

namespace AlienHelper;

/// <summary>A plugin's plugin.json.</summary>
public sealed record PluginManifest(string Id, string Name, string Version, string? Author, string? Description, string Assembly, int Api);

/// <summary>A plugin found in the plugins folder: loaded only once the user turned it on.</summary>
public sealed class PluginEntry
{
    public required string Folder { get; init; }
    public PluginManifest? Manifest { get; init; }
    /// <summary>Why it cannot be loaded (bad manifest, unsafe folder, wrong API) or why it failed; null when fine.</summary>
    public string? Problem { get; set; }
    internal IPlugin? Instance;
    internal PluginHost? Host;
    public bool Running => Host != null;
    /// <summary>Stopped this session: its code stays loaded until the app restarts.</summary>
    public bool Unloading { get; internal set; }
    public string Id => Manifest?.Id ?? Path.GetFileName(Folder);
    public string Title => Manifest is { } m ? $"{m.Name} {m.Version}" : Path.GetFileName(Folder);
}

/// <summary>
/// Plugins: separate products that run inside the app (screen and sound recording, for example). Each lives in its
/// own folder under <see cref="Dir"/> with a plugin.json and its assemblies, and builds against AlienHelper.PluginApi.
/// The app runs with admin rights, so a plugin's code runs with them too: plugins load only from under Program Files,
/// only when every folder and file on the way is writable by administrators and the system alone (an installer put
/// them there, not a program running as the user), and only after the user turned them on in Settings.
/// </summary>
public sealed class PluginManager : IDisposable
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppId.Key, "Plugins");

    readonly Settings _s;
    readonly Control _ui;
    readonly Action<string, string> _notify;
    internal readonly OverlayManager Overlay;
    readonly List<PluginEntry> _plugins = new();
    readonly System.Windows.Forms.Timer _gameTimer = new() { Interval = 2000 };
    bool _game;

    public IReadOnlyList<PluginEntry> Plugins => _plugins;
    /// <summary>A plugin started, stopped or failed.</summary>
    public event Action? Changed;

    public PluginManager(Settings s, Control ui, OverlayManager overlay, Action<string, string> notify)
    {
        _s = s; _ui = ui; Overlay = overlay; _notify = notify;
        _gameTimer.Tick += (_, _) => CheckGame();
    }

    /// <summary>Finds the plugins and starts the ones the user turned on.</summary>
    public void Start()
    {
        Discover();
        foreach (var p in _plugins.Where(p => _s.EnabledPlugins.Contains(p.Id))) Load(p);
    }

    void Discover()
    {
        _plugins.Clear();
        if (!Directory.Exists(Dir)) return;
        string? unsafeRoot = Unsafe(Path.GetDirectoryName(Dir)!, recurse: false) ?? Unsafe(Dir, recurse: false);
        foreach (var folder in Directory.GetDirectories(Dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            PluginManifest? m = null;
            string? problem;
            try { m = ReadManifest(folder, out problem); }
            catch (Exception ex) { problem = ex.Message; }
            problem = (unsafeRoot ?? Unsafe(folder, recurse: true)) ?? problem;   // the folder's rights come first
            if (problem == null && m!.Api != PluginApi.Version) problem = L.T("plugins.api", m.Api, PluginApi.Version);
            var p = new PluginEntry { Folder = folder, Manifest = m, Problem = problem };
            if (problem != null) Log.Warn($"plugin {p.Id}: {problem}");
            _plugins.Add(p);
        }
        Log.Info($"plugins: {_plugins.Count} in {Dir}");
    }

    static PluginManifest? ReadManifest(string folder, out string? problem)
    {
        problem = null;
        string file = Path.Combine(folder, "plugin.json");
        if (!File.Exists(file)) { problem = L.T("plugins.nomanifest"); return null; }
        var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        // the assembly is a plain file name: nothing outside the plugin's folder is loaded
        if (m == null || string.IsNullOrWhiteSpace(m.Name) || m.Id is not { } id || !Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")
            || string.IsNullOrEmpty(m.Assembly) || m.Assembly != Path.GetFileName(m.Assembly) || !m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        { problem = L.T("plugins.badmanifest"); return m; }
        if (!File.Exists(Path.Combine(folder, m.Assembly))) { problem = L.T("plugins.noassembly", m.Assembly); return m; }
        return m;
    }

    /// <summary>Turns a plugin on (loads and starts it now) or off (stops it now; its code goes at the next start).</summary>
    public void SetEnabled(PluginEntry p, bool on)
    {
        _s.EnabledPlugins.RemoveAll(x => x.Equals(p.Id, StringComparison.OrdinalIgnoreCase));
        if (on) _s.EnabledPlugins.Add(p.Id);
        _s.Save();
        if (on && !p.Running) Load(p);
        if (!on && p.Running) { Stop(p); p.Unloading = true; Changed?.Invoke(); }
    }

    public bool IsEnabled(PluginEntry p) => _s.EnabledPlugins.Contains(p.Id, StringComparer.OrdinalIgnoreCase);

    void Load(PluginEntry p)
    {
        if (p.Problem != null || p.Manifest is not { } m) return;
        try
        {
            if (p.Instance == null)
            {
                // checked again right before loading: the folder may have changed since start-up
                if (Unsafe(p.Folder, recurse: true) is { } why) { p.Problem = why; Changed?.Invoke(); return; }
                var ctx = new PluginLoadContext(Path.Combine(p.Folder, m.Assembly));
                var asm = ctx.LoadFromAssemblyPath(Path.Combine(p.Folder, m.Assembly));
                var type = asm.GetExportedTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                    ?? throw new InvalidOperationException(L.T("plugins.notype"));
                p.Instance = (IPlugin)Activator.CreateInstance(type)!;
            }
            var host = new PluginHost(this, p);
            p.Host = host;
            p.Instance.Start(host);
            p.Unloading = false;
            Log.Info($"plugin {p.Id} {m.Version} started");
        }
        catch (Exception ex)
        {
            Fail(p, "start", ex);
            return;
        }
        _gameTimer.Enabled = _plugins.Any(x => x.Running);
        Changed?.Invoke();
    }

    void Stop(PluginEntry p)
    {
        var host = p.Host;
        if (host == null) return;
        p.Host = null;
        try { p.Instance?.Stop(); }
        catch (Exception ex) { Log.Error($"plugin {p.Id}: stop", ex); }
        host.Close();
        _gameTimer.Enabled = _plugins.Any(x => x.Running);
        Log.Info($"plugin {p.Id} stopped");
    }

    /// <summary>A call into the plugin threw: it is stopped for this session, and Settings shows why.</summary>
    internal void Fail(PluginEntry p, string what, Exception ex)
    {
        Log.Error($"plugin {p.Id}: {what}", ex);
        if (p.Running) Stop(p);
        p.Problem = L.T("plugins.failed", (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message);
        Changed?.Invoke();
    }

    /// <summary>The tray submenus of the running plugins.</summary>
    public IEnumerable<ToolStripMenuItem> MenuItems(Action<PluginEntry> openSettings)
    {
        foreach (var p in _plugins.Where(x => x.Running).ToList())
        {
            var sub = new ToolStripMenuItem(p.Manifest!.Name) { ForeColor = Theme.Fg };
            try
            {
                foreach (var it in p.Instance!.MenuItems())
                {
                    var item = it;
                    sub.DropDownItems.Add(new ToolStripMenuItem(item.Text, null, (_, _) => Call(p, "menu", item.OnClick))
                        { Checked = item.Checked, Enabled = item.Enabled, ForeColor = Theme.Fg });
                }
            }
            catch (Exception ex) { Fail(p, "menu", ex); continue; }
            if (sub.DropDownItems.Count > 0) sub.DropDownItems.Add(new ToolStripSeparator());
            sub.DropDownItems.Add(new ToolStripMenuItem(L.T("plugins.settings"), null, (_, _) => openSettings(p)) { ForeColor = Theme.Fg });
            yield return sub;
        }
    }

    /// <summary>The plugin's settings view in a window of its own; a message when it has none.</summary>
    public void OpenSettings(PluginEntry p, IWin32Window? owner)
    {
        if (!p.Running) return;
        Control? view;
        try { view = p.Instance!.CreateSettingsView(); }
        catch (Exception ex) { Fail(p, "settings", ex); return; }
        if (view == null) { MessageBox.Show(owner, L.T("plugins.nosettings"), p.Manifest!.Name); return; }
        var f = new Form { Text = $"{p.Manifest!.Name} — {L.T("settings")}", FormBorderStyle = FormBorderStyle.FixedSingle, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen };
        Theme.Style(f);
        f.ClientSize = view.Size;
        view.Dock = DockStyle.Fill;
        f.Controls.Add(view);
        f.Show(owner);
    }

    /// <summary>Calls into a plugin; an exception stops the plugin instead of the app.</summary>
    internal void Call(PluginEntry p, string what, Action a)
    {
        try { a(); }
        catch (Exception ex) { Fail(p, what, ex); }
    }

    internal void RunOnUi(Action a)
    {
        if (_ui.IsDisposed) return;
        if (_ui.InvokeRequired) _ui.BeginInvoke(a); else a();
    }

    internal void Notify(string title, string text) => RunOnUi(() => _notify(title, text));

    void CheckGame()
    {
        bool game = GameDetector.InFront();
        if (game == _game) return;
        _game = game;
        foreach (var p in _plugins.Where(x => x.Running).ToList()) p.Host!.RaiseGame(new GameEventArgs(game, GameDetector.LastApp));
    }

    public void ThemeChanged() { foreach (var p in _plugins.Where(x => x.Running).ToList()) p.Host!.RaiseTheme(); }
    public void LanguageChanged() { foreach (var p in _plugins.Where(x => x.Running).ToList()) p.Host!.RaiseLanguage(); }

    public void Dispose()
    {
        _gameTimer.Dispose();
        foreach (var p in _plugins.Where(x => x.Running).ToList()) Stop(p);
    }

    // ---- where plugins may come from

    static readonly SecurityIdentifier[] Trusted =
    {
        new(WellKnownSidType.LocalSystemSid, null),
        new(WellKnownSidType.BuiltinAdministratorsSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),   // TrustedInstaller
        new(WellKnownSidType.CreatorOwnerSid, null),   // the owner, checked on its own below
        new("S-1-3-4"),                                // OWNER RIGHTS, likewise
    };

    // rights that let someone change or replace what is loaded: write, append, attributes, delete, permissions, owner
    const int WriteRights = 0x2 | 0x4 | 0x10 | 0x40 | 0x100 | 0x10000 | 0x40000 | 0x80000
        | 0x40000000 | 0x10000000;   // GENERIC_WRITE, GENERIC_ALL

    /// <summary>
    /// Null when only administrators and the system can change the folder (and, with recurse, everything in it);
    /// otherwise why not. The owner counts too: whoever owns a file can change its permissions.
    /// </summary>
    static string? Unsafe(string path, bool recurse)
    {
        try
        {
            if (Check(new DirectoryInfo(path).GetAccessControl(), path) is { } dirWhy) return dirWhy;
            if (!recurse) return null;
            foreach (var d in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                if (Check(new DirectoryInfo(d).GetAccessControl(), d) is { } why) return why;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                if (Check(new FileInfo(f).GetAccessControl(), f) is { } why) return why;
            return null;
        }
        catch (Exception ex) { return L.T("plugins.unsafe", ex.Message); }
    }

    static string? Check(FileSystemSecurity sec, string path)
    {
        string name = Path.GetFileName(path);
        if (sec.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !Trusted.Take(3).Contains(owner))
            return L.T("plugins.unsafe", $"{name}: {Account(sec.GetOwner(typeof(SecurityIdentifier)))}");
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (r.AccessControlType != AccessControlType.Allow || r.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;
            if (r.IdentityReference is SecurityIdentifier sid && Trusted.Contains(sid)) continue;
            if (((int)r.FileSystemRights & WriteRights) != 0) return L.T("plugins.unsafe", $"{name}: {Account(r.IdentityReference)}");
        }
        return null;
    }

    static string Account(IdentityReference? id)
    {
        if (id == null) return "?";
        try { return id.Translate(typeof(NTAccount)).Value; } catch { return id.Value; }
    }
}

/// <summary>
/// A plugin's own assembly context: its assemblies and native libraries come from its folder, the plugin API (and the
/// framework) from the app, so the plugin and the app share the same IPlugin type.
/// </summary>
sealed class PluginLoadContext : AssemblyLoadContext
{
    static readonly string Api = typeof(IPlugin).Assembly.GetName().Name!;
    readonly string _dir;
    readonly AssemblyDependencyResolver? _deps;

    public PluginLoadContext(string mainAssembly) : base(Path.GetFileNameWithoutExtension(mainAssembly))
    {
        _dir = Path.GetDirectoryName(mainAssembly)!;
        try { _deps = new AssemblyDependencyResolver(mainAssembly); }   // reads the plugin's .deps.json
        catch { _deps = null; }                                          // none (or not supported here): look in the folder
    }

    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == Api) return null;   // always the app's copy
        string? path = _deps?.ResolveAssemblyToPath(name);
        if (path == null)
        {
            string local = Path.Combine(_dir, name.Name + ".dll");
            if (File.Exists(local)) path = local;
        }
        return path != null ? LoadFromAssemblyPath(path) : null;   // null: the framework, from the app
    }

    protected override IntPtr LoadUnmanagedDll(string name)
    {
        string? path = _deps?.ResolveUnmanagedDllToPath(name);
        if (path == null)
        {
            string local = Path.Combine(_dir, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll");
            if (File.Exists(local)) path = local;
        }
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

/// <summary>What one plugin gets from the app (<see cref="IPluginHost"/>).</summary>
sealed class PluginHost : IPluginHost
{
    readonly PluginManager _m;
    readonly PluginEntry _p;
    readonly List<HotKey> _keys = new();
    bool _closed;

    readonly PluginOverlay _overlay;

    public PluginHost(PluginManager m, PluginEntry p)
    {
        _m = m; _p = p;
        DataDirectory = Path.Combine(AlienHelper.Log.Dir, "plugins", p.Id);
        Directory.CreateDirectory(DataDirectory);
        _overlay = new PluginOverlay(m, p);
    }

    public IPluginOverlay Overlay => _overlay;

    public string AppVersion => typeof(PluginHost).Assembly.GetName().Version?.ToString(3) ?? "";
    public bool IsTestBuild => AppId.Test;
    public bool IsElevated => Launcher.IsElevated();
    public string Language => L.Current;
    public string DataDirectory { get; }
    public event EventHandler? LanguageChanged;
    public event EventHandler<GameEventArgs>? GameChanged;
    public event EventHandler? ThemeChanged;

    public void Log(PluginLogLevel level, string message)
    {
        string line = $"plugin {_p.Id}: {message}";
        if (level == PluginLogLevel.Error) AlienHelper.Log.Error(line);
        else if (level == PluginLogLevel.Warning) AlienHelper.Log.Warn(line);
        else AlienHelper.Log.Info(line);
    }

    public void RunOnUi(Action action) => _m.RunOnUi(() => { if (!_closed) _m.Call(_p, "ui", action); });

    public IDisposable? RegisterHotkey(Keys combo, Action action)
    {
        if (_closed) return null;
        var key = HotKey.FromCombo((int)combo, () => { if (!_closed) _m.Call(_p, "hotkey", action); });
        if (key == null) return null;
        if (!key.Registered) { key.Dispose(); return null; }
        _keys.Add(key);
        return new Removal(() => { _keys.Remove(key); key.Dispose(); });
    }

    public void Notify(string title, string text) { if (!_closed) _m.Notify(title, text); }

    public bool GameInFront => GameDetector.InFront();
    public string? ForegroundApp => GameDetector.LastApp;

    public PluginTheme Theme => new(UI.Theme.IsDark, UI.Theme.Bg, UI.Theme.Card, UI.Theme.Fg, UI.Theme.Muted, UI.Theme.Accent, UI.Theme.Warn, UI.Theme.Family);

    internal void RaiseGame(GameEventArgs e) => _m.Call(_p, "game event", () => GameChanged?.Invoke(this, e));
    internal void RaiseTheme() => _m.Call(_p, "theme event", () => ThemeChanged?.Invoke(this, EventArgs.Empty));
    internal void RaiseLanguage() => _m.Call(_p, "language event", () => LanguageChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>The plugin stopped: its hotkeys and handlers go, later calls do nothing.</summary>
    internal void Close()
    {
        _closed = true;
        foreach (var k in _keys) k.Dispose();
        _keys.Clear();
        GameChanged = null; ThemeChanged = null; LanguageChanged = null;
        _overlay.Close();
    }

    sealed class Removal : IDisposable
    {
        Action? _a;
        public Removal(Action a) => _a = a;
        public void Dispose() { _a?.Invoke(); _a = null; }
    }
}

/// <summary>What one plugin may do with the overlay (<see cref="IPluginOverlay"/>): its own items, mode, style.</summary>
sealed class PluginOverlay : IPluginOverlay
{
    readonly PluginManager _m;
    readonly PluginEntry _p;
    readonly List<PluginOverlayItem> _items = new();
    bool _closed, _hidden;

    public PluginOverlay(PluginManager m, PluginEntry p)
    {
        _m = m; _p = p;
        _m.Overlay.Changed += OnChanged;
    }

    OverlayManager O => _m.Overlay;
    internal OverlayManager Manager => O;

    public bool Visible => O.Visible;
    public Rectangle Bounds => O.Bounds;

    public PluginOverlayMode Mode
    {
        get => (PluginOverlayMode)O.Mode;
        set { if (Enum.IsDefined(value)) Ui(() => { if (O.Mode != (int)value) O.Mode = (int)value; }); }
    }

    public PluginOverlayStyle Style
    {
        get => (PluginOverlayStyle)O.Style;
        set { if (Enum.IsDefined(value)) Ui(() => { if (O.Style != (int)value) O.Style = (int)value; }); }
    }

    public void Toggle() => Ui(O.Toggle);

    public event EventHandler? Changed;
    void OnChanged() { if (!_closed) _m.Call(_p, "overlay event", () => Changed?.Invoke(this, EventArgs.Empty)); }

    public bool HiddenFromCapture
    {
        get => _hidden;
        set { _hidden = value; Ui(() => O.HideFromCapture(this, value)); }
    }

    public IPluginOverlayItem AddItem(string label)
    {
        var item = new PluginOverlayItem(this, label ?? "");
        lock (_items) { if (_closed) return item; _items.Add(item); }   // after the plugin stopped: an item nobody sees
        item.Row.Failed = ex => _m.Fail(_p, "overlay icon", ex);
        Ui(() => { if (!item.Removed) O.AddItem(item.Row); });
        return item;
    }

    internal void Remove(PluginOverlayItem item)
    {
        lock (_items) { if (!_items.Remove(item)) return; }
        item.Removed = true;
        Ui(() => O.RemoveItem(item.Row));
    }

    /// <summary>Runs on the UI thread, now when already there; nothing once the plugin stopped.</summary>
    internal void Ui(Action a) => _m.RunOnUi(() => { if (!_closed) a(); });

    /// <summary>The plugin stopped: its items and its capture request go (called on the UI thread).</summary>
    internal void Close()
    {
        _closed = true;
        O.Changed -= OnChanged;
        Changed = null;
        List<PluginOverlayItem> items;
        lock (_items) { items = _items.ToList(); _items.Clear(); }
        foreach (var it in items) { it.Removed = true; O.RemoveItem(it.Row); }
        O.HideFromCapture(this, false);
    }
}

/// <summary>
/// A plugin's overlay item. The plugin may set it from any thread: values are kept here and copied to the overlay's
/// <see cref="OverlayItem"/> on the UI thread, at most once per batch of changes.
/// </summary>
sealed class PluginOverlayItem : IPluginOverlayItem
{
    const int SampleCap = 240;
    readonly PluginOverlay _o;
    readonly object _lock = new();
    readonly List<float> _samples = new();
    string _label, _value = "", _extra = "";
    string? _compact;
    Color? _color;
    bool _visible = true, _essential = true, _graph;
    float _min, _max = 100;
    Action<Graphics, RectangleF, Color>? _icon;
    int _pending;

    internal readonly OverlayItem Row = new();
    internal volatile bool Removed;

    public PluginOverlayItem(PluginOverlay o, string label) { _o = o; _label = label; Row.Label = label; }

    T Get<T>(ref T field) { lock (_lock) return field; }
    void Set<T>(ref T field, T value) { lock (_lock) field = value; Changed(); }

    public string Label { get => Get(ref _label); set => Set(ref _label, value ?? ""); }
    public string Value { get => Get(ref _value); set => Set(ref _value, value ?? ""); }
    public string? Compact { get => Get(ref _compact); set => Set(ref _compact, value); }
    public string Extra { get => Get(ref _extra); set => Set(ref _extra, value ?? ""); }
    public Color? Color { get => Get(ref _color); set => Set(ref _color, value); }
    public bool Visible { get => Get(ref _visible); set => Set(ref _visible, value); }
    public bool Essential { get => Get(ref _essential); set => Set(ref _essential, value); }
    public bool Graph { get => Get(ref _graph); set => Set(ref _graph, value); }
    public float GraphMin { get => Get(ref _min); set => Set(ref _min, value); }
    public float GraphMax { get => Get(ref _max); set => Set(ref _max, value); }
    public Action<Graphics, RectangleF, Color>? DrawIcon { get => Get(ref _icon); set => Set(ref _icon, value); }

    public void AddSample(float value)
    {
        lock (_lock) { _samples.Add(value); if (_samples.Count > SampleCap) _samples.RemoveRange(0, _samples.Count - SampleCap); }
        Changed();
    }

    public void ClearSamples() { lock (_lock) _samples.Clear(); Changed(); }

    void Changed()
    {
        if (Removed || Interlocked.Exchange(ref _pending, 1) == 1) return;   // one copy covers every change before it
        _o.Ui(Apply);
    }

    void Apply()
    {
        Interlocked.Exchange(ref _pending, 0);
        if (Removed) return;
        lock (_lock)
        {
            (Row.Label, Row.Value, Row.Compact, Row.Extra, Row.Color) = (_label, _value, _compact, _extra, _color);
            (Row.Visible, Row.Essential, Row.Min, Row.Max, Row.DrawIcon) = (_visible, _essential, _min, _max, _icon);
            Row.History = _graph ? _samples.ToList() : null;
        }
        _o.Manager.ItemsChanged();
    }

    public void Dispose() => _o.Remove(this);
}
