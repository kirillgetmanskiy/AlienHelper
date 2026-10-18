# Microsoft Store listing

One folder per Store language:

| Folder | Store language |
|---|---|
| `ru` | Russian (ru-ru) |
| `en` | English (en-us) |
| `fr` | French (fr-fr) |
| `es` | Spanish (es-es) |
| `ja` | Japanese (ja-jp) |
| `zh` | Chinese Simplified (zh-cn) |

Each folder has:

- `description.txt` — name, short description, description, features (one per line), search terms (up to 7).
  Every description says the app is for compatible Dell Alienware laptops and carries the non-affiliation notice.
- `screenshots/` — 1920×1080 PNG (the Store accepts 1366×768 and larger):
  `01-main.png` main window and overlay, `02-charts.png` charts, `03-curves.png` fan curves, `04-keys.png` key lighting.

Regenerate the screenshots with `tools\store-shots.ps1` (renders the app's windows in every language through
`AlienHelper.exe --screenshot`; nothing is written to the hardware).

`icons/` — 1:1 app tile icons for Partner Center (Store listing → Store logos): `AppTile-300x300.png`,
`AppTile-150x150.png`, `AppTile-71x71.png`. Opaque square tiles (the Store rounds them); regenerate with
`tools\make-icon.ps1`, which draws every size from vectors.

`hero/` — 16:9 Super hero art (Partner Center → Store listing → Windows and Xbox image): `SuperHero-3840x2160.png`
and `SuperHero-1920x1080.png`. No text, so one image serves every language; the lower left is kept calm for the
title and buttons the Store draws over it. Regenerate with `tools\make-hero.ps1`.
