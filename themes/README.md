# Themes

**English** | [Русский](#темы)

Alien Helper can load themes from files, so anyone can make one and share it. A theme is a small `.json` file with a name
and colors. The themes here are not built into the app: download one and add it in **Settings → Theme → Add theme…**,
or put the file into the themes folder (**Settings → Theme → Themes folder**) and pick it from the same list. It applies
at once, no restart.

| File | Theme |
|---|---|
| [`phosphor-terminal.json`](phosphor-terminal.json) | Phosphor Terminal: a green monochrome retro CRT terminal look, monospace font, square corners, the overlay included. Built into the app as **Terminal**; the file is kept as an example to start from |

## Making a theme

The easiest start: **Settings → Theme → Save current as file…** writes the theme in use as a file. Change the colors in
any text editor, then add it back with **Add theme…**.

```jsonc
{
  "format": 1,
  "name": "My theme",              // or { "en": "My theme", "ru": "Моя тема" }: shown in the app's language
  "author": "your name",
  "base": "dark",                  // dark or light: the title bar, and every color the file leaves out
  "corners": "round",              // or "square"
  "font": "Consolas",              // any installed font; Segoe UI when left out or not installed
  "tint": "#3CFF7A",               // optional: recolors every chart line and overlay row to this hue
  "overlay": true,                 // optional: the game overlay and the mode hint use the theme too (dark themes only)
  "colors": {
    "background": "#0E1014",       // window background
    "card": "#171A20",             // cards (sections)
    "cardHover": "#1F232B",        // buttons under the mouse, selected items
    "border": "#262B34",           // card and button borders
    "track": "#2A303B",            // slider and fan bar tracks
    "text": "#E8EAED",             // main text
    "muted": "#8A93A3",            // captions, secondary text
    "accent": "#1FB6FF",           // active buttons, switches, the main chart line
    "hot": "#FF6A3D",              // high temperatures, Alien Mode, power
    "good": "#3DDC97",             // normal temperatures, GPU
    "warning": "#FFC14D",          // warm temperatures, warnings
    "extra": "#9B7BFF"             // second data color: E-cores, VRAM, the Custom mode
  }
}
```

Only `colors` is required, and it may list just the colors you change; the rest come from `base`. When `card` is set
but `cardHover`, `border` or `track` are not, they are derived from it.

Rules:
- colors are `#RRGGBB` (or `#RGB`); comments and trailing commas are allowed;
- the file is at most 64 KB;
- the app checks the file when it is added: a broken file is refused with the reason, and text that is hard to read on
  the cards (contrast below 4.5:1) is pointed out;
- a theme file is only data: it cannot run anything.

To share a theme, send a pull request that adds your file to this folder and a line to the table above.

---

# Темы

[English](#themes) | **Русский**

Alien Helper умеет загружать темы из файлов, поэтому сделать и выложить тему может кто угодно. Тема — небольшой файл
`.json` с названием и цветами. Темы из этой папки в программу не встроены. Скачайте файл и добавьте его через
**Настройки → Тема → Добавить тему…** или положите в папку тем (**Настройки → Тема → Папка тем**) и выберите в том же
списке. Тема применяется сразу, без перезапуска.

| Файл | Тема |
|---|---|
| [`phosphor-terminal.json`](phosphor-terminal.json) | Терминал (люминофор): зелёный монохромный ретро-терминал с ЭЛТ, моноширинный шрифт, квадратные углы, оверлей тоже в теме. Встроена в программу как «Терминал»; файл оставлен как пример, с которого удобно начать |

## Как сделать тему

Проще всего начать так: **Настройки → Тема → Сохранить текущую в файл…** сохраняет текущую тему в файл. Поменяйте
цвета в любом текстовом редакторе и добавьте файл обратно через **Добавить тему…**.

Формат описан в примере выше:
- `name`: название; можно на нескольких языках;
- `base`: `dark` или `light`, задаёт заголовок окна и все цвета, которых нет в файле;
- `corners`: `round` или `square`;
- `font`: любой установленный шрифт;
- `tint`: перекрашивает все линии графиков и строки оверлея в этот оттенок;
- `overlay`: оверлей и подсказка режима тоже используют тему (только для тёмных тем);
- `colors`: цвета, назначение каждого — в комментариях примера.

Обязателен только `colors`, и в нём можно указать лишь те цвета, которые меняете. Остальные берутся из `base`. Если
задан `card`, но нет `cardHover`, `border` или `track`, они выводятся из него.

Правила:
- цвета в виде `#RRGGBB` (или `#RGB`), комментарии и запятые в конце допустимы;
- размер файла до 64 КБ;
- при добавлении программа проверяет файл: битый не примет и скажет почему, а если текст плохо читается на карточках
  (контраст ниже 4,5:1), предупредит;
- файл темы — только данные, запустить из него ничего нельзя.

Чтобы поделиться темой, пришлите pull request с вашим файлом в этой папке и строкой в таблице выше.
