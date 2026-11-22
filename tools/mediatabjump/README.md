MEDIA TAB JUMP
==============

> Part of [Windows Unlocked Toolkit](../../README.md). The easiest way to
> install it is the toolkit's one-file installer, which lets you pick this tool
> from a menu. The steps below are for installing it on its own from this folder.

Click the "now playing" card in the Windows volume flyout and it takes you to
the browser tab that's playing it. Works with Edge (and Chrome, Brave, Firefox,
Opera, Vivaldi).


INSTALL
-------
1. Download this folder (or the whole repo as a zip) and extract it.
2. Double-click Install.cmd.
   If Windows asks whether to run it, choose Run (or More info > Run anyway).
3. Wait for "Done!", then press any key to close the window.

That's it. It starts automatically each time you sign in.


USE
---
Play something in a browser, click the volume/Wi-Fi icons on the taskbar, and
click the media card: the title, the picture, or the empty space around them.
The play/pause/next/previous buttons still work normally.

It should feel near-instant. In the background it notes where each browser
window keeps its tabs, so a click only has to ask the browser once per window.

When your pointer is over a browser's media card, a soft glow of light appears
around the card's edge. It slowly shifts colour, is brightest near your pointer,
and pulses briefly when you click. It switches off over the play/pause buttons
(those still just control playback). If Windows' "Animation effects" setting
is off, the glow stays still. To turn the glow off or back on, double-click
Toggle-Glow.cmd.


REMOVE
------
Double-click Uninstall.cmd. It stops the helper, removes it from startup, and
deletes its folder. You can also turn it off temporarily in
Task Manager > Startup apps ("MediaTabJump").


WHAT IT DOES (AND DOESN'T DO)
-----------------------------
- It notices mouse clicks and pointer movement so it can tell when you're on
  the media card. It ignores everything that isn't on the Windows panel, never
  changes or blocks a click, and never looks at the keyboard.
- The glow is its own see-through window that clicks pass straight through.
- If keyboard focus lands on a browser tab after switching (which makes Edge
  show the tab's hover preview), it moves focus to the page, as a real click
  would. If Windows won't let it bring Edge forward the normal way, it may tap
  Alt as a last resort.
- It reads the card and your browser's tab titles with UI Automation, the
  built-in Windows feature that screen readers use. No browser extension.
- It never connects to the internet.
- It's built on your PC from MediaTabJump.cs (readable source code) using the
  C# compiler that already comes with Windows. No admin rights needed.
- It lives in %LOCALAPPDATA%\MediaTabJump and keeps a small log there
  (log.txt, capped at about 1 MB) that includes the titles of media you click.
  It never records what's in other Windows panels (notifications, calendar,
  Wi-Fi...). Installing a new version starts a fresh log.


IF IT DOESN'T WORK
------------------
Open %LOCALAPPDATA%\MediaTabJump\log.txt (paste that into File Explorer's
address bar). It records what the helper saw when you clicked. Sharing the last
part of it is the quickest way to get it fixed.

If the installer says it "didn't stay running", Smart App Control or antivirus
may have blocked the newly built program.
