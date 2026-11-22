# Leanback Mode for Windows

> Part of [Windows Unlocked Toolkit](../../README.md). The easiest way to
> install it is the toolkit's one-file installer, which lets you pick this tool
> from a menu. The steps below are for installing it on its own from this folder.

Turn on a game controller and your PC switches to **TV mode**: bigger text and
apps, a bigger mouse pointer, and the controller works as a mouse. Turn the
controller off and it goes back to **desktop mode**. The switch happens behind
a soft blur, so you never see the screen jump around.

Made for using a PC on a TV from the couch.

## Works with

Any controller Windows sees as a game controller, over USB, Bluetooth, or a
2.4 GHz dongle:

- Xbox controllers, and anything in "X-input" mode (most 8BitDo pads, many generic pads)
- PlayStation (DualShock 4, DualSense)
- Nintendo Switch Pro
- 8BitDo, PowerA, PDP, HORI, Logitech, and generic Bluetooth or wireless pads

**Tip for 2.4 GHz dongles:** use the dongle's X-input (Xbox) mode if it has
one. In that mode Windows can tell when the controller itself turns off. In
other modes some dongles look "connected" as long as they're plugged in, which
keeps TV mode on (see [If it doesn't work](#if-it-doesnt-work)).

## Install

1. Download this folder (or the whole repo as a zip) and extract it.
2. Double-click `Install.cmd`. If Windows asks whether to run it, choose
   **Run** (or **More info**, then **Run anyway**).
3. If AntiMicroX isn't installed yet, the installer gets it with `winget`.
   Windows may ask you to approve that one step.

Both apps then start automatically each time you sign in, and live in the
system tray.

If you used the older **Controller TV Mode**, installing Leanback Mode
replaces it and keeps your settings.

## Use

| You do | What happens |
| --- | --- |
| Turn the controller on | TV mode, after half a second |
| Turn the controller off | Desktop mode, about a second later |
| Press **Ctrl+Alt+T** | Switch between TV and desktop mode any time. Lasts until the controller next turns on or off. |
| Open a game | The controller mouse pauses so the game gets the controller, and comes back when you leave the game. |

Left-click the TV icon in the tray for the menu: switch modes, choose
Automatic / Always TV / Always desktop, pause the controller mouse, or edit
settings.

### Controller buttons (as a mouse)

| Button | Does |
| --- | --- |
| Left stick | Move the pointer |
| Right stick | Up/down scrolls, left/right switches browser tabs |
| A | Left click |
| X | Right click |
| B | Enter |
| Y (hold) | Fast pointer |
| D-pad | Arrow keys |
| LT / RT | Space / Backspace |
| LB / RB | Browser back / forward |
| Start | Windows key (Start menu) |
| Back / Select | Refresh |
| Left stick click | Middle click |
| Right stick click | F11 (full screen) |

Button names follow the Xbox layout. On a PlayStation pad, A = Cross,
B = Circle, X = Square, Y = Triangle.

## Settings

Right-click the tray icon and choose **Edit settings**. Changes apply as soon
as you save. The file explains each option. The main ones:

- `TvScale` / `NormalScale`: display scaling in each mode (for example 150 and 100).
- `TvPointerSize` / `NormalPointerSize`: pointer size in each mode.
- `Hotkey`: the switch shortcut, for example `Ctrl+Alt+T` or `Win+Alt+M`.
- `PauseMouseInGames`, `GameFolders`, `NotGames`: when the controller mouse steps aside for games.
- `DetectControllers`: `All` (default) or `XInput` for Xbox-style only.
- `IgnoreControllers`: devices that should never count as a controller.

Pointer speed and button mapping live in the AntiMicroX layout
(`%LOCALAPPDATA%\antimicrox\profiles\controller-mouse.amgp`). You can edit it
from the AntiMicroX window.

## Remove

Double-click `Uninstall.cmd`. It switches back to desktop mode, stops both
apps, removes them from startup, and deletes Leanback Mode's folder.
AntiMicroX itself stays installed. To remove it too, run
`Uninstall.ps1 -RemoveAntiMicroX` from PowerShell.

## What it does (and doesn't do)

- It checks every quarter second whether a game controller is connected. It
  doesn't read your keyboard or what you type.
- It changes Windows display scaling on your main screen and the pointer size,
  the same settings as in Windows Settings.
- To tell when you're in a game, it looks at which app is in front and where
  that app is installed.
- Leanback Mode never connects to the internet. The installer uses `winget`
  to download AntiMicroX once, if you don't already have it.
- It's built on your PC from `LeanbackMode.cs` (readable source code) with the
  C# compiler that comes with Windows. No admin rights needed.
- It lives in `%LOCALAPPDATA%\LeanbackMode`, with a small log (`log.txt`).

Controller-as-mouse is provided by [AntiMicroX](https://github.com/AntiMicroX/antimicrox),
a free, open-source app (GPL-3.0). It's installed separately and not included here.

## If it doesn't work

Open `%LOCALAPPDATA%\LeanbackMode\log.txt` (paste that into File Explorer's
address bar). It says which controller turned TV mode on, for example
`controller turned on: Game controller (0079:0006)`.

- **TV mode stays on with the controller off:** something is still showing
  up as a game controller. That's often a 2.4 GHz dongle that isn't in X-input
  mode, or a device that only looks like a controller. Add the ID from the log to
  `IgnoreControllers` in settings (for example `IgnoreControllers=0079:0006`),
  or set `DetectControllers=XInput`.
- **Nothing happens when the controller turns on:** check that it shows up in
  Windows Settings > Bluetooth & devices. Then look in the log.
- **The controller doesn't move the pointer:** check that AntiMicroX is running
  in the tray, and that you're not in a game (the mouse pauses there).
