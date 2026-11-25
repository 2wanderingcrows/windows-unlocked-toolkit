AUTOZOOM
========

> Part of [Windows Unlocked Toolkit](../../README.md). The easiest way to
> install it is the toolkit's one-file installer, which lets you pick this tool
> from a menu. The steps below are for installing it on its own from this folder.

Pick any app and choose how big it looks in two situations: when its window is
maximized, and when it isn't (snapped, restored, half the screen). AutoZoom
switches between the two by itself as you maximize and restore the window.

Handy when an app is comfortable full-screen but cramped in a smaller window,
or the other way round.


INSTALL
-------
1. Download this folder (or the whole repo as a zip) and extract it.
2. Double-click Install.cmd.
   If Windows asks whether to run it, choose Run (or More info > Run anyway).
3. Wait for "Done!", then press any key to close the window.

It starts automatically each time you sign in. A fresh install starts with one
example app, Claude (4 steps bigger when maximized, 1 step bigger when not).


USE
---
The easy way is the Windows Unlocked Toolkit tray app: open its settings, go to
the AutoZoom page, click "Add an app" and pick an app that's open. Then set its
zoom for "Maximized" and "Not maximized" with the - and + buttons. Changes apply
straight away.

Zoom is counted in steps. One step is one press of the app's own zoom-in key
(Ctrl and +), and negative steps are presses of zoom-out (Ctrl and -), counted
from the app's normal 100% size. How big a step is depends on the app: in
Claude, 1 step is about 110% and 4 steps about 144%; in Edge or Chrome, 1 step
is 110% and 4 steps 175%.

You can also edit %LOCALAPPDATA%\AutoZoom\settings.ini yourself. Changes apply
within a second:

    App.claude=4,1          one line per app: maximized steps, then not-maximized steps
    App.ms-teams=2,0        the name is the app's process name (Task Manager > Details, without .exe)
    PollMs=10               how often the window in front is checked, in ms
    KeyGapMs=0              pause between key presses; raise to 10-20 if a switch lands on the wrong size
    Log=true                write one line per zoom change to log.txt

Works with apps that zoom with Ctrl and +, Ctrl and -, and Ctrl and 0: Electron
apps such as Claude, Discord, Microsoft Teams, Slack and VS Code; browsers; and
Notepad. Browsers remember zoom per website, so they work best with a single
site (for example an installed web app).

Each window is tracked on its own, so two windows of the same app (say, two
accounts side by side) never throw each other's zoom off.


MORE THAN ONE SCREEN
--------------------
A size that's right on a laptop screen can be tiny on a big monitor or a TV.
In the toolkit app's AutoZoom page, pick a screen under "Levels for" and set
different levels for it. Apps you don't change there use their all-screens
levels. When you drag a window to another screen, AutoZoom switches its zoom
as soon as you let go.

In settings.ini that's one extra line per app and screen, using the screen's
name as Windows Settings > Display shows it ("Built-in display" for a laptop's
own screen):

    App.claude=4,1                  everywhere else
    App.claude@LG TV SSCR2=7,3      on the TV


REMOVE
------
Double-click Uninstall.cmd. It stops AutoZoom, removes it from startup, and
deletes its folder and settings. Apps keep the zoom they are at; press Ctrl+0
in an app to put it back to 100%. You can also turn AutoZoom off for a while in
the toolkit tray app, or in Task Manager > Startup apps ("AutoZoom").


WHAT IT DOES (AND DOESN'T DO)
-----------------------------
- It checks which window is in front, whether it's maximized and which screen
  it's on. Nothing else.
- When a window of an app on your list changes between maximized and not, it
  presses that app's zoom keys (Ctrl with +, - or 0). The first time it sees a
  window it presses Ctrl+0 once to start from 100%.
- It never reads what you type, what's on screen, or window titles.
- It never connects to the internet.
- It's built on your PC from AutoZoom.cs (readable source code) using the C#
  compiler that already comes with Windows. No admin rights needed.
- It lives in %LOCALAPPDATA%\AutoZoom and keeps a small log there (log.txt,
  capped at about 200 KB) with one line per zoom change: the app's process
  name and how many steps it zoomed.


IF IT DOESN'T WORK
------------------
- If you zoom an app by hand, AutoZoom doesn't know, so its levels move by the
  same amount until the window is closed. Press Ctrl+0 and maximize or restore
  the window once, or turn AutoZoom off and on in the toolkit.
- If a switch sometimes lands on the wrong size, set KeyGapMs=15 in
  settings.ini.
- Open %LOCALAPPDATA%\AutoZoom\log.txt (paste that into File Explorer's address
  bar) to see each zoom change it made.
- If the installer says it "didn't stay running", Smart App Control or
  antivirus may have blocked the newly built program.

Upgrading from Claude AutoZoom (the first, Claude-only version)? The installer
removes its scheduled task and keeps your Claude zoom levels.
