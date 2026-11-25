# EVE Focus

A light window switcher for EVE Online clients: one small exe that lives in the system tray, with no thumbnails and nothing to install.

## Getting started

1. Put `EveFocus.exe` in any folder, for example `D:\Games\EveFocus`, and run it.
2. The first time, it asks which characters you play and which keys to use for them. Characters that are logged in are already in the list: click a key box and press the keys.
3. Click **Save and start**. EVE Focus writes your settings to `EveFocus.json` next to the exe and keeps running in the system tray. If that folder can't be written to (for example under Program Files), the file goes to `%APPDATA%\EveFocus` instead.

Double-click the tray icon to change the settings later, or right-click it for the menu.

EVE Focus needs Windows 10 or 11. It runs on .NET Framework 4.8, which is part of Windows.

## What it does

- **A hotkey for each character** brings that character's client to the front.
- **Only the active client stays open** (on by default): whenever a client comes to the front, by hotkey, the tray menu, the taskbar or Alt+Tab, the other clients are minimized. A minimized EVE client is throttled much harder than one that is only out of focus, so this saves a lot of GPU.
- **Minimize all clients** with one hotkey. Pressing it again brings back the client you were on (or all clients, when "Keep only the active client open" is off).
- **Next / previous client** hotkeys go through the clients in the order of your character list.
- **Window layouts**: tray menu → **Save window layouts** remembers where each character's client sits (position, size, monitor, maximized). A client is put back there when that character logs in.
- **Tray menu** lists the running clients; click one to switch to it.

A hotkey acts when you release its key, so an EVE client never gains or loses focus while that key is held. Otherwise EVE can think the key is still pressed, for example Q (Approach), which shows a blue marker over your ship.

EVE Focus only manages windows: it focuses, minimizes, restores and moves them. It never sends keys or clicks to a client and never reads or changes the EVE process.

Use EVE in **Window** or **Fixed Window** mode. Exclusive fullscreen doesn't minimize and restore cleanly.

## Updates

EVE Focus checks for a new version when it starts and once a day. When there is one, it says so, and the tray menu shows **Update to EVE Focus x.y...**; **Check for updates** in the tray menu checks right away. An update is only downloaded from this repository's releases and only installed when its signature matches the release key built into the app. EVE Focus then restarts, and your settings stay as they are.

The automatic check can be turned off in the settings. It is the only time EVE Focus connects to the internet: it asks `api.github.com` for the latest release and, when you choose to update, downloads it from `github.com`. Nothing else is sent.

## The settings file

The settings window covers everything, but `EveFocus.json` can also be edited by hand; EVE Focus reloads it when it is saved.

```json
{
  "Characters": [
    { "Name": "First Pilot", "Hotkey": "Alt+1" },
    { "Name": "Second Pilot", "Hotkey": "Alt+2" }
  ],
  "Hotkeys": { "MinimizeAll": "Alt+Q", "NextClient": "", "PreviousClient": "" },
  "MinimizeOthersOnSwitch": true,
  "AutoApplyLayouts": true,
  "Layouts": {},
  "CheckForUpdates": true
}
```

Keys are written as modifiers (`Ctrl`, `Alt`, `Shift`, `Win`) plus one key name from WinForms `Keys` (`A`–`Z`, `0`–`9`, `F1`–`F24`, `Left`, `PageDown`, `Oemtilde`, `NumPad1`, ...). An empty string turns a hotkey off. A hotkey is taken from every other program while EVE Focus runs, so avoid keys you use in the game.

## Building

Needs the .NET SDK (8 or later). `dotnet build -c Release` creates `bin\Release\net48\EveFocus.exe`, and that file is the whole app.

## Releasing

1. Set the new version in `EveFocus.csproj` (`<Version>`), commit it and push.
2. Tag the commit with the same version, for example `git tag v1.1.0`, and push the tag.
3. The release workflow builds `EveFocus.exe`, signs it with the `RELEASE_SIGNING_KEY` repository secret and creates a draft release with the exe, its signature and its SHA-256.
4. Check the draft on GitHub and publish it. Running copies of EVE Focus pick it up from then on.
