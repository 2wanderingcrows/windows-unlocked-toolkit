# Leanback Mode for Windows

> Part of [Windows Unlocked Toolkit](../../README.md). The easiest way to
> install it is the toolkit's one-file installer, which lets you pick this tool
> from a menu. The steps below are for installing it on its own from this folder.

Turn on a game controller and your PC switches to **Leanback mode**: bigger text and
apps, a bigger mouse pointer, and the controller works as a mouse. Turn the
controller off and it goes back to **desktop mode**. The switch happens behind
a soft blur, so you never see the screen jump around.

If the controller just falls asleep on its own after sitting idle, your PC
stays in Leanback mode, so you can pick it back up mid-movie. Only switching
the controller off yourself takes you back to desktop mode.

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
keeps Leanback mode on (see [If it doesn't work](#if-it-doesnt-work)).

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

Installing again (or an automatic update from the toolkit) keeps your settings.
If you changed controller buttons in the toolkit app, your layout is kept too,
and the new default layout is saved next to it as `controller-mouse.amgp.new`.
If you turned Leanback Mode or the controller mouse off in the toolkit app,
it stays off.

## Use

| You do | What happens |
| --- | --- |
| Turn the controller on | Leanback mode, after half a second |
| Turn the controller off (hold its Home button) | Desktop mode, about a second later |
| Leave the controller until it falls asleep | Stays in Leanback mode |
| Press **Ctrl+Alt+T** | Switch between Leanback and desktop mode any time. Lasts until the controller next turns on or off. |
| Open a game | The controller mouse pauses so the game gets the controller, and comes back when you leave the game. |
| Open Playnite fullscreen, Kodi or Steam Big Picture | Same: these apps have their own controller controls, so the controller mouse steps aside. Press **Home** to take the controller back, and press it again to hand it back to the app. Edit the list in the tray app (Leanback page, "Step aside for controller apps"). |

Left-click the TV icon in the tray for the menu: switch modes, choose
Automatic / Always Leanback / Always desktop, pause the controller mouse, or edit
settings. If you use the Windows Unlocked Toolkit tray app, its icon replaces
this one and all of these settings are in its Leanback Mode page.

### Asleep or switched off?

Windows can't tell a controller that fell asleep from one you switched off;
both just disappear. So Leanback Mode looks at what happened right before:

- Nothing on the controller was touched for 5 minutes or more: it fell
  asleep, and Leanback mode stays on.
- You were using it, or you held its Home / Xbox button to switch it off:
  back to desktop mode.

To go back to desktop while the controller sleeps, press **Ctrl+Alt+T**. To
turn this off, set `StayInLeanbackWhenAsleep=false` (or use the switch in the
toolkit app). For controllers that aren't Xbox-style, "touched" means any
mouse or keyboard input, including the controller moving the pointer.

### More than one screen

Got a TV and a monitor on the same PC? Pick the **Leanback screen** (in the
toolkit app's Leanback Mode page, or the tray menu's **Leanback screen**). Only
that screen gets bigger; your other screens stay exactly as they are. When
Leanback mode starts, the pointer jumps onto that screen so the controller is
ready to go (turn that off with **Bring the pointer along**).

If the Leanback screen isn't connected (most TVs disappear from Windows when
they're switched off), Leanback Mode leaves your other screens alone. Switch the
TV on and it gets the Leanback size within a couple of seconds.

### Controller buttons (as a mouse)

| Button | Does |
| --- | --- |
| Left stick | Move the pointer |
| Right stick | Up/down scrolls, left/right switches browser tabs |
| A | Left click |
| X | Right click |
| B | Enter |
| **Y (hold)** | **Fast pointer** (see the tip below) |
| D-pad | Arrow keys |
| LT (tap) / RT | Space / Backspace |
| LT (hold) | Volume and window switching, see below |
| LB / RB | Browser back / forward |
| Start | Windows key (Start menu) |
| Back / Select | Refresh |
| Left stick click | Middle click |
| Right stick click | F11 (full screen) |
| Home | Nothing by default (pick an action or an app in the toolkit app) |

Every button can be changed in the toolkit app, including Home and each D-pad
direction. Extra buttons, like back paddles, show up there when your controller
reports them to Windows. Some pads in Xbox (X-input) mode keep their extra
buttons to themselves. For an 8BitDo pad, assign L4/R4 to another button in
8BitDo Ultimate Software, or switch the pad to a mode that reports them.

If the Home button opens the Xbox Game Bar, turn that off: press Win+G, open
Settings, and untick "Open Game Bar using this button on a controller".

> **Tip: hold Y for the fast pointer.** On a TV or an ultrawide monitor the
> pointer has a long way to travel. Hold **Y** and it moves about three times as
> fast, so it crosses the whole screen in a blink; let go and it's back to
> precise. Set how fast in the toolkit app (Leanback Mode > Controller as mouse).

### Hold LT for volume and window switching

Hold the left trigger and the controller gets a second layer:

| While holding LT | Does |
| --- | --- |
| Right stick up / down | Volume up / down. Tilt a little for fine steps, all the way to move fast. Windows shows its volume bar. |
| Right stick left / right | Flip through your open windows (like Alt+Tab). Let go of LT to switch to the one picked. |
| A | Play / pause |
| B | Mute |
| LB / RB | Previous / next track |

Everything else works as usual while you hold it, and a quick tap of LT still
presses Space. In the toolkit app's Leanback Mode page you can pick another
button to hold (RT, LB or RB), change the volume speed, set what every button
does in the second layer, or turn it off.

Volume and window switching need an Xbox-style controller (XInput). Most
8BitDo pads and 2.4 GHz dongles have an X-input mode. With other controllers,
turn this off in the toolkit app so LT presses Space again.

### Your own buttons and key combos

In the toolkit app's Leanback Mode page, click any button's action to pick
another one: clicks, Enter, Escape, media keys, **Last window (Alt+Tab)**,
**All windows (Win+Tab)**, **Show desktop**, **Close tab** and more. Choose
**Record a key combo** and press any keys, such as Ctrl+Shift+T or Alt+F4, to
make the button press exactly that. Choose **Open an app** to make a button
open any program, for example Start opening Playnite or Steam Big Picture
(apps it finds, such as Playnite, are listed right in the menu). Switch the Buttons card to **Holding LT** to
set the second layer the same way.

Button names follow the Xbox layout. On a PlayStation pad, A = Cross,
B = Circle, X = Square, Y = Triangle.

## Settings

Right-click the tray icon and choose **Edit settings**. Changes apply as soon
as you save. The file explains each option. The main ones:

- `Screen`: which screen gets bigger. `Main` (default) or a screen's name as Windows Settings > Display shows it, for example `Screen=LG TV SSCR2`. The log lists the connected screens.
- `MovePointerToScreen`: move the pointer onto that screen when Leanback mode starts.
- `TvScale` / `NormalScale`: that screen's display scaling in Leanback and desktop mode (for example 150 and 100).
- `TvPointerSize` / `NormalPointerSize`: pointer size in each mode.
- `Hotkey`: the switch shortcut, for example `Ctrl+Alt+T` or `Win+Alt+M`.
- `PauseMouseInGames`, `GameFolders`, `NotGames`: when the controller mouse steps aside for games.
- `StepAsideForApps`, `ControllerApps`: apps with their own controller controls (exe names, or words from the window title), separated by `;`.
- `StayInLeanbackWhenAsleep`, `AsleepAfterIdleMinutes`: stay in Leanback mode when the controller falls asleep, and how long it must sit untouched to count as asleep (default 5 minutes).
- `DetectControllers`: `All` (default) or `XInput` for Xbox-style only.
- `IgnoreControllers`: devices that should never count as a controller.
- `LayerTap`: what a quick tap of the layer button presses (default `Space`). Keys like `Ctrl+Shift+T`, `BrowserBack` or `MediaPlayPause`.
- `VolumeSpeed`: how fast the volume moves with the right stick tilted all the way, in percent per second (default 50).

Pointer speed and button mapping live in the AntiMicroX layout
(`%LOCALAPPDATA%\antimicrox\profiles\controller-mouse.amgp`). The easiest way
to change them is the toolkit app; you can also edit it from the AntiMicroX
window. The second layer is the layout's set 2.

## Remove

Double-click `Uninstall.cmd`. It switches back to desktop mode, stops both
apps, removes them from startup, and deletes Leanback Mode's folder.
AntiMicroX itself stays installed. To remove it too, run
`Uninstall.ps1 -RemoveAntiMicroX` from PowerShell.

## What it does (and doesn't do)

- It checks every quarter second whether a game controller is connected. It
  doesn't read your keyboard or what you type.
- While you hold the layer button (LT), it reads the controller's right stick
  and presses the volume keys, Alt+Tab, or the button's tap key for you. It
  only does this while the controller mouse is on and no game is in front.
- It changes Windows display scaling on one screen (your main one unless you
  pick another) and the pointer size, the same settings as in Windows Settings.
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
address bar). It says which controller turned Leanback mode on, for example
`controller turned on: Game controller (0079:0006)`.

- **Leanback mode stays on with the controller off:** if the log says the
  controller "fell asleep", that's on purpose (see above). Otherwise something is still showing
  up as a game controller. That's often a 2.4 GHz dongle that isn't in X-input
  mode, or a device that only looks like a controller. Add the ID from the log to
  `IgnoreControllers` in settings (for example `IgnoreControllers=0079:0006`),
  or set `DetectControllers=XInput`.
- **Nothing happens when the controller turns on:** check that it shows up in
  Windows Settings > Bluetooth & devices. Then look in the log.
- **The controller doesn't move the pointer:** check that AntiMicroX is running
  in the tray, and that you're not in a game (the mouse pauses there).
