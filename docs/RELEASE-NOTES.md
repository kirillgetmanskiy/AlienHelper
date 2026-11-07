# Release notes

**English** | [Русский](#что-нового)

## 0.1.26

- **Emergency mode.** When the GPU overheats, the app switches to Balanced (the GPU gets up to 80 W) and runs the fans
  at full speed until the GPU cools down, then returns to the previous mode. It also starts when the laptop cuts the
  GPU power on its own right after running hot. You cannot pick it by hand; a notification says when it starts and
  ends. A mode you choose meanwhile comes on after it. **Settings → Emergency mode** turns it on or off and sets the
  start (87 °C) and end (72 °C) temperatures.
- **The boost key (F6) no longer blinks.** In Alien Mode, with the fans at 100 % and in the emergency mode the laptop
  lights F6 itself. Keyboard colors and effects now leave that key alone.

### Microsoft Store: "What's new in this version" (en-us)

0.1.25 was not submitted to the Store on its own, so this covers both:

```
• Emergency mode: when the GPU overheats, the app switches to Balanced and runs the fans at full speed until it cools down, then returns to your mode. Settings → Emergency mode sets the temperatures or turns it off.
• The boost key (F6) no longer blinks while keyboard effects run.
• More detailed diagnostics in the log when the GPU power limit changes.
```

### Microsoft Store: "Notes for certification"

```
New: an emergency mode. When the NVIDIA GPU stays at or above a set temperature (87 °C by default), or the laptop cuts the GPU power right after running hot, the app switches the Dell thermal profile to Balanced and sets the fans to 100 % through the same AWCC WMI interface it already uses, shows a notification, and returns to the previous profile once the GPU has cooled down (72 °C by default). It can be turned off in Settings → Emergency mode. Keyboard lighting effects no longer repaint the boost key's LED while the firmware shows the boost on it. No new capabilities. The add-on AlienHelperVideoCap (9NPBGN51VF76) works as in 0.1.24: buy it in Settings → Plugins, then tray menu → Alien Helper VideoCap → Start recording.
```

## 0.1.25

- **Diagnostics for the GPU power limit.** In Alien Mode the GPU sometimes drops to 80 W under load. When the limit
  changes, the log now also records what the NVIDIA driver says holds the clocks, CPU and platform power, and the
  firmware's temperatures. This is to find the cause; nothing changes in how the app controls the laptop.

### Microsoft Store: "What's new in this version" (en-us)

```
• More detailed diagnostics in the log when the GPU power limit changes, to find why the GPU sometimes drops to 80 W in Alien Mode.
```

### Microsoft Store: "Notes for certification"

```
Diagnostics only: the app logs more details (NVIDIA driver clock reasons via NVML, CPU and platform power) when the GPU power limit changes. No new capabilities, no change in behaviour. The add-on AlienHelperVideoCap (9NPBGN51VF76) works as in 0.1.24: buy it in Settings → Plugins, then tray menu → Alien Helper VideoCap → Start recording.
```

## 0.1.24

- **A bought add-on now turns on.** In 0.1.23 Alien Helper did not see that an add-on without a package (such as
  VideoCap) was bought, so the plugin stayed "Not bought". The app now also checks the add-ons in your Microsoft
  account. A plugin you bought turns on by itself the first time the app sees it. If the Store is unreachable, the app
  checks again a minute later.

### Microsoft Store: "What's new in this version" (en-us)

```
• Fixed: a bought add-on (Alien Helper VideoCap) stayed "Not bought". It now turns on by itself after the update.
```

### Microsoft Store: "Notes for certification"

```
Fix of 0.1.23: the license-only durable add-on AlienHelperVideoCap (Store ID 9NPBGN51VF76) is not listed in StoreContext.GetAppLicenseAsync().AddOnLicenses, so the bundled plugin (Pluginsideocap) did not load after purchase. The app now also reads StoreContext.GetUserCollectionAsync(["Durable"]) and loads the plugin when the add-on is in the user's collection. To test: buy the add-on in Settings → Plugins, then use the tray menu → Alien Helper VideoCap → Start recording. No new capabilities.
```

## 0.1.23

- **Alien Helper VideoCap.** VideoCap is the first paid plugin: screen and sound recording in games and on the
  desktop. In the Store version it comes inside the app's package and turns on once you buy its add-on in
  **Settings → Plugins → Buy**. It needs nothing else to download.
- Plugins now use the app's own copy of the Windows runtime libraries, so the package is smaller.

## 0.1.22

The package now declares borderless screen capture (`graphicsCaptureWithoutBorder`) and the microphone. Plugins
run inside the app, so a screen-recording add-on needs these to capture without the yellow border (once the user
allows it) and to record the microphone. The app itself does not record anything.

## 0.1.21

For the Microsoft Store, 0.1.23 follows 0.1.17, so the Store text below covers 0.1.21 to 0.1.23. Everything since
0.1.17:

- **Plugins.** Plugins are separate products that run inside Alien Helper, for example screen and sound recording.
  Each gets its own tray submenu, a settings window, hotkeys, notifications and items in the overlay. A plugin is off
  until you turn it on in **Settings → Plugins**. If a plugin fails, it is stopped and Alien Helper keeps running.
- **Add-ons in the Microsoft Store** (Store version). **Settings → Plugins → Add-ons in the Microsoft Store** lists the
  plugins sold as add-ons, with their prices. Buying one turns it on. The Store version loads plugins from these
  add-ons only.
- **The overlay and plugins.** A plugin can add its own rows to the overlay, such as "REC 01:24", with a graph and an
  icon. It can also keep the overlay out of screenshots and recordings.
- **Game detection.** The overlay's "In games" mode now shows only for games, not for every full-screen app. It
  checks Windows' own game list, the folders of Steam, Epic, GOG, EA, Ubisoft, Xbox and Riot, and GPU load. The tray
  menu can mark the current app as a game or not a game.
- **Limited mode.** The message about missing hardware support is now a yellow warning. It folds into a "!" in the
  window header.

### Microsoft Store: "What's new in this version" (en-us, 1500 characters at most)

```
• Plugins: separate products that run inside Alien Helper. Each gets its own tray menu, settings, hotkeys and overlay items. Plugins stay off until you turn them on in Settings → Plugins.
• Alien Helper VideoCap, the first plugin: screen and sound recording in games and on the desktop. Buy the add-on in Settings → Plugins and it turns on at once.
• Add-ons: Settings → Plugins → Add-ons in the Microsoft Store lists the plugins sold in the Store, with their prices.
• Overlay: plugins can add their own rows (for example REC 01:24) and keep the overlay out of recordings.
• Game detection: the overlay's "In games" mode now shows only for games, not every full-screen app. It uses Windows' game list, game library folders and GPU load. The tray menu can mark the current app as a game or not.
• The limited-mode message is now a yellow warning that folds into a "!" in the header.
```

### Microsoft Store: "Notes for certification"

```
This update adds plugin support and the first paid plugin, Alien Helper VideoCap (screen and sound recording). VideoCap ships inside this app's package (folder Plugins\videocap) and is unlocked by the durable add-on AlienHelperVideoCap (Store ID 9NPBGN51VF76), a license-only add-on without a package. The app checks StoreContext.GetAppLicenseAsync().AddOnLicenses and loads the plugin only while that license is active. Settings → Plugins shows Buy, which calls RequestPurchaseAsync. The app never downloads code and in the Store version loads plugin code only from its own package. To test VideoCap: buy the add-on in Settings → Plugins, then use the tray menu → Alien Helper VideoCap → Start recording. The restricted capabilities (runFullTrust, allowElevation) are unchanged from the previous submission. New general-use capabilities: graphicsCaptureWithoutBorder and microphone (device capability), both for VideoCap. Windows still asks the user before borderless capture and microphone access.
```

---

# Что нового

[English](#release-notes) | **Русский**

## 0.1.26

- **Аварийный режим.** Когда видеокарта перегревается, программа включает Balanced (видеокарте до 80 Вт) и
  вентиляторы на полную, пока видеокарта не остынет, а потом возвращает прежний режим. Он включается и тогда, когда
  ноутбук сам урезает мощность видеокарты сразу после нагрева. Вручную его выбрать нельзя; о включении и выключении
  сообщает уведомление. Режим, выбранный в это время, включится после него. В «Настройки → Аварийный режим» его можно
  выключить и задать температуры включения (87 °C) и выключения (72 °C).
- **Клавиша буста (F6) больше не мигает.** В Alien Mode, при вентиляторах на 100 % и в аварийном режиме ноутбук сам
  подсвечивает F6. Цвета и эффекты клавиатуры теперь эту клавишу не трогают.

### Microsoft Store: «Новые возможности этой версии» (ru-ru)

0.1.25 в Store отдельно не отправлялась, поэтому текст охватывает обе версии:

```
• Аварийный режим: при перегреве видеокарты программа включает Balanced и вентиляторы на полную, пока она не остынет, а затем возвращает ваш режим. Температуры и выключение — в «Настройки → Аварийный режим».
• Клавиша буста (F6) больше не мигает при эффектах клавиатуры.
• Подробная диагностика в логе при смене лимита мощности видеокарты.
```

## 0.1.25

- **Диагностика лимита мощности видеокарты.** В режиме Alien Mode видеокарта под нагрузкой иногда падает до 80 W.
  Теперь при смене лимита в лог пишется, что ограничивает частоты по словам драйвера NVIDIA, мощность процессора и
  платформы и температуры из прошивки. Это нужно, чтобы найти причину; управление ноутбуком не меняется.

### Microsoft Store: «Новые возможности этой версии» (ru-ru)

```
• Подробная диагностика в логе при смене лимита мощности видеокарты, чтобы найти, почему она иногда падает до 80 W в Alien Mode.
```

## 0.1.24

- **Купленное дополнение теперь включается.** В 0.1.23 программа не видела покупку дополнения без пакета (например
  VideoCap), и плагин оставался «Не куплено». Теперь программа проверяет ещё и дополнения в вашей учётной записи
  Microsoft. Купленный плагин включается сам, как только программа увидит покупку. Если Store недоступен, проверка
  повторится через минуту.

### Microsoft Store: «Новые возможности этой версии» (ru-ru)

```
• Исправлено: купленное дополнение (Alien Helper VideoCap) оставалось «Не куплено». После обновления оно включится само.
```

## 0.1.23

- **Alien Helper VideoCap.** VideoCap — первый платный плагин: запись экрана и звука в играх и на рабочем столе. В
  Store-версии он уже есть в пакете программы и включается после покупки дополнения в «Настройки → Плагины →
  Купить». Скачивать больше ничего не нужно.
- Плагины теперь используют библиотеки среды Windows из самой программы, поэтому пакет стал меньше.

## 0.1.22

Пакет теперь объявляет захват экрана без рамки (`graphicsCaptureWithoutBorder`) и микрофон. Плагины работают внутри
программы, поэтому дополнению для записи экрана это нужно, чтобы снимать без жёлтой рамки (после разрешения
пользователя) и записывать микрофон. Сама программа ничего не записывает.

## 0.1.21

В Microsoft Store после 0.1.17 выходит 0.1.23, поэтому текст для Store ниже охватывает версии с 0.1.21 по 0.1.23. Всё,
что изменилось после 0.1.17:

- **Плагины.** Плагины — отдельные продукты, которые работают внутри Alien Helper, например запись экрана и звука.
  У плагина бывает своё меню в трее, окно настроек, горячие клавиши, уведомления и строки в оверлее. Плагин выключен,
  пока его не включить в «Настройки → Плагины». Если плагин сломается, он останавливается, а программа работает дальше.
- **Дополнения в Microsoft Store** (Store-версия). В «Настройки → Плагины → Дополнения в Microsoft Store» видны
  плагины, которые продаются как дополнения, с ценами. Купленный плагин сразу включается. Store-версия загружает
  плагины только из таких дополнений.
- **Оверлей и плагины.** Плагин может добавить в оверлей свои строки, например «REC 01:24», с графиком и значком.
  Ещё он может скрыть оверлей из скриншотов и записи экрана.
- **Определение игр.** Режим оверлея «В играх» теперь срабатывает только на игры, а не на любую программу на весь
  экран. Учитываются список игр Windows, папки Steam, Epic, GOG, EA, Ubisoft, Xbox и Riot и нагрузка на видеокарту. В
  меню трея текущую программу можно отметить как игру или как «не игра».
- **Ограниченный режим.** Сообщение о том, что оборудование не поддерживается, стало жёлтым предупреждением. Его
  можно свернуть до «!» в заголовке окна.

### Microsoft Store: «Новые возможности этой версии» (ru-ru, не больше 1500 символов)

```
• Плагины: отдельные продукты, которые работают внутри Alien Helper. У плагина своё меню в трее, настройки, горячие клавиши и строки в оверлее. Плагины выключены, пока вы не включите их в «Настройки → Плагины».
• Alien Helper VideoCap, первый плагин: запись экрана и звука в играх и на рабочем столе. Купите дополнение в «Настройки → Плагины», и он сразу включится.
• Дополнения: в «Настройки → Плагины → Дополнения в Microsoft Store» видны плагины, которые продаются в Store, с ценами.
• Оверлей: плагины могут добавлять свои строки (например REC 01:24) и скрывать оверлей из записи экрана.
• Определение игр: режим оверлея «В играх» срабатывает только на игры, а не на любую программу на весь экран. Учитываются список игр Windows, папки игровых библиотек и нагрузка на видеокарту. В меню трея программу можно отметить как игру или как «не игра».
• Сообщение об ограниченном режиме стало жёлтым предупреждением, его можно свернуть до «!» в заголовке.
```
