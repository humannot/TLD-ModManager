# TLD Mod Manager

A MelonLoader plugin for **The Long Dark** that keeps your mods up to date and lets you browse and install new ones from inside the game. Mod data comes from [tldmods.net](https://tldmods.net/).

> **Unofficial project.** This plugin is not endorsed by and not affiliated with Hinterland Studio (the developers of The Long Dark), MelonLoader or the tldmods.net website. All mentioned names and materials belong to their respective owners.

## Features
- **Auto-update on launch.** The plugin loads before your mods, compares installed versions with tldmods.net and replaces outdated files before the game loads them, so no extra restart is needed.
- **In-game manager.** Open it with the **Mod Manager** button in the main menu (right above *Quit*) or with **F8**:
  - tabs for *Installed*, *Available* and *Updates*, plus search by name, author or category;
  - one-click install, with all dependencies installed first;
  - every dependency is marked present or missing, and **Download missing** fetches the rest;
  - update one mod or all of them, disable or enable a mod without removing it (its files get a `.disabled` suffix), remove a mod;
  - **Open mods folder** and **Restart game** buttons.
- **Sorting** by name, last update or author; click the active sort again to reverse it.
- **Game version.** Every mod shows the game version it was last tested on (for example `TLD v2.59`), as a badge on its image, in the list and in the details.
- **Translate description.** With the game in Russian, a button at the bottom translates the selected mod's description into Russian and shows the translation under the original text (the same button hides it). It uses the free [MyMemory](https://mymemory.translated.net/) service (no key; machine translation; about 5,000 characters a day, results are cached in `UserData/ModManager/translations.json`). The description text is sent to that service.
- **Esc** closes the manager.
- **Tiles with preview images** on the *Available* tab, like tldmods.net (switch to a plain list with *VIEW*); *Installed* and *Updates* stay lists. The details panel shows the image at full size (up to 550 px high; the content below moves up for smaller images). Previews are cached in `UserData/ModManager/img`. PNG and JPEG are shown; mods whose only image is WebP or GIF show their name instead.
- **Ask on first run.** The first time the game starts with the plugin you are asked whether to update mods at launch; if you say yes, updating starts from the next launch. You can flip it any time with the *UPDATE ON LAUNCH* button in the manager.
- If a mod file is locked by the running game, the change is applied on the next launch (`UserData/ModManager/pending`).
- The plugin lists itself as an installed mod and cannot remove itself. **Check manager update** looks at this repository's latest GitHub release and downloads a newer `ModManager.dll`; press *Restart game* afterwards and the new version is swapped in.
- **Follows the game language.** The interface is in Russian when the game is set to Russian, English otherwise.

## Installation
1. Install MelonLoader 0.7.x.
2. Put `ModManager.dll` into `TheLongDark/Plugins/` (**Plugins**, not Mods).

Download it from the [Releases](https://github.com/humannot/TLD-ModManager/releases) page.

## Settings
Stored in `UserData/MelonPreferences.cfg`, section `[ModManager]`:
- `AutoUpdate` (default `false`): update mods at launch.
- `AskedAutoUpdate`: set once you answer the first-run question.

## Limitations
- It installs `.dll`, `.modcomponent` and `.zip` downloads. Mods distributed as `.7z`, `.rar` or an installer show a **Mod page** button instead.
- An installed mod is recognised by the `MelonInfo` inside its DLL or by its file name. If the version is unknown (for example a hand-copied `.modcomponent`), auto-update leaves it alone and the manager offers the update instead.
- Disabling and enabling take effect at the next game start. A disabled mod still counts as present when another mod's dependencies are checked.
- The main-menu button depends on the game's menu code; if a game update breaks it, F8 still opens the manager.

## Building
```bash
dotnet build -c Release -p:Deploy=true
```
The game path is set in `Directory.Build.props` (`TLDDir`) or with `-p:TLDDir=...`.
