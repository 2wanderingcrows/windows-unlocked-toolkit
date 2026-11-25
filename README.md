# Windows Unlocked Toolkit

Small, free tools that fix everyday annoyances in Windows. Install the ones you
want, skip the rest.

**[Website](https://2wanderingcrows.github.io/windows-unlocked-toolkit/)** ·
**[Download the setup](https://2wanderingcrows.github.io/windows-unlocked-toolkit/WindowsUnlockedToolkitSetup.exe)** ·
[Releases](https://github.com/2wanderingcrows/windows-unlocked-toolkit/releases)

## Tools

<details>
<summary><b>Leanback Mode</b>: use your PC from the couch with a game controller.</summary>

Pick up a controller and Windows rearranges itself for the couch. Put it down and your desktop is back.

- Turns on with your controller, off when you switch it off
- Bigger text and a pointer you can see across the room
- **Hold Y for a fast pointer** that crosses a big TV in a blink
- Hold LT and tilt the right stick for volume or window switching
- **Plays nice with Playnite, Steam Big Picture and Kodi**, handing them the controls while they're in front
- **Map any button** to a key combo, or to open an app
- **Finds every button on your controller**: Home, D-pad, paddles and extras
- Steps aside on its own when a game starts
- Monitor and TV? Only the screen you pick gets bigger
- Xbox, PlayStation, Switch Pro, 8BitDo and generic Bluetooth or 2.4 GHz pads

[More about Leanback Mode](tools/leanback)
</details>

<details>
<summary><b>Media Tab Jump</b>: click the player in the volume flyout to jump to the tab that is playing.</summary>

Something's playing in one of thirty tabs. Click the player in the volume flyout and land right on it.

- Jumps to the exact tab, even in another window or screen
- Edge, Chrome, Brave, Firefox, Opera and Vivaldi
- A soft glow shows the card is clickable
- Play, pause and skip work like always
- No browser extension, nothing to sign in to

[More about Media Tab Jump](tools/mediatabjump)
</details>

<details>
<summary><b>AutoZoom</b>: set a zoom level per app for maximized and smaller windows.</summary>

Set how zoomed in each app is when maximized and when not. AutoZoom switches as you resize.

- Any app that zooms with Ctrl and +: Claude, Teams, Discord, VS Code, browsers
- Each window keeps its own zoom
- Different levels per screen, switching as you drag across
- Set it all from the tray app, applied instantly
- Only presses the app's own zoom keys, never reads the screen

[More about AutoZoom](tools/autozoom)
</details>

<details>
<summary><b>Tray app</b>: one tray icon with switches and settings for every tool.</summary>

Every tool behind one tray icon, with quick switches and a settings page for each.

- One click between Leanback and desktop mode
- Controller speed, buttons and key combos without config files
- Glow colours and AutoZoom levels with live preview
- Light or dark, nine accent colours, three icon styles
- Pause everything for an hour

[More about Tray app](tools/toolkit)
</details>

Coming later: Window Tab Organizer, Startup Minimizer.

## Install

1. Download [`WindowsUnlockedToolkitSetup.exe`](https://2wanderingcrows.github.io/windows-unlocked-toolkit/WindowsUnlockedToolkitSetup.exe).
2. Open it. If Windows says it protected your PC, choose **More info**, then **Run anyway**.
   That warning is a false alarm: the setup app isn't digitally signed yet. A
   signed version is coming in an update, through SignPath Foundation's free
   signing for open-source projects. If your browser blocks the download, open
   your downloads list and choose **Keep**.
3. Pick your tools and press **Install**. No admin rights needed.

See the [code signing policy](CODE_SIGNING_POLICY.md) for how the setup app is built and signed.

Updates install themselves in the background and keep your settings. You can
turn that off in the tray app under **Updates and about**.

<details>
<summary>Other ways to install</summary>

From PowerShell:

```powershell
irm https://2wanderingcrows.github.io/windows-unlocked-toolkit/install.ps1 | iex
```

Or download [`WindowsUnlockedToolkit.cmd`](https://2wanderingcrows.github.io/windows-unlocked-toolkit/WindowsUnlockedToolkit.cmd).
To install a single tool, open its folder under [`tools/`](tools) and run `Install.cmd`.
</details>

## Questions

<details>
<summary>Is it safe?</summary>

All of the code is here and is compiled on your own PC, so nothing is
pre-built. The tools don't go online. The tray app's update check downloads
`manifest.ini` (the version list) from the website, and `install.ps1` when
there's an update. Nothing about you is sent.
</details>

<details>
<summary>What does the setup download?</summary>

Only AntiMicroX (through `winget`), and only if you pick Leanback Mode and
don't already have it. Because the tools are compiled on your PC, antivirus or
Smart App Control may ask about them the first time.
</details>

<details>
<summary>How do I remove a tool?</summary>

Go to **Settings > Apps > Windows Unlocked Toolkit > Uninstall** and pick what
to remove. Settings a tool changed, like display scaling, are put back.
</details>

<details>
<summary>Something isn't working</summary>

Each tool keeps a small log in its folder (its README says where).
[Open an issue](https://github.com/2wanderingcrows/windows-unlocked-toolkit/issues)
with the last few lines.
</details>

<details>
<summary>How the repo is organized</summary>

- Each tool is a folder in [`tools/`](tools) with a `tool.json`.
- On every push to `main`, [GitHub Actions](.github/workflows/build.yml) runs
  [`scripts/build.py`](scripts/build.py), which packs every tool into one
  installer and rebuilds the website.
- The setup app ([`setup/`](setup)) is built by
  [`scripts/build-setup.ps1`](scripts/build-setup.ps1). When a tool's version
  changes, a release is published and installed tray apps update.
- The website is [`site/index.template.html`](site/index.template.html), with
  each tool's text in [`site/showcase.json`](site/showcase.json) and pictures in
  [`site/assets/`](site/assets).
</details>

## Friends

[The Haugen Project](https://thehaugenproject.com/): a history atlas, a live
California and Oregon outage board, quizzes and browser games, with no ads or
tracking.

## License

[MIT](LICENSE). Made with help from Claude.
