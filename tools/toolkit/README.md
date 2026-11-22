# Toolkit tray app

> Part of [Windows Unlocked Toolkit](../../README.md). Pick **Toolkit tray app**
> in the toolkit's one-file installer, or install it on its own from this folder.

One icon in the system tray for the whole toolkit.

- **Click the icon** for quick switches: Leanback mode or desktop mode, and
  on/off tiles for Leanback Mode, the controller mouse, Media Tab Jump and its
  hover glow. There's also **Pause everything for 1 hour**.
- **Open settings** for a page per tool:
  - **Leanback Mode:** when to use Leanback mode, how big everything gets, pointer size,
    the switch shortcut, staying in Leanback mode when the controller falls asleep,
    pointer and scroll speed, what each controller button does, and which
    controllers count.
  - **Media Tab Jump:** the hover glow's colour, brightness and size, with a live preview.
  - **Appearance:** light or dark, an accent colour (or your Windows accent), and the tray icon's style.
  - **Updates and about:** start with Windows, the website, source code and logs.

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
- Turning a tool off stops it and moves its sign-in entry to
  `%LOCALAPPDATA%\WindowsUnlockedToolkit\turned-off`, so it stays off after a
  restart. Turning it on puts the entry back.
- While it runs, Leanback Mode hides its own tray icon so there's just one.
- It never goes online. The website and GitHub buttons just open your browser.
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
