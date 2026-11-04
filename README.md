# Alien Helper

**English** | [Русский](README.ru.md)

A lightweight replacement for Alienware Command Center on the **Alienware 16 Area-51 (AA16250)**, in the spirit of G-Helper:
one window and a tray icon, with no services or background agents of its own.

It uses the same interfaces as AWCC:
- **ACPI WMI** `root\WMI:AWCCWmiMethodFunction`: power modes, Alien Mode, fan speeds, temperatures, fan boost;
- **AlienFX USB HID**: keyboard lighting (Darfon `0D62:1BBC`, protocol v5) and chassis lighting (`187C:0551`, protocol v4).

The protocol comes from [T-Troll/alienfx-tools](https://github.com/T-Troll/alienfx-tools) and the Linux driver
[`alienware-wmi-wmax.c`](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/dell/alienware-wmi-wmax.c).

## Support the project

Alien Helper is free and open source. If it is useful to you, you can support its development by getting it from the
[Microsoft Store](https://apps.microsoft.com/detail/9PC5V3BMXDWP); the Store keeps it up to date.

## Features

- Modes: Balanced, Balanced Performance, Cool, Quiet, Performance, Alien Mode. The list is read from the firmware.
  Alien Mode is the firmware's gaming mode (Game Shift): maximum CPU/GPU power limits and fans at full speed.
  Every mode button has a tooltip describing the mode.
- 4 fans (2 CPU + 2 GPU): live speeds, CPU/GPU temperatures.
- Fan boost 0–100%. It only **raises** fan speed above the BIOS curve; 0 hands control back to the BIOS.
  Fans cannot be slowed below the stock curve, by design. The boost lasts only while the app is open:
  it is reset to 0 on exit.
  The mode sets the boost: Alien Mode sets 100%, Cool 75%, every other mode sets "auto". This happens when the mode changes
  in the app, in the tray or outside it (the hardware Alien Mode key), and at start; within a mode the slider can be moved.
  The **Split** button (expert mode) gives two sliders: CPU (fans 0x32, 0x3B) and GPU (0x33, 0x3C).
  Modes set both sliders the same way. Turning **Split** off applies the higher of the two boosts to all fans.
- **Custom** is a mode of its own (seventh button, tray menu, mode hotkey); only one mode is active at a time. It runs on a
  firmware profile chosen in the curve editor (Balanced by default) and hands the fans to the CPU and GPU curves,
  temperature → boost. Fan rules per mode: Custom — strictly the curves; Alien Mode — 100 %, sliders locked; every other
  mode — the boost sliders. Switching the mode outside the app (G key) ends Custom.
- **Overclock** (inside Custom: button next to **Curves…**, the curve editor or the tray): NVIDIA GPU core and memory
  clock offsets through the driver's performance-state table (NVAPI), in cautious presets added to the offsets that were
  there before (set by AWCC, NVIDIA App or another tool): **Mild** +50 / +150 MHz, **Medium** +100 / +300, **Strong**
  +150 / +500 (core / memory). The original offsets are saved first and put back when Overclock or Custom is switched off,
  when the app exits, at the next start after a crash and after sleep. The CPU is not overclocked. Turning it on the
  first time shows a warning to confirm; hotkey Ctrl+Shift+Alt+C (configurable) switches it inside Custom. The temperature
  is smoothed (~3 s), the boost rises up to 15 %/s and falls up to 5 %/s, and the firmware is written at most once a
  second per group (a 1 % drift only every 3 s). Alien Mode still runs the fans at 100 %; leaving Custom gives the
  boost back to the mode. **Curves…** opens the editor: one chart per group, drag the points (double-click adds one,
  right-click removes one), live temperature and boost shown. Profiles: built-in **Quiet**, **Balanced** (default),
  **Performance**, **Maximum cooling**, plus your own (**Save**, **Save as…**, **Delete**, **Reset** to the profile);
  choosing a profile applies it at once.
- Screen brightness: a slider in the main window (WMI `WmiMonitorBrightness`). Changes made with the brightness keys,
  the Windows slider or the app arrive at once through the `WmiMonitorBrightnessEvent` event, without polling.
- After sleep or unlock the firmware may switch the mode by itself: for 40 s the app watches it (1.5, 3, 5, 10, 20 s) and
  puts the chosen mode back instead of adopting the change (logged as "mode changed … after sleep/unlock").
- **Power** section (off by default; **Settings** → **Main window**): the laptop's total draw — on battery from the
  battery gauge (with charge and time left), on AC from Intel's platform energy counter (RAPL PSYS, MSR 0x64D, read
  through PawnIO; it covers the whole platform, e.g. 93 W total next to CPU 29 W and GPU 21 W); without PawnIO on AC
  only CPU + GPU is shown. **Windows mode**: Efficiency / Balanced / Maximum
  (the Windows power mode overlay, same as Settings → Power).
- Lighting: color and brightness separately for the keyboard and the chassis, on/off from the tray.
  Lighting is applied again after waking from sleep.
- Settings are stored in `%APPDATA%\AlienHelper\settings.json`. With **Apply settings on start** on, start-up
  restores the last settings: the power mode (the last one active, even if it was changed with the hardware key;
  after a reboot the BIOS starts in Balanced) and keyboard (template, animation, key colors) and chassis lighting.
  Lighting is applied again 8 s and 25 s after start, because Dell's AlienFXSubAgent service paints its own
  lighting after sign-in, and again after sleep. The fan boost follows the mode.
  If the firmware reports an unknown mode, the window shows "now: unknown" and no button is highlighted.
- Themes (**Settings** → **Theme**): **Dark** (default), **Light** (the same design on light surfaces) and **Strict**
  (graphite, square corners and knobs, flat fills without gradients), and **Terminal** (green monochrome retro CRT look:
  Consolas, square corners, every chart in shades of green, the overlay included). The title bar and tray menu follow
  the theme; the game overlay and the mode hint stay dark in the other themes so they do not glare over games.
- Themes from files: anyone can make one (a small `.json` with colors, and optionally a font, square corners and a tint
  for one-color themes) and add it in **Settings** → **Theme** → **Add theme…**; **Save current as file…** gives a
  template. Format and ready themes: [`themes/`](themes/README.md). They are not built into the app.
- Key lighting editor (the **Keys** button in the **Lighting** card): the keyboard is drawn on screen;
  left click paints a key (you can drag), right click turns it off; palette, brightness, **Fill all**, **Clear**.
  Templates in the **Template** drop-down (with color previews): WASD, FPS, MOBA, Cat (^_^ on E-4-R, T-Y, U-8-I), Heart, Rainbow, Fire, Ocean.
  **New** creates an empty named template, **Save** writes the drawing into the selected user template,
  **Save as…** saves it under a new name, **Delete** removes any template (**Restore built-ins** brings the
  standard ones back). Unsaved changes are marked with ● in the list.
  Fire, Ocean and Rainbow are animated (~25 fps, ~16 ms per frame; Rainbow adds a brightness wave over the moving colors).
  The animation keeps running after the editor is closed; the **Animation** switch stops and starts it
  (enabled only for animated templates; new animations cannot be created). The scheme is saved
  and applied at start and after sleep. Picking a color in the main window returns to single-color mode.
  - The layout was taken from a photo of the AA16250 keyboard (main block + the Mic/Mute/Vol−/Vol+ column).
    LED numbers: a 20×7 matrix (id = row × 20 + position) from the Area-51 2025 map in alienfx-tools #588;
    the 16" differences were found by tests on this laptop (Esc/F1–F3 = 140–143, Backspace 35, Caps 61, Shift 82/94,
    Win 103, Copilot 112, Mic 19). Wide keys have several LEDs (both Shift keys have two).
    **Fill all** is verified: every key lights up.
  - **Fix key mapping**: pick a key and use ◀ ▶ to find the number that lights exactly that key.
    Corrections are stored in `settings.json` (`KeyIds`).
- UI languages: Русский, English, Français, Español, 日本語, 中文. Chosen in **Settings**
  or the tray; the Windows language is the default. Number formats follow the chosen language.
- The icon is an "AH" monogram (`src\AlienHelper\Assets\app.ico`, generated by `tools\make-icon.ps1`).
  The Alienware logo is a Dell trademark with no free license, so it is not used.
- **Charts** window (the chart icon button next to ⚙ at the top of the main window, or tray item):
  - charts in three columns over 5 minutes: temperatures (CPU, GPU), speeds of the 4 fans,
    clocks (max P-core, E-core, GPU), memory (RAM, VRAM), temperature of each SSD,
    read and write operations per second of each SSD (IOPS, Windows `PhysicalDisk` counters);
    a full-width network chart with download and upload of each active connection (Mbit/s);
    every GPU and drive gets its own lines; when a legend does not fit beside the title it wraps onto more lines;
  - load and clock of each of the 24 cores (P- and E-cores apart, up to 12 compact tiles a row), from the Windows `Processor Information` counters.
    The counters run only while the charts window is open.
  - Per-core temperatures (orange line and value on each tile) come from the CPU's digital thermal sensors
    (MSR 0x19C, TjMax from 0x1A2), read through the signed PawnIO driver. Alien Helper installs no driver: it uses PawnIO
    when it is present, with the IntelMSR module built into the app (IntelMSR.bin next to the exe takes priority).
    Read-only; matched Core Temp on this laptop.
    Without PawnIO the tiles show load and clock only.
- All metrics refresh every 2 s by default. **Settings** → **Metrics refresh** sets it from 0.1 to 10 s in 0.1 s steps;
  charts and sparklines keep covering the same time (charts 5 min, tiles ~80 s, overlay 2 min), only with more or fewer points.
  Changing it starts the histories over.
  Sensors are read on their own thread on a fixed schedule (gaps within ~±15 ms of the setting, measured), so a busy
  window never delays or drops a reading; the NVIDIA GPU is read separately because waking a dozing dGPU takes ~0.5 s.
  Only the metrics that are needed are read. The main window's enabled sections are read all the time, also while the
  window sits in the tray (CPU: firmware and CPU counters; GPU: firmware and NVML; SSD: disk sensors; network; mode);
  a section turned off in Settings is not read. Everything else — per-core temperatures, charts-only sources — is read
  only while the charts window is open (and not minimized), and the charts show only what was read since the window
  opened. The overlay reads what it shows while it is visible. With every section off nothing is read, except the power
  mode every 2 s while a fan boost is set (so leaving Alien Mode with the G key can drop the boost). Slow sources (≈50 counters, NVML, PawnIO) open in the background, so a window shows the fast values
  at once (first reading in the charts window: 87 ms instead of 3.4 s). At slow refresh rates the charts glide between samples
  instead of jumping a step; long histories are averaged to about one point per pixel.
- Main window sections (each can be turned off in **Settings** → **Main window**; at least one always stays on):
  - **CPU**: processor model, temperature, load, max P/E-core clock, package power (Windows "Energy Meter" counters, Intel RAPL),
    RAM used/total, CPU fans;
  - **GPU**: a row for each NVIDIA GPU (NVML): model, temperature, load, clock, power, VRAM used/total and memory clock;
    then the GPU fans. Without NVML (e.g. an AMD card) one row shows the AWCC GPU temperature;
    VRAM temperature comes from the driver's NVAPI thermal sensors (NVML and nvidia-smi report it as N/A on GeForce;
    matched GPU-Z on this laptop) and is shown in the VRAM tile, the temperature chart and the overlay.
    RAM temperature is not shown: the DDR5 sensor can only be read with a kernel driver;
  - **SSD**: a tile for each drive, three per row ("SSD 1 · C:", "SSD 2" for a drive without letters, model in the tooltip):
    temperature via `IOCTL_STORAGE_QUERY_PROPERTY`, read and write IOPS below it ("R 120 · W 45"),
    total IOPS as a graph in the tile background;
  - **Network** (off by default, turn it on in **Settings** → **Main window**): a tile for each active connection
    (up, with a gateway; loopback, tunnels and internal adapters such as WSL are left out), two per row: download speed,
    upload and link speed below it, adapter model in the tooltip, total traffic as the background graph;
  - **Mode**, **Screen brightness**, **Lighting**;
  If the sections do not fit on the screen (many GPUs or drives), the window stops at the screen height and scrolls.
  - sparklines for the last ~80 s: in the corner of the CPU and GPU tiles, as a fill under the text for RAM and VRAM,
    and as a faint fill under each fan's speed bar.
  **Settings** → **Main window** → **Numbers only, no graphs**: current values only, no sparklines, lower tiles.
- Game overlay: a translucent panel on top of all windows; clicks pass through it. It shows CPU/GPU,
  clocks, CPU and GPU power, VRAM, RAM, fans and the mode.
  - Show: **Off**, **Always** (or the hotkey), **In games** — in **Settings** → **Overlay** and the tray menu.
    FPS and frame time appear only while a game is in front (not for browsers, players or apps).
  - 4 styles (**Settings** → **Overlay**, or the tray): **Full** (rows with graphs), **Mini** (a column of values),
    **Bar** (a horizontal strip with sparklines), **Icons** (one line, drawn icons instead of words).
  - Moving: Ctrl+Shift+Alt+arrows (can be held). These shortcuts are registered only while the overlay is visible.
    The position is saved; choosing a corner in the tray resets it.
  - Show it manually with the hotkey (`Ctrl+Shift+O` by default), the switch in **Settings** or the tray.
    A separate **Automatically in games** switch shows the overlay while a game is in the foreground. The screen
    corner is chosen in the tray.
  - What counts as a game: a game Windows itself has seen run (Game Bar and Game Mode keep that list), or one from a
    Steam, Epic, GOG, EA, Ubisoft or Xbox / Game Pass library, also in a window. Any other app counts only while it
    covers the screen and keeps the GPU busy, so a maximized editor, messenger or video does not. If it guesses
    wrong, open the tray → **Overlay** while the app was the last one in front and pick **Count as a game** or
    **Not a game** (a second click hands it back to the detector).
  - Games in exclusive full-screen mode draw over all windows. To see the overlay,
    switch the game to borderless windowed mode.
  - FPS, frame time (average and maximum) and 1% low are computed from ETW Present events
    (Microsoft-Windows-DXGI and D3D9 providers, as in PresentMon) for the foreground process.
    The `AlienHelper-Frames` ETW session runs only while the overlay is visible.
    Vulkan/OpenGL games without a DXGI layer may not be counted.
  - A sparkline under each value: FPS over a minute, frame time bars (spikes above 2× the median in orange),
    temperatures, power, VRAM, RAM over 2 minutes.
  - "Response time" here means frame time. True input latency (as in NVIDIA Reflex) cannot be measured without support in the game itself.
- `AlienHelper.exe --fpstest out.txt` — a 6-second measurement of Present events per process (read-only).

## Running

`publish\AlienHelper.exe` is a single file; .NET does not need to be installed. The AWCC WMI methods are available only
to administrators, so the app itself runs elevated. The exe starts without elevation (`asInvoker`):
- if the app is already running, it just shows its window (through the named event `Local\AlienHelper.Show`);
- if **Start without UAC prompt** is on, it runs the Task Scheduler task `AlienHelper-NoUAC`
  (no triggers, highest privileges), with no UAC prompt;
- otherwise it asks for elevation through UAC once.

The **Start without UAC prompt** switch creates that task; turning it off deletes it. If the exe is moved to another folder,
the tasks are updated the next time the app starts.

- Left click on the tray icon shows or hides the window; right click opens the menu (modes, boost, lighting, overlay,
  language, charts, settings, report, exit).
- **Settings** window (⚙ button in the main window or tray item): main window sections, overlay and its style, hotkeys,
  start-up options, start with Windows, start without UAC, language, diagnostics, About.
  About states the device the app was tested on (Alienware 16 Area-51, AA16250) and the models that should
  work but were not tested (Alienware with AWCC, 2020 and later; Dell G15/G16 for modes and fans only).
- Hotkeys are set in **Settings**: show/hide the overlay (Ctrl+Shift+O by default), cycle the overlay style
  (Ctrl+Shift+Alt+O by default; a hidden overlay is shown) and cycle the power mode
  (Ctrl+Shift+Alt+M by default; the mode name appears at the bottom of the screen for 1.6 s).
  If a combination is taken by another app, **Settings** says so.
- Closing the window (×) hides it to the tray; exit from the menu.
- `AlienHelper.exe --tray` starts the app straight in the tray, without the window.
- **Start minimized (to tray)** (on by default): at start the window does not open, only the tray icon.
  Launching the exe again while the app runs opens the window.

### Start with Windows

The **Start with Windows** switch in **Settings** creates the Task Scheduler task `AlienHelper`:
at user sign-in, with highest privileges (no UAC prompt), with `--tray`, including on battery.
Turning it off deletes the task. The app installs no services.

### Conflict with AWCC

If AWCC (`AWCC.*SubAgent`, `AlienFXSubAgent`) stays running, it may override modes
and lighting. You can uninstall Alienware Command Center in Apps or disable its services.
Without AWCC the hardware Alien Mode key may stop working, since AWCC handles it. The mode can still
be switched from the tray or with the hotkey.

## Repository layout

| Path | What is there |
|---|---|
| `src/AlienHelper/` | the app's source: C# (.NET 8, WinForms), `Hardware/` (firmware WMI, AlienFX, NVML/NVAPI, PawnIO, disks, power), `UI/` (theme and controls), `Assets/` (icons) |
| `build.ps1` | builds `publish\AlienHelper.exe` |
| `installer/` | Inno Setup installer and the MSIX package for the Microsoft Store |
| `tools/` | icon, Store screenshots and hero art generators |
| `store-listing/` | Microsoft Store texts, screenshots and images in 6 languages |
| `docs/` | Store requirements review and submission notes |
| `licenses/`, `THIRD-PARTY-NOTICES.txt` | third-party licenses shipped with the exe |

GitHub Actions (`.github/workflows/build.yml`) builds the exe on every push and attaches it to the run.

## Building

You need the .NET 8 SDK. Without administrator rights it can be installed like this:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
```

Then build the project:

```powershell
.\build.ps1        # -> publish\AlienHelper.exe (self-contained single file, ~63 MB)
```

## Diagnostics

- `AlienHelper.exe --probe out.txt` only reads and writes a file: WMI resources, fans, sensors, modes and lighting devices.
- `AlienHelper.exe --nvapi out.txt` dumps the NVIDIA NVAPI thermal sensors (read-only, no admin rights needed).
- `AlienHelper.exe --bench out.txt` times every sensor source (read-only).
- `AlienHelper.exe --coretemps out.txt` reads per-core temperatures through PawnIO five times (read-only).
- `AlienHelper.exe --curvetest out.txt` runs the curve driver on a simulated temperature ramp and lists the writes it would make (writes nothing).
- `AlienHelper.exe --curvehw out.txt` runs the curves on the hardware for 20 s with the "Maximum cooling" profile, then returns to auto.
- `AlienHelper.exe --gpuclocks out.txt` lists the NVIDIA P0 clock table (editable clocks, allowed offsets, current offsets).
- `AlienHelper.exe --octest out.txt` runs the overclock on/off cycle and checks the original offsets come back.
- `AlienHelper.exe --demandtest out.txt` checks that only wanted sources are read (nothing on screen → no reads).
- `--screenshot` also takes `theme=light|strict` and `numbers`.
- `AlienHelper.exe --test out.txt read profile:A0 wait:10 read boost:30 wait:10 read restore` tests features step by step.
  Steps: `read`, `profile:<hex>`, `boost:<0-100>`, `wait:<sec>`, `kb:<RRGGBB>:<brightness>`, `ch:<RRGGBB>:<brightness>`,
  `restore` (resets the boost and returns the original mode). This mode does not change `settings.json`.
- `AlienHelper.exe --screenshot <folder> [language] [split] [hide=cpu,ssd,…] [fakegpus=N] [fakedisks=N] [poll=ms]` renders the main window, charts, key editor,
  settings and the 4 overlay styles to PNG after 12 s. `split` shows the split boost, `hide=` hides main window sections, `fakegpus=` / `fakedisks=` add fake devices to check the layout, `poll=` sets the refresh period. Nothing is written to the hardware.

## Verified on the hardware (AA16250, BIOS 2.7.0, October 2026)

| Feature | Result |
|---|---|
| WMI resource discovery | ✅ 4 fans (0x32, 0x3B — CPU; 0x33, 0x3C — GPU), sensors 0x01 CPU and 0x06 GPU, 6 modes (A0–A4, AB) |
| Reading fan speed, max speed, temperatures | ✅ |
| Reading the current mode and Alien Mode | ✅ |
| Switching mode (Alien Mode → Balanced) | ✅ fans dropped from ~5900 to ~3550 rpm |
| Back to Alien Mode | ✅ |
| Fan boost 30% → 0 | ✅ fans rose from ~3550 to ~4100 rpm and came back |
| Split CPU / GPU boost, +60% | ✅ CPU: CPU fans ~2100 → ~4100 rpm, GPU fans unchanged; GPU: the other way round; reset returns to the BIOS curve |
| Custom fan curves, "Maximum cooling" profile, 20 s | ✅ CPU 71 °C → boost 100 %, CPU fans 2132 → 5571 rpm; GPU 51 °C → 74 %, GPU fans 2123 → 4954 rpm; eased down as the temperature fell; back to auto afterwards |
| GPU overclock, Mild preset | ✅ offsets +100/+300 (set before the app) → +150/+450 → back to +100/+300 |
| Restoring the mode at start | ✅ Balanced (A0) after a reboot, Balanced Performance (A1) restored at start |
| VRAM temperature (NVAPI thermal sensors) | ✅ 52.0 °C, same as GPU-Z «Memory Temperature» at the same moment |
| Per-core temperatures (PawnIO, MSR 0x19C) | ✅ TjMax 105 °C, cores 56–74 °C, same as Core Temp |
| Fan percentage (op 6), reading the boost (op 0x0C) | ❌ the firmware does not report them (−1 / always 0); not used in the UI |
| Keyboard (v5) and chassis (v4) lighting: off → red → cyan 60% | ✅ confirmed visually (light ids: keyboard 0–143, chassis 0–47), with AWCC running |
| Per-key lighting, Fill all, Fire/Ocean/Rainbow animations | ✅ confirmed visually, template switching 40–60 ms |
| Window, tray, overlay, start without UAC | ✅ checked by the user |
| Reapplying lighting after sleep and after sign-in | ⏳ not verified |

Not implemented: MUX/GPU mode (needs a reboot, risk of a black screen), battery charge limit
(not in this WMI interface), the firmware's built-in lighting effects, firmware fan curves (the app's curves work through the boost).

## Safety

The app calls only the methods AWCC itself calls: `Thermal_Information`, `Thermal_Control`
(mode change, boost), `GameShiftStatus` (Alien Mode), `GetFanSensors`. For NVIDIA GPUs it reads NVML and the NVAPI thermal
sensors query (read-only, the same one GPU-Z uses). There is no direct EC access: the BIOS keeps
watching temperatures and fans. MUX, overclocking, TCC and GPIO are not touched.

## Installer

```powershell
winget install JRSoftware.InnoSetup   # once
.\installer\build-installer.ps1
```

Output: `installer\Output\AlienHelper-Setup-<version>.exe` (the version comes from `src\AlienHelper\AlienHelper.csproj`).
The installer puts the app in `Program Files\AlienHelper`, creates a Start menu shortcut (and optionally a desktop one),
and closes a running copy before updating (`AlienHelper.exe --exit`, which also resets the fan boost).
Uninstalling removes the `AlienHelper` and `AlienHelper-NoUAC` scheduled tasks and asks whether to delete the settings.
Silent install: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.

For the Microsoft Store, the installer and the exe must be signed with a code signing certificate:
`.\installer\build-installer.ps1 -SignCmd 'signtool.exe sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a $f'`.

## License

[GNU GPL v3](LICENSE) or any later version. The app is provided "as is", without any warranty:
the author is not liable for hardware damage, overheating or other consequences. The "Alien Helper" name and icon
are not licensed for modified versions (GPL section 7(e)). Details, trademarks and third-party sources are
in [NOTICE.md](NOTICE.md). The project is not affiliated with Dell Inc.; Alienware is a trademark of Dell Inc.

## Components and drivers

Included in the app:

| Component | What it does | License | Link |
|---|---|---|---|
| .NET 8 and Windows Desktop runtime | runtime and interface | MIT | [dotnet.microsoft.com](https://dotnet.microsoft.com/) |
| System.Management | WMI access (firmware, battery, disks) | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) |
| System.Diagnostics.PerformanceCounter | CPU, disk and power counters | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) |
| Microsoft.Diagnostics.Tracing.TraceEvent | FPS and frame time in the overlay | MIT | [microsoft/perfview](https://github.com/microsoft/perfview) |
| IntelMSR module for PawnIO | per-core temperatures and total power | LGPL-2.1 | [namazso/PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) |

The full list of NuGet dependencies is in `THIRD-PARTY-NOTICES.txt`. The AlienFX and AWCC code is based on
[alienfx-tools](https://github.com/T-Troll/alienfx-tools) (MIT). The installer is built with
[Inno Setup](https://jrsoftware.org/isinfo.php).

Not included, used when present on the system:

| Component | What it does | Comes from | Link |
|---|---|---|---|
| NVML (`nvml.dll`), NVAPI | GPU sensors and overclock | NVIDIA driver | [NVML](https://developer.nvidia.com/management-library-nvml), [NVAPI](https://github.com/NVIDIA/nvapi) |
| PawnIO driver | reads the CPU registers | optional, separate install | [pawnio.eu](https://pawnio.eu/) |
| AWCC firmware interface (`AWCCWmiMethodFunction`) | modes and fans | laptop firmware and Dell drivers | [Dell support](https://www.dell.com/support) |

The app installs no drivers. Without PawnIO it works, but shows no per-core temperatures, and on AC only the CPU + GPU sum
instead of the total; the charts and the Power section then say so, with a link to PawnIO. The Setup installer offers to open the PawnIO page when the driver is missing. Some anti-cheat systems have reportedly
flagged PawnIO, so install it only if you need those readings, and only from the official site.

## Licenses and trademarks

Alien Helper is an independent project, not affiliated with Dell Technologies Inc. Alienware, AlienFX, Alienware Command Center
and Area-51 are trademarks of Dell Inc. and are mentioned only to identify compatible hardware.
Third-party components and their licenses are listed in `THIRD-PARTY-NOTICES.txt` and `licenses/` (copied next to the exe
at build time). The Microsoft Store requirements review is in `docs/STORE-CHECK.md` (in Russian); the Store submission notes are in `docs/STORE-SUBMISSION.md`.

Without the Alienware interface (AWCC WMI) the app shows a message and runs with limited
features; on an untested Alienware model it shows a warning once.

## Logs and error reports

- Log: `%APPDATA%\AlienHelper\logs\alienhelper-YYYYMMDD.log` (start, hardware, mode changes,
  animations, busy hotkeys, errors). Kept for 7 days.
- **Settings** → **Diagnostics** → **Collect report** (or the tray item) puts
  `AlienHelper-report-*.zip` on the desktop: a system and hardware summary, logs, `settings.json` and snapshots of the app's
  open windows (only its own windows, not the whole screen). **Email the author** opens a message to kirill.getmanskiy@gmail.com.
- If the app ended with an unhandled error, it offers to collect a report at the next start.
