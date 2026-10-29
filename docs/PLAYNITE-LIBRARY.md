# `playnite-library.json` — the installed games list (format 1)

Written by the extension since 1.2.0, next to AGSS's `status.json` in the AGSS data folder, and read by AutoGameSoftwareSync 2.0.3 or newer. The extension only writes it next to an **existing** `status.json`; it never creates the folder. Removed by the extension when the list is switched off in its settings or when the extension is disabled.

- Encoding: UTF-8 without BOM, one line.
- Written in one go: unique temporary file in the same folder, then replace. A reader never sees a partial file.
- Rewritten only when the list changes (the generation date alone never causes a rewrite), after a 5 s quiet period following a library event, at startup, when `status.json` appears, when the data folder or the settings change, and at exit if a write was pending.
- A reader must ignore unknown fields: later versions only add fields and keep `schema` at 1 as long as nothing is renamed or removed.

## Fields

Top level:

| field | type | meaning |
|---|---|---|
| `schema` | integer | format number, 1 |
| `generated_utc` | string, optional | generation date, ISO 8601 UTC (`2026-10-29T12:00:00Z`) |
| `games` | array | one entry per **installed** game, sorted by name then id |

Per game (strings are never `null`, empty when unknown):

| field | type | meaning |
|---|---|---|
| `id` | string | Playnite game id (GUID) |
| `name` | string | name, trimmed; a game with an empty name is not listed |
| `source` | string | Playnite source: `Steam`, `GOG`, `Epic`... empty for a game added by hand |
| `plugin_id` | string | Playnite library plugin (GUID), empty for a game added by hand |
| `install_dir` | string | installation folder, Playnite variables expanded (`{InstallDir}`, `{PlayniteDir}`, `{EmulatorDir}` when the emulator is known; otherwise the text is kept as is) |
| `exes` | array of strings | programs of the game's *file* actions, expanded, deduplicated |
| `game_id` | string | id of the game in its library (Steam appid, GOG id, Epic app name...) |
| `steam_appid` | integer, only for Steam games | numeric Steam appid |
| `platforms` | array of strings | Playnite platforms (`PC (Windows)`, `Nintendo SNES`...) |
| `hidden` | boolean | the user hid the game in Playnite |
| `roms` | array of strings, only when not empty | ROM files of an emulated game, expanded |
| `emulator` | object, only for a game with an emulator action whose emulator still exists | `name`, `install_dir`, `profile` (built-in profile name or custom profile name), `exe` (program of a custom profile, expanded; empty for a built-in profile, which Playnite resolves itself) |

Not included: play time, play count, score, completion, last played, tags, categories, notes, images, account data.

## Sample

[`playnite-library.sample.json`](playnite-library.sample.json) is produced byte for byte by `LibraryFile.ToJson` (test `SampleFileIsExactlyWhatTheExtensionWrites`): a Steam game, an emulated game and a hidden game added by hand on a network share.

```json
{"schema":1,"generated_utc":"2026-10-29T12:00:00Z","games":[{"id":"...","name":"Celeste","source":"Steam","plugin_id":"cb91dfc9-b977-43bf-8e70-55f46e410fab","install_dir":"C:\\Games\\Steam\\steamapps\\common\\Celeste","exes":["C:\\Games\\Steam\\steamapps\\common\\Celeste\\Celeste.exe"],"game_id":"504230","platforms":["PC (Windows)"],"hidden":false,"steam_appid":504230}, ...]}
```

Note: the serializer escapes `<`, `>`, `&` and `'` as `\u003c`, `\u003e`, `\u0026`, `\u0027`; any JSON reader decodes them.
