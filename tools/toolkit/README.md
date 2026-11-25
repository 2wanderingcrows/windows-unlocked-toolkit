# Toolkit tray app

> Part of [Windows Unlocked Toolkit](../../README.md). Pick **Toolkit tray app**
> in the toolkit's one-file installer, or install it on its own from this folder.

One icon in the system tray for the whole toolkit.

- **Click the icon** for quick switches: Leanback mode or desktop mode, and
  on/off tiles for Leanback Mode, the controller mouse, Media Tab Jump, its
  hover glow, and AutoZoom. There's also **Pause everything for 1 hour**.
- **Open settings** for a page per tool:
  - **Leanback Mode:** when to use Leanback mode, which screen gets bigger (with
    more than one), how big everything gets, pointer size,
    the switch shortcut, staying in Leanback mode when the controller falls asleep,
    pointer and scroll speed, what each controller button does (pick an action or
    record your own key combo), holding LT for volume and window switching (with
    its own set of buttons), and which controllers count.
  - **Media Tab Jump:** the hover glow's colour, brightness and size, with a live preview,
    and (with more than one screen) whether the tab comes to the screen you're on.
  - **AutoZoom:** which apps it zooms, and how many steps each one gets when maximized and when not,
    on every screen or on one screen in particular.
  - **Appearance:** light or dark, an accent colour (or your Windows accent), and the tray icon's style.
  - **Updates and about:** check for updates, **Update automatically** (on by default),
    start with Windows, the website, source code and logs.

It shows settings for the tools you have installed; the others say "Not installed".

## Install

Double-click `Install.cmd`. It builds the app on your PC and starts it with the
settings window open. It then starts each time you sign in, and it's in the
Start menu as **Windows Unlocked Toolkit** (that opens the settings window).

The icon may land in the tray's overflow (the **^** next to the clock). Drag it
onto the taskbar to keep it in view.

## Remove

Double-click `Uninstall.cmd`. The tools themselves stay installed and keep
running on their own. Any tool you turned off from the app is turned back on
at sign-in, and Leanback Mode shows its own tray icon again.

## What it does (and doesn't do)

- The tools keep running as their own small apps, so a problem in one can't
  take down the others. The tray app only reads their status files and changes
  their settings files:
  - Leanback Mode: `%LOCALAPPDATA%\LeanbackMode\settings.ini` (it reads `status.ini` to show the current mode)
  - Controller mouse: the AntiMicroX layout `%LOCALAPPDATA%\antimicrox\profiles\controller-mouse.amgp`.
    Your original is kept once as `controller-mouse.amgp.before-toolkit`.
  - Media Tab Jump: `%LOCALAPPDATA%\MediaTabJump\settings.ini` and `glow-off.txt`
  - AutoZoom: `%LOCALAPPDATA%\AutoZoom\settings.ini`
- Turning a tool off stops it and moves its sign-in entry to
  `%LOCALAPPDATA%\WindowsUnlockedToolkit\turned-off`, so it stays off after a
  restart. Turning it on puts the entry back.
- While it runs, Leanback Mode hides its own tray icon so there's just one.
- **Automatic updates.** A couple of minutes after it starts, and then every few
  hours, it downloads `manifest.ini` from the toolkit's website: a short list of
  each tool's newest version. If a tool you have is newer there, it waits until
  you've been away from the PC for 10 minutes, downloads `install.ps1` from the
  same website and runs it hidden. That reinstalls only the tools that changed,
  keeps their settings, leaves tools you turned off turned off, and keeps
  controller buttons you changed. Updating the tray app itself restarts it.
  The last run is logged in `update-log.txt` next to `log.txt`.
- Turn off **Update automatically** and it never goes online by itself; it only
  checks when you press **Check for updates**. The website and GitHub buttons
  just open your browser.
- It's built on your PC from `WindowsUnlockedToolkit.cs` and the `.xaml`
  layout files (readable source) with the C# compiler that comes with Windows.
  No admin rights needed.
- It lives in `%LOCALAPPDATA%\WindowsUnlockedToolkit\App`, with its settings
  and a small log (`log.txt`) one folder up.

## If it doesn't work

- **No icon:** check the tray overflow (**^**). If it isn't running, open
  **Windows Unlocked Toolkit** from the Start menu.
- **A tool says "Not installed":** install that tool from the toolkit's
  one-file installer. The tray app finds it within a second.
- **Something else:** open **Updates and about > Open logs folder** and look at
  `log.txt`, or [open an issue](https://github.com/2wanderingcrows/windows-unlocked-toolkit/issues)
  with the last few lines.
