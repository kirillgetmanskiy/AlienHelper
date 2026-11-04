# Plugins

**English** | [Русский](#плагины)

> Test build only for now (Alien Helper Test, branches `test` and `test-no-uac`). The API may still change before it
> reaches the release build.

A plugin is a separate product that runs inside Alien Helper: screen and sound recording, for example. It is a .NET 8
class library that builds against `AlienHelper.PluginApi.dll` and is installed by its own installer.

## How the app finds and loads plugins

- Folder: `%ProgramFiles%\AlienHelperTest\Plugins\<plugin>\` (the release build will use
  `%ProgramFiles%\AlienHelper\Plugins\`). Each plugin has its own folder with a `plugin.json` and its assemblies.
- **Only from a folder that administrators alone can change.** The app runs with admin rights, so a plugin's code runs
  with them too. A plugin loads only when its folder, every file in it and the folders above (`Plugins`, the app
  folder) can be changed by Administrators, SYSTEM and TrustedInstaller alone, and are owned by one of them. That is
  the case for files an installer running as administrator put under Program Files. A plugin a program running as the
  user dropped there, or anything in a folder with wider rights, is shown in Settings as not loaded, with the reason.
- **Off until the user turns it on** in **Settings → Plugins**. A plugin that is off is never loaded. Turning one on
  loads and starts it at once; turning it off stops it, and its code goes when the app restarts.
- A plugin that throws from a call is stopped for the session, and Settings shows the error. An exception on a thread of
  the plugin's own still closes the app: catch them.

## plugin.json

```json
{
  "id": "screen-recorder",            // letters, digits, . _ -; the settings and data folder is named after it
  "name": "Screen Recorder",
  "version": "1.0.0",
  "author": "Your name",
  "description": "Records the screen and the sound",
  "assembly": "ScreenRecorder.dll",   // a file in the plugin's folder
  "api": 1                            // PluginApi.Version the plugin was built for
}
```

## A plugin

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <!-- from the app's build: publish-dev\sdk\ (build.ps1 puts it there) -->
    <Reference Include="AlienHelper.PluginApi">
      <HintPath>path\to\sdk\AlienHelper.PluginApi.dll</HintPath>
      <Private>false</Private>   <!-- the app shares its own copy: do not ship one -->
    </Reference>
  </ItemGroup>
</Project>
```

```csharp
using AlienHelper.Plugins;

public sealed class Recorder : IPlugin
{
    IPluginHost? _host;
    IDisposable? _key;
    bool _recording;

    public void Start(IPluginHost host)
    {
        _host = host;
        _key = host.RegisterHotkey(Keys.Control | Keys.Shift | Keys.R, Toggle);
        host.GameChanged += (_, e) => host.Log(PluginLogLevel.Info, e.InFront ? $"game: {e.App}" : "no game");
    }

    public void Stop() { _recording = false; _key?.Dispose(); }

    public IEnumerable<PluginMenuItem> MenuItems() =>
        new[] { new PluginMenuItem(_recording ? "Stop recording" : "Start recording", Toggle) { Checked = _recording } };

    public Control? CreateSettingsView() => new Label { Text = "Settings go here", Size = new Size(320, 120) };

    void Toggle()
    {
        _recording = !_recording;
        _host!.Notify("Screen Recorder", _recording ? "Recording" : "Stopped");
    }
}
```

What `IPluginHost` gives (see `src\AlienHelper.PluginApi\PluginApi.cs`):

| | |
|---|---|
| `AppVersion`, `IsTestBuild`, `IsElevated` | about the app |
| `Language`, `LanguageChanged` | the UI language (`ru`, `en`, `fr`, `es`, `ja`, `zh`) |
| `DataDirectory` | the plugin's own writable folder: `%APPDATA%\AlienHelperTest\plugins\<id>` |
| `Log` | a line in the app's log, marked with the plugin's id |
| `RunOnUi` | runs an action on the UI thread; every call into the plugin already comes there |
| `RegisterHotkey` | a global hotkey; removed when the plugin stops |
| `Notify` | a notification from the tray icon |
| `GameInFront`, `ForegroundApp`, `GameChanged` | the game detection the overlay uses |
| `Theme`, `ThemeChanged` | the app's colors and font, for the plugin's windows |
| `Overlay` | the in-game overlay: the plugin's own items, mode, style, hiding it from recordings (below) |

The tray menu gets a submenu with the plugin's name: its `MenuItems()` and **Settings…**, which opens
`CreateSettingsView()` in a window.

## The overlay

`host.Overlay` (API 1.1) lets a plugin put its own items into the in-game overlay,
after the app's rows: a row in the full style, a segment in the bar, a value in the minimal styles.

```csharp
var rec = host.Overlay.AddItem("REC");
rec.Color = Color.FromArgb(255, 80, 80);
rec.Extra = "1080p 60";             // small text: right of the value (full), under it (bar)
rec.Compact = "1:24";               // the minimal styles (Mini, Icons); Value otherwise
rec.DrawIcon = (g, r, c) => { using var b = new SolidBrush(c); g.FillEllipse(b, r); };   // icon strip; else the label
rec.Value = "01:24";                // any thread: the overlay redraws soon after

var mic = host.Overlay.AddItem("MIC");
mic.Graph = true; mic.GraphMin = 0; mic.GraphMax = 1;
mic.Essential = false;              // not in the minimal styles
mic.AddSample(level);               // the last 240 points are drawn

host.Overlay.HiddenFromCapture = true;   // recordings and screenshots leave the overlay out (Windows 10 2004+)
rec.Dispose();                           // removes the item; the host removes the rest when the plugin stops
```

`Visible`, `Bounds` and `Changed` say whether and where it is on screen. `Mode` (Off, Always, InGames), `Style`
(Full, Mini, Bar, Icons) and `Toggle()` change what the user chose, and it is saved: use them for the user's own
actions (a menu item or hotkey of the plugin), not on your own. On an app with API 1.0 (no `Overlay` yet) a plugin that uses it is stopped when it
gets there, and Settings shows the error.

## Installing

The plugin's installer runs as administrator and copies the files to
`%ProgramFiles%\AlienHelperTest\Plugins\<id>\`. The user then turns the plugin on in **Settings → Plugins**. To remove
a plugin, delete its folder (and restart the app if it was running).

---

# Плагины

[English](#plugins) | **Русский**

> Пока только в тестовой версии (Alien Helper Test, ветки `test` и `test-no-uac`). До выпуска API может измениться.

Плагин — отдельный продукт, который работает внутри Alien Helper, например запись экрана и звука. Это библиотека
.NET 8, собранная с `AlienHelper.PluginApi.dll`, со своим установщиком.

- Папка: `%ProgramFiles%\AlienHelperTest\Plugins\<плагин>\`, в каждой — `plugin.json` и сборки плагина.
- **Только из папки, которую может менять лишь администратор.** Программа работает с правами администратора, и код
  плагина получает те же права. Поэтому плагин загружается, только если его папку, все файлы в ней и папки выше
  могут менять только Администраторы, SYSTEM и TrustedInstaller и владелец у них тоже один из них. Так и будет, если
  файлы положил установщик, запущенный от администратора. Иначе плагин не загружается, а в настройках видна причина.
- **Выключен, пока пользователь не включит** его в «Настройки → Плагины». Выключенный плагин не загружается вовсе.
  Включение сразу загружает и запускает, выключение останавливает, а код выгружается после перезапуска программы.
- Если вызов плагина бросил исключение, плагин останавливается до перезапуска, и в настройках видна ошибка. Исключение
  в собственном потоке плагина закрывает программу целиком, поэтому их нужно ловить.

Плагин может добавлять в оверлей свои строки (например «REC 01:24» с красной точкой), рисовать для них график и значок,
скрывать оверлей из записи экрана и скриншотов, а по действию пользователя менять режим и вид оверлея
(`host.Overlay`, API 1.1).

Формат `plugin.json`, пример плагина, работа с оверлеем и список возможностей `IPluginHost` — в английской части выше.
