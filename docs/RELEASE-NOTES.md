# Release notes

**English** | [Русский](#что-нового)

## 0.1.21

For the Microsoft Store, this release follows 0.1.17. Everything since then:

- **Plugins.** Plugins are separate products that run inside Alien Helper, for example screen and sound recording.
  Each gets its own tray submenu, a settings window, hotkeys, notifications and items in the overlay. A plugin is off
  until you turn it on in **Settings → Plugins**. If a plugin fails, it is stopped and Alien Helper keeps running.
- **Add-ons in the Microsoft Store** (Store version). **Settings → Plugins → Add-ons in the Microsoft Store** lists the
  plugins sold as add-ons, with their prices. Buying one installs it and turns it on. The Store version loads plugins
  from these add-ons only.
- **The overlay and plugins.** A plugin can add its own rows to the overlay, such as "REC 01:24", with a graph and an
  icon. It can also keep the overlay out of screenshots and recordings.
- **Game detection.** The overlay's "In games" mode now shows only for games, not for every full-screen app. It
  checks Windows' own game list, the folders of Steam, Epic, GOG, EA, Ubisoft, Xbox and Riot, and GPU load. The tray
  menu can mark the current app as a game or not a game.
- **Limited mode.** The message about missing hardware support is now a yellow warning. It folds into a "!" in the
  window header.

### Microsoft Store: "What's new in this version" (en-us, 1500 characters at most)

```
• Plugins: separate products that run inside Alien Helper, such as screen and sound recording. Each gets its own tray menu, settings, hotkeys and overlay items. Plugins stay off until you turn them on in Settings → Plugins.
• Add-ons: Settings → Plugins → Add-ons in the Microsoft Store lists plugins sold in the Store. Buying one installs it and turns it on.
• Overlay: plugins can add their own rows (for example REC 01:24) and keep the overlay out of recordings.
• Game detection: the overlay's "In games" mode now shows only for games, not every full-screen app. It uses Windows' game list, game library folders and GPU load. The tray menu can mark the current app as a game or not.
• The limited-mode message is now a yellow warning that folds into a "!" in the header.
```

### Microsoft Store: "Notes for certification"

```
This update adds plugin support. Plugins are sold only as durable add-ons of this app in the Microsoft Store. Each is an optional package (MSIX) of this app, from the same publisher. Settings → Plugins → "Add-ons in the Microsoft Store" lists them through Windows.Services.Store. Buying one calls RequestPurchaseAsync and then RequestDownloadAndInstallStorePackagesAsync. The app loads plugin code only from its own optional packages installed by the Store. It never downloads code itself and never loads plugins from any other location in the Store version. No add-ons are published yet, so the list is empty. The restricted capabilities (runFullTrust, allowElevation) are unchanged from the previous submission.
```

---

# Что нового

[English](#release-notes) | **Русский**

## 0.1.21

В Microsoft Store эта версия выходит после 0.1.17. Всё, что изменилось с тех пор:

- **Плагины.** Плагины — отдельные продукты, которые работают внутри Alien Helper, например запись экрана и звука.
  У плагина бывает своё меню в трее, окно настроек, горячие клавиши, уведомления и строки в оверлее. Плагин выключен,
  пока его не включить в «Настройки → Плагины». Если плагин сломается, он останавливается, а программа работает дальше.
- **Дополнения в Microsoft Store** (Store-версия). В «Настройки → Плагины → Дополнения в Microsoft Store» видны
  плагины, которые продаются как дополнения, с ценами. Купленный плагин сам устанавливается и включается.
  Store-версия загружает плагины только из таких дополнений.
- **Оверлей и плагины.** Плагин может добавить в оверлей свои строки, например «REC 01:24», с графиком и значком.
  Ещё он может скрыть оверлей из скриншотов и записи экрана.
- **Определение игр.** Режим оверлея «В играх» теперь срабатывает только на игры, а не на любую программу на весь
  экран. Учитываются список игр Windows, папки Steam, Epic, GOG, EA, Ubisoft, Xbox и Riot и нагрузка на видеокарту. В
  меню трея текущую программу можно отметить как игру или как «не игра».
- **Ограниченный режим.** Сообщение о том, что оборудование не поддерживается, стало жёлтым предупреждением. Его
  можно свернуть до «!» в заголовке окна.

### Microsoft Store: «Новые возможности этой версии» (ru-ru, не больше 1500 символов)

```
• Плагины: отдельные продукты, которые работают внутри Alien Helper, например запись экрана и звука. У плагина своё меню в трее, настройки, горячие клавиши и строки в оверлее. Плагины выключены, пока вы не включите их в «Настройки → Плагины».
• Дополнения: в «Настройки → Плагины → Дополнения в Microsoft Store» видны плагины, которые продаются в Store. Купленный плагин сам устанавливается и включается.
• Оверлей: плагины могут добавлять свои строки (например REC 01:24) и скрывать оверлей из записи экрана.
• Определение игр: режим оверлея «В играх» срабатывает только на игры, а не на любую программу на весь экран. Учитываются список игр Windows, папки игровых библиотек и нагрузка на видеокарту. В меню трея программу можно отметить как игру или как «не игра».
• Сообщение об ограниченном режиме стало жёлтым предупреждением, его можно свернуть до «!» в заголовке.
```
