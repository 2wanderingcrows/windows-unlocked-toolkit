<#
.SYNOPSIS
    Removes Leanback Mode for Windows and its start-at-sign-in entries. Optionally removes AntiMicroX too.

.DESCRIPTION
    1. Closes Leanback Mode cleanly. If you're in Leanback mode it switches back to desktop mode first,
       then your normal display scaling and pointer size are re-applied as a safety net.
    2. Stops AntiMicroX and removes both Startup shortcuts, so nothing starts at sign-in.
    3. Deletes %LOCALAPPDATA%\LeanbackMode (the app, its settings and log).

    By default AntiMicroX stays installed with your controller layout, so you can still open it yourself.
    With -RemoveAntiMicroX it is uninstalled too, and %LOCALAPPDATA%\antimicrox (layout + settings) is deleted.

    The files in this folder are never touched, so you can reinstall with Install.cmd any time.
    Add -WhatIf to see what would happen without changing anything.

.PARAMETER RemoveAntiMicroX
    Also uninstall AntiMicroX and delete its layout and settings.

.EXAMPLE
    .\uninstall.ps1 -WhatIf
.EXAMPLE
    .\uninstall.ps1
.EXAMPLE
    .\uninstall.ps1 -RemoveAntiMicroX
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$RemoveAntiMicroX
)

$ErrorActionPreference = 'Stop'

$local   = $env:LOCALAPPDATA
$amxDir  = Join-Path $local 'antimicrox'
$tvDir   = Join-Path $local 'LeanbackMode'
$tvExe   = Join-Path $tvDir 'LeanbackMode.exe'
$oldDir  = Join-Path $local 'ControllerTVMode'   # name used before Leanback Mode
$startup = [Environment]::GetFolderPath('Startup')
$shortcuts = @(
    (Join-Path $startup 'AntiMicroX (controller mouse).lnk'),
    (Join-Path $startup 'Leanback Mode.lnk'),
    (Join-Path $startup 'Controller TV Mode.lnk')
)

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Info([string]$text) { Write-Host "    $text" }

# ---------------------------------------------------------------- 1. Leanback Mode -> desktop mode

Write-Step 'Leanback Mode'
if (Test-Path -LiteralPath $tvExe) {
    if ($PSCmdlet.ShouldProcess('Leanback Mode', 'Close it and return to desktop mode')) {
        if (Get-Process -Name LeanbackMode -ErrorAction SilentlyContinue) {
            Start-Process -FilePath $tvExe -ArgumentList '--exit' -Wait   # graceful: plays the transition back to desktop mode
            Write-Info 'closed'
        }
        Start-Process -FilePath $tvExe -ArgumentList '--reset' -Wait      # safety net: normal scaling + pointer size
        Write-Info 'normal display scaling and pointer size applied'
    }
} else {
    Write-Info 'not installed'
}
$left = Get-Process -Name LeanbackMode, ControllerTVMode -ErrorAction SilentlyContinue
if ($left) { $left | Stop-Process -Force }

# ---------------------------------------------------------------- 2. AntiMicroX + startup entries

Write-Step 'Start at sign-in'
$amx = Get-Process -Name antimicrox -ErrorAction SilentlyContinue
if ($amx) { $amx | Stop-Process -Force; if (-not $WhatIfPreference) { Write-Info 'stopped AntiMicroX' } }
foreach ($s in $shortcuts) {
    if (Test-Path -LiteralPath $s) {
        Remove-Item -LiteralPath $s -Force
        if (-not $WhatIfPreference) { Write-Info "removed $(Split-Path $s -Leaf)" }
    }
}

# ---------------------------------------------------------------- 3. files

Write-Step 'Files'
if (Test-Path -LiteralPath $tvDir) {
    Remove-Item -LiteralPath $tvDir -Recurse -Force
    if (-not $WhatIfPreference) { Write-Info "deleted $tvDir" }
} else {
    Write-Info 'Leanback Mode folder already gone'
}
if (Test-Path -LiteralPath $oldDir) {
    Remove-Item -LiteralPath $oldDir -Recurse -Force
    if (-not $WhatIfPreference) { Write-Info "deleted $oldDir (older Controller TV Mode)" }
}

if ($RemoveAntiMicroX) {
    Write-Step 'AntiMicroX'
    if ($PSCmdlet.ShouldProcess('AntiMicroX', 'Uninstall with winget')) {
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            winget uninstall --id AntiMicroX.antimicrox -e --silent | Out-Host
        } else {
            Write-Info 'winget not available: uninstall AntiMicroX from Settings > Apps instead.'
        }
    }
    if (Test-Path -LiteralPath $amxDir) {
        Remove-Item -LiteralPath $amxDir -Recurse -Force
        if (-not $WhatIfPreference) { Write-Info "deleted $amxDir" }
    }
} else {
    Write-Info 'AntiMicroX kept (use -RemoveAntiMicroX to remove it and its layout too)'
}

Write-Host ''
if ($WhatIfPreference) {
    Write-Host 'Dry run only (-WhatIf): nothing was changed.' -ForegroundColor Yellow
} else {
    Write-Host 'Leanback Mode is uninstalled. Run Install.cmd to put it back.' -ForegroundColor Green
}
