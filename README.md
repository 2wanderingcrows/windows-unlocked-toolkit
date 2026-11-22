# Windows Unlocked Toolkit

Small, open-source tools that make Windows nicer to use. Each one is plain,
readable source code, needs no admin rights unless it says so, and comes with
its own uninstaller.

**Website and one-file installer:** https://maxsilbert805.github.io/windows-unlocked-toolkit

## Install

**Easiest:** download [`WindowsUnlockedToolkit.cmd`](https://maxsilbert805.github.io/windows-unlocked-toolkit/WindowsUnlockedToolkit.cmd)
and double-click it. A menu lets you pick which tools to install. Run it again
and choose **U** to uninstall one.

**Or paste this into PowerShell:**

```powershell
irm https://maxsilbert805.github.io/windows-unlocked-toolkit/install.ps1 | iex
```

**Or install a single tool by hand:** open its folder under [`tools/`](tools),
download it, and run its `Install.cmd`.

Windows may say the file is from an unknown publisher. Choose **More info**,
then **Run anyway**. The installer holds each tool's full source. The only
thing it downloads is AntiMicroX (with `winget`), and only if you pick Leanback
Mode and don't have it yet.

## Tools

| Tool | What it does |
| --- | --- |
| [Leanback Mode](tools/leanback) | Turn on a game controller and your PC switches to a couch-friendly TV mode: bigger text and pointer, and the controller works as a mouse. Works with Xbox, PlayStation, Switch Pro, 8BitDo and generic Bluetooth or 2.4 GHz pads. |
| [Media Tab Jump](tools/mediatabjump) | Click the now-playing card in the Windows volume flyout to jump straight to the browser tab that's playing it. |

More are on the way, along with a toolkit tray app for changing every tool's
settings in one place.

## How this repo works

- Every tool lives in its own folder in [`tools/`](tools), with a `tool.json`
  describing it.
- On every change to `main`, [GitHub Actions](.github/workflows/build.yml) runs
  [`scripts/build.py`](scripts/build.py), which packs every tool into one
  installer and rebuilds the website. Nobody edits the combined installer by
  hand, so it can't fall out of date.

## License

[MIT](LICENSE). Use it, change it, share it.
