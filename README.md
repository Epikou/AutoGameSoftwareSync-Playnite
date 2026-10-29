# AutoGameSoftwareSync for Playnite

[Francais](README.fr.md)

<p align="center"><img src="assets/icon.png" width="96" alt="AutoGameSoftwareSync"></p>

A free [Playnite](https://playnite.link) extension that shows, for each game, what [AutoGameSoftwareSync](https://github.com/Epikou/AutoGameSoftwareSync) (AGSS) knows about its saves:

- the state: **Protected**, **To upload**, **Conflict**, **Cloud newer**...
- the date of the last sync;
- the device the version in place comes from, in its own colour.

AGSS does all the work. The extension never launches, restores or backs up anything, and it never writes to your save files or to your vault.

New in 1.2.0: it also gives AGSS the list of your **installed games**, so that AutoGameSoftwareSync 2.0.3 or newer can suggest the ones you do not protect yet (including games added by hand).

**What it reads and writes, plainly**: it reads AGSS's `status.json` and Playnite's library (installed games); it writes `playnite-library.json` in the AGSS data folder; in fallback mode only, it adds or removes one genre (or tag / feature / category) on the games AGSS follows. Nothing else, ever: none of your files is copied, moved or deleted.

![Badge in Solaris](screenshots/solaris-badge.png)
![Conflict in Solaris](screenshots/solaris-conflict.png)

## Supported fullscreen themes

For these themes a badge is inserted into the game page, in the theme's own style. Nothing to configure.

| Theme | Checked version |
|---|---|
| Solaris | 1.3.6 |
| ReskinPS5 | 1.11 |
| (u)biquity | 2.0.4 |
| PS5ish | 5.0.3 |
| PlayniteModernUI | 1.6.2 |
| Playnite Deck | 3.0.2 |
| Nintendo Switch-ish | 2.0.0 |
| Toggle | 1.10.1 |

| | |
|---|---|
| ![ReskinPS5](screenshots/theme-reskinps5.png) | ![ubiquity](screenshots/theme-ubiquity.png) |
| ![PS5ish](screenshots/theme-ps5ish.png) | ![PlayniteModernUI](screenshots/theme-playnitemodernui.png) |
| ![Playnite Deck](screenshots/theme-playnitedeck.png) | ![Nintendo Switch-ish](screenshots/theme-nintendo-switchish.png) |
| ![Toggle](screenshots/theme-toggle.png) | |

### Any other theme: the genre fallback

If the active fullscreen theme is not in the list (for example XBOXSERIESish, whose fixed-size details card cannot take an extra line), the extension adds a genre such as `● AGSS : Protected · MYPC`. The `●` makes it sort first. It is replaced when the state changes, removed when it is no longer needed and restored after a metadata download. No genre is written while a badge works. Only items in the extension's own form (prefix followed by a state) are ever removed: a genre of yours is never touched. Choose *Badge only* in the settings so nothing is ever added to your games; the menu *AGSS : remove all added items* takes everything out again.

![Genre fallback](screenshots/genre-fallback.png)

Theme authors can place the information themselves: see [docs/THEMES.md](docs/THEMES.md).

## Installed games list (1.2.0)

The extension writes `playnite-library.json` next to AGSS's `status.json`, in the AGSS data folder (`%LOCALAPPDATA%\AutoGameSoftwareSync`, or the folder set by `AUTOGAMESOFTWARESYNC_DATA_DIR`, or the `status.json` path typed in the settings). It is written in one go, only when the list changes, and only next to an existing `status.json`: the extension never creates the AGSS folder. It is removed when you untick *Write the installed games list for AGSS* or disable the extension.

One entry per installed game: Playnite id, name, source (Steam, GOG...), library plugin, install folder, programs, id in its library, Steam app id, platforms, hidden flag, and for emulated games the ROMs and the emulator. No play time, no score, no account. Install paths naturally contain your Windows user name; the file stays on this PC and the extension sends nothing anywhere. Format and sample: [docs/PLAYNITE-LIBRARY.md](docs/PLAYNITE-LIBRARY.md). AutoGameSoftwareSync 2.0.3 does not use the emulated games yet.

## Requirements

- Playnite 10 (extension API 6.15 or newer).
- AutoGameSoftwareSync **2.0 or newer**. The best display needs **2.0.2 or newer** (the version that ships the device name and colour in `status.json`); with 2.0.0 the state and date are shown but not the device. The installed games list needs **AutoGameSoftwareSync 2.0.3 or newer**.

## Installation

1. Download `AutoGameSoftwareSync_Playnite_Plugin_1_2_0.pext` from the [latest release](https://github.com/Epikou/AutoGameSoftwareSync-Playnite/releases/latest).
2. Double-click it (or drag it onto Playnite) and confirm the installation. Restart Playnite when asked.
3. That's all: the AGSS data folder, the active theme and the language are detected automatically, and the display follows `status.json` live.

Settings stay available in Playnite (Add-ons > Extensions settings): display mode, field, prefix, installed games list, `status.json` path, optional notifications on Conflict / Cloud newer (off by default).

State texts follow Playnite's language (English, French, Russian, Spanish, Chinese); settings, menu and messages are in English.

Uninstalling leaves `playnite-library.json` and fallback genres in place (Playnite has no uninstall hook): untick *Write the installed games list* and use *AGSS : remove all added items* first for a clean exit.

## About

Made with vibe coding: the extension was tested by hand in Playnite 10 with the themes above, and comes with an automated test suite. It is free; if it is useful to you, you can support the project on [Ko-fi](https://ko-fi.com/autogamesoftwaresave).

Questions, help and news: join the [Discord](https://discord.gg/W8YXUJAK4e).

The source code is visible in this repository (`src`, `tests`) so that you can read and audit what the extension does. It is not open source: see [LICENSE](LICENSE). To build and test it: `dotnet build` / `dotnet test` (.NET Framework 4.6.2, PlayniteSDK 6.15.0).

## License

Free for personal use, source visible but all rights reserved: see [LICENSE](LICENSE). Playnite and PlayniteSDK belong to their respective authors.
