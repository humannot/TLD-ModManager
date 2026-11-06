# TLD Mod Manager

A MelonLoader plugin for **The Long Dark** that keeps your mods up to date and lets you browse and install new ones from inside the game. Mod data comes from [tldmods.net](https://tldmods.net/).

## Features
- **Auto-update on launch.** The plugin loads before your mods, compares installed versions with tldmods.net and replaces outdated files before the game loads them, so no extra restart is needed.
- **In-game manager.** Open it with the **Mods** button in the main menu (right above *Quit*) or with **F8**:
  - tabs for *Installed*, *Available* and *Updates*, plus search by name, author or category;
  - one-click install, with all dependencies installed first;
  - every dependency is marked present or missing, and **Download missing** fetches the rest;
  - update one mod or all of them, remove a mod;
  - **Open mods folder** and **Restart game** buttons.
- **Main-menu banner.** Shows whether auto-update is on, what was updated at launch and how many updates are waiting. Click it to open the manager.
- **Ask on first run.** The first time the game starts with the plugin you are asked whether to update mods at launch; if you say yes, updating starts from the next launch. You can flip it any time with the *UPDATE ON LAUNCH* button in the manager.
- If a mod file is locked by the running game, the change is applied on the next launch (`UserData/ModManager/pending`).
- The plugin lists itself as an installed mod and cannot remove itself.

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
- The main-menu button borrows the colours of the *Quit* item; if the game updates its menu code, F8 still works.

## Building
```bash
dotnet build -c Release -p:Deploy=true
```
The game path is set in `Directory.Build.props` (`TLDDir`) or with `-p:TLDDir=...`.
