<#
.SYNOPSIS
    Installs Leanback Mode for Windows: TV mode when a controller turns on, plus controller-as-mouse.

.DESCRIPTION
    1. Installs AntiMicroX (free, open-source controller-to-mouse app) with winget if it isn't
       installed yet. Windows may ask for admin approval for that one step.
    2. Adds the Leanback controller layout to AntiMicroX. Your other AntiMicroX settings and
       layouts are left alone.
    3. Builds LeanbackMode.exe from LeanbackMode.cs with the C# compiler that ships with Windows,
       into %LOCALAPPDATA%\LeanbackMode.
    4. Upgrades an older "Controller TV Mode" install, keeping its settings.
    5. Adds both apps to your Startup folder so they run (tray only) at sign-in, and starts them.

    Safe to re-run: it updates everything in place and keeps your settings.

.PARAMETER NoStart
    Install everything but don't start the apps now.
#>
[CmdletBinding()]
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$here       = $PSScriptRoot
$local      = $env:LOCALAPPDATA
$amxExe     = Join-Path $env:ProgramFiles 'AntiMicroX\bin\antimicrox.exe'
$amxDir     = Join-Path $local 'antimicrox'
$amxProfile = Join-Path $amxDir 'profiles\controller-mouse.amgp'
$appDir     = Join-Path $local 'LeanbackMode'
$appExe     = Join-Path $appDir 'LeanbackMode.exe'
$oldDir     = Join-Path $local 'ControllerTVMode'   # name used before Leanback Mode
$startup    = [Environment]::GetFolderPath('Startup')
$csc        = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Info([string]$text) { Write-Host "    $text" }

function New-Shortcut([string]$path, [string]$target, [string]$arguments, [string]$description) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $target
    $lnk.Arguments = $arguments
    $lnk.WorkingDirectory = Split-Path $target -Parent
    $lnk.Description = $description
    $lnk.WindowStyle = 7 # minimized (both apps are tray-only anyway)
    $lnk.Save()
    Write-Info "starts at sign-in: $(Split-Path $path -Leaf)"
}

try {
    Write-Host ''
    Write-Host 'Leanback Mode for Windows - install' -ForegroundColor Cyan
    Write-Host ''

    # ------------------------------------------------------------ checks

    foreach ($f in 'LeanbackMode.cs', 'antimicrox_settings.ini', 'controller-mouse.amgp') {
        if (-not (Test-Path -LiteralPath (Join-Path $here $f))) { throw "Missing $f. Keep it in the same folder as Install.ps1." }
    }
    if (-not (Test-Path -LiteralPath $csc)) { throw "The C# compiler wasn't found at $csc (it's part of .NET Framework 4.x, built into Windows 10 and 11)." }

    # ------------------------------------------------------------ 1. AntiMicroX

    Write-Step 'AntiMicroX (turns the controller into a mouse)'
    if (Test-Path -LiteralPath $amxExe) {
        Write-Info 'already installed'
    } else {
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
            throw 'winget is not available. Install AntiMicroX from https://github.com/AntiMicroX/antimicrox/releases, then run this installer again.'
        }
        Write-Info 'installing with winget (approve the admin prompt if Windows shows one)...'
        winget install --id AntiMicroX.antimicrox -e --silent --accept-package-agreements --accept-source-agreements | Out-Host
        if (-not (Test-Path -LiteralPath $amxExe)) { throw "AntiMicroX didn't install to $amxExe." }
    }

    # ------------------------------------------------------------ 2. stop running copies

    Write-Step 'Stopping running copies'
    # Stopped directly (not "--exit") so your current TV/desktop mode stays as it is;
    # the new copy picks it up when it starts.
    foreach ($name in 'antimicrox', 'LeanbackMode', 'ControllerTVMode') {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if ($procs) { $procs | Stop-Process -Force; Write-Info "stopped $name" }
    }
    Start-Sleep -Milliseconds 800

    # ------------------------------------------------------------ 3. AntiMicroX layout

    Write-Step 'Controller layout'
    New-Item -ItemType Directory -Force -Path (Split-Path $amxProfile -Parent) | Out-Null
    $newProfile = [IO.File]::ReadAllText((Join-Path $here 'controller-mouse.amgp'))
    if ((Test-Path -LiteralPath $amxProfile) -and ([IO.File]::ReadAllText($amxProfile) -ne $newProfile)) {
        Copy-Item -LiteralPath $amxProfile -Destination "$amxProfile.bak" -Force
        Write-Info 'backed up your previous layout as controller-mouse.amgp.bak'
    }
    [IO.File]::WriteAllText($amxProfile, $newProfile)
    Write-Info 'installed controller-mouse.amgp'
    $amxSettings = Join-Path $amxDir 'antimicrox_settings.ini'
    if (Test-Path -LiteralPath $amxSettings) {
        Write-Info 'kept your existing AntiMicroX settings'
    } else {
        Copy-Item -LiteralPath (Join-Path $here 'antimicrox_settings.ini') -Destination $amxSettings
        Write-Info 'installed AntiMicroX settings (start in tray, smooth mouse)'
    }

    # ------------------------------------------------------------ 4. Leanback Mode app

    Write-Step 'Building Leanback Mode'
    New-Item -ItemType Directory -Force -Path (Join-Path $appDir 'src') | Out-Null
    $srcCopy = Join-Path $appDir 'src\LeanbackMode.cs'
    Copy-Item -LiteralPath (Join-Path $here 'LeanbackMode.cs') -Destination $srcCopy -Force
    $buildOutput = & $csc /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$appExe" $srcCopy 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw 'Building LeanbackMode.exe failed (see errors above).'
    }
    Write-Info "built $appExe"

    # Upgrade from Controller TV Mode: keep its settings and current mode, then remove it.
    if (Test-Path -LiteralPath $oldDir) {
        foreach ($f in 'settings.ini', 'state.ini') {
            $from = Join-Path $oldDir $f
            $to = Join-Path $appDir $f
            if ((Test-Path -LiteralPath $from) -and -not (Test-Path -LiteralPath $to)) {
                Copy-Item -LiteralPath $from -Destination $to
                Write-Info "kept your Controller TV Mode $f"
            }
        }
        Remove-Item -LiteralPath $oldDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Info 'removed the old Controller TV Mode app (Leanback Mode replaces it)'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $appDir 'settings.ini'))) {
        Write-Info 'settings will be created on first start, using your current display scaling as "desktop mode"'
    }

    # ------------------------------------------------------------ 5. startup + start

    Write-Step 'Start at sign-in'
    $oldShortcut = Join-Path $startup 'Controller TV Mode.lnk'
    if (Test-Path -LiteralPath $oldShortcut) { Remove-Item -LiteralPath $oldShortcut -Force }
    New-Shortcut (Join-Path $startup 'AntiMicroX (controller mouse).lnk') $amxExe "--tray --profile `"$amxProfile`"" 'Controller as mouse (AntiMicroX, tray only)'
    New-Shortcut (Join-Path $startup 'Leanback Mode.lnk') $appExe '' 'TV mode while a game controller is on'

    if ($NoStart) {
        Write-Step 'Not starting the apps (-NoStart); they start at your next sign-in.'
    } else {
        Write-Step 'Starting'
        Start-Process -FilePath $amxExe -ArgumentList '--tray', '--profile', "`"$amxProfile`"" -WorkingDirectory (Split-Path $amxExe -Parent)
        Start-Process -FilePath $appExe -WorkingDirectory $appDir
        Write-Info 'AntiMicroX and Leanback Mode are running in the system tray.'
    }

    Write-Host ''
    Write-Host 'Leanback Mode is installed.' -ForegroundColor Green
    Write-Host '  Turn a controller on for TV mode (bigger text and pointer); turn it off for desktop mode.'
    Write-Host '  Ctrl+Alt+T switches modes any time. Settings: right-click the TV icon in the tray > Edit settings.'
}
catch {
    Write-Host ''
    Write-Host 'Install failed:' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
