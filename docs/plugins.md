# Plugins

**English** | [Русский](#плагины)

A plugin is a separate product that runs inside Alien Helper: screen and sound recording, for example. It is a .NET 8
class library that builds against `AlienHelper.PluginApi.dll` and is installed by its own installer.

## How the app finds and loads plugins

- Folder: `%ProgramFiles%\AlienHelper\Plugins\<plugin>\` (the test build, Alien Helper Test:
  `%ProgramFiles%\AlienHelperTest\Plugins\`). Each plugin has its own folder with a `plugin.json` and its assemblies.
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
  "api": 1,                           // PluginApi.Version the plugin was built for
  "storeId": "9XXXXXXXXXXX"           // optional: the Microsoft Store add-on it is sold as
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

The app shares its own copies of `AlienHelper.PluginApi`, `WinRT.Runtime` and `Microsoft.Windows.SDK.NET` with every
plugin: two WinRT runtimes in one process would fight over the same COM objects. A plugin that uses Windows APIs
targets `net8.0-windows10.0.22621.0` or older and need not ship those two files.

What `IPluginHost` gives (see `src\AlienHelper.PluginApi\PluginApi.cs`):

| | |
|---|---|
| `AppVersion`, `IsTestBuild`, `IsElevated` | about the app |
| `Language`, `LanguageChanged` | the UI language (`ru`, `en`, `fr`, `es`, `ja`, `zh`) |
| `DataDirectory` | the plugin's own writable folder: `%APPDATA%\AlienHelper\plugins\<id>` (test build: `AlienHelperTest`) |
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
`%ProgramFiles%\AlienHelper\Plugins\<id>\` (or `AlienHelperTest` for the test build). The user then turns the plugin on in **Settings → Plugins**. To remove
a plugin, delete its folder (and restart the app if it was running).

## Selling a plugin as a Microsoft Store add-on

The Store build of Alien Helper (MSIX, branch `no-uac-bypass`) lists the app's add-ons in **Settings → Plugins →
Add-ons in the Microsoft Store** with their prices. **Buy** opens the Store's own purchase dialog, then the Store
downloads and installs the add-on's package, and the plugin is turned on at once. The Store build loads plugins from these
add-ons only, never from the plugins folder: the Store does not allow adding code to an app from outside it (policy
10.2.2). The builds from the Setup.exe have no Store: they load the folder and do not show the button.

**For now: inside the app's package.** Optional packages need Microsoft's permission. Until it comes, a paid plugin
ships inside the app's Store package as `Plugins\<id>` (`installer\build-msix.ps1 -Plugins <folder>`; by default
VideoCap's `..\AlienHelper-VideoCap\publish\store-bundle`). Its add-on is a plain *durable without a package*: a
license only. The Store build loads such a plugin only while the user owns the `storeId` in its plugin.json. Such an
add-on never shows in `GetAppLicenseAsync().AddOnLicenses`, only in `StoreContext.GetUserCollectionAsync()`, so the
app asks both. Otherwise **Settings → Plugins** shows it as not bought, with **Buy**.
After the purchase it turns on at once, with nothing to download, and a refund stops it. Each plugin update then
ships with an update of the app.

**Later: an optional package.** An add-on is then a *durable with a package*: its package is an MSIX **optional package** of Alien Helper. Windows installs
it under `C:\Program Files\WindowsApps\`, where only the system can write, so it passes the same check as the plugins
folder. The app loads optional packages of its own publisher only.

1. Ask Microsoft for permission to use optional packages: the Store requires it
   ([Windows developer support](https://developer.microsoft.com/windows/support)).
2. Partner Center → Alien Helper → **Add-ons → Create add-on**, type **Durable**. Set the price (and a free trial if
   wanted), turn on the package upload, and note the add-on's Store ID for `storeId` in plugin.json.
3. Make the package: the plugin's folder as it would be in `Plugins\<id>\` (plugin.json at the root, the assemblies
   next to it), with an `AppxManifest.xml` and no `Applications`:

   ```xml
   <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
            xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
            xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
            IgnorableNamespaces="uap uap3">
     <!-- Name and Publisher: the add-on's identity from Partner Center -->
     <Identity Name="..." Publisher="CN=4F657209-00E8-4045-8043-BF61B6723400" Version="1.0.0.0" ProcessorArchitecture="x64" />
     <Properties>
       <DisplayName>Alien Helper VideoCap</DisplayName>
       <PublisherDisplayName>LobsterSoft</PublisherDisplayName>
       <Logo>Assets\StoreLogo.png</Logo>
     </Properties>
     <Dependencies>
       <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
       <uap3:MainPackageDependency Name="35916LobsterSoft.AlienHelper" />
     </Dependencies>
     <Resources><Resource Language="en-us" /></Resources>
   </Package>
   ```

   `makeappx pack /d <folder> /p VideoCap.msix`, then upload it to the add-on's submission.
4. The plugin itself needs nothing else: the same `IPlugin` as from the folder.

---

# Плагины

[English](#plugins) | **Русский**

Плагин — отдельный продукт, который работает внутри Alien Helper, например запись экрана и звука. Это библиотека
.NET 8, собранная с `AlienHelper.PluginApi.dll`, со своим установщиком.

- Папка: `%ProgramFiles%\AlienHelper\Plugins\<плагин>\` (у тестовой версии — `AlienHelperTest`), в каждой — `plugin.json` и сборки плагина.
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

Платный плагин можно продавать как дополнение (add-on) Alien Helper в Microsoft Store. В Store-версии программы есть
кнопка «Настройки → Плагины → Дополнения в Microsoft Store»: там видны цены, а «Купить» открывает окно покупки Store.
После покупки Store сам скачивает и ставит пакет дополнения, и плагин сразу включается. Пока разрешения Microsoft на
optional packages нет, платный плагин лежит внутри пакета самой программы (`Plugins\<id>`), а дополнение — только
лицензия: без покупки плагин виден в «Настройки → Плагины» как «Не куплено» с кнопкой «Купить». Store-версия загружает плагины только из
таких дополнений, папка плагинов в ней не используется (правило Store 10.2.2). Дополнение — это *durable с
пакетом*, необязательный (optional) MSIX-пакет Alien Helper. Для него нужно разрешение Microsoft на optional packages.
Шаги и пример манифеста — в английской части выше («Selling a plugin as a Microsoft Store add-on»).

Формат `plugin.json`, пример плагина, работа с оверлеем и список возможностей `IPluginHost` — в английской части выше.
