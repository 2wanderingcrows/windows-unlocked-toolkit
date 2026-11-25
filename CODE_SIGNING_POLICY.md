# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/),
certificate by [SignPath Foundation](https://signpath.org/).

This covers `WindowsUnlockedToolkitSetup.exe`, the setup app on the
[website](https://2wanderingcrows.github.io/windows-unlocked-toolkit/) and on the
[Releases page](https://github.com/2wanderingcrows/windows-unlocked-toolkit/releases).
It is built from this repository by GitHub Actions
([`.github/workflows/build.yml`](.github/workflows/build.yml)) on GitHub-hosted
runners. Only that build is signed, and only from the `main` branch.

The tools it installs are not prebuilt: their source code is inside the setup
app and is compiled on your PC with the C# compiler that ships with Windows.

## Team roles

- Committers and reviewers: [2wanderingcrows](https://github.com/2wanderingcrows) (Max).
  Changes are proposed as pull requests, many of them written with Claude (an AI
  assistant), and Max reviews and merges every one. Nobody else can commit.
- Approvers: [2wanderingcrows](https://github.com/2wanderingcrows) (Max)
  approves each signing request.

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

The setup app does not send any information anywhere. It installs what you
pick on your own PC.

The toolkit's tray app checks for updates: every few hours it downloads a short
version list (`manifest.ini`) from the project website, and when there is an
update, the installer (`install.ps1`) from the same website. It sends nothing
about you or your PC beyond what any web request includes (your IP address and
the app's name and version), and the project collects no analytics. You can
turn automatic updates off in the tray app under **Updates and about**; it then
only goes online when you press **Check for updates**. Leanback Mode may install
AntiMicroX with Windows' own `winget`, which downloads it from its publisher.
No other part of the toolkit goes online.
