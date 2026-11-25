<#
.SYNOPSIS
    Removes the Windows Unlocked Toolkit tray app. The tools themselves (Leanback Mode, Media Tab Jump, ...) stay.

.DESCRIPTION
    1. Closes the app.
    2. Turns back on any tool you had turned off from the app (puts its startup entry back),
       and lets Leanback Mode show its own tray icon again.
    3. Removes the sign-in entry and Start menu shortcut, and deletes the app, its settings and its log.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$dataDir = Join-Path $env:LOCALAPPDATA 'WindowsUnlockedToolkit'
$appDir  = Join-Path $dataDir 'App'
$exe     = Join-Path $appDir 'WindowsUnlockedToolkit.exe'
$parked  = Join-Path $dataDir 'turned-off'
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startup = [Environment]::GetFolderPath('Startup')
$leanIni = Join-Path $env:LOCALAPPDATA 'LeanbackMode\settings.ini'

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Info([string]$text) { Write-Host "    $text" }

try {
    Write-Host ''
    Write-Host 'Windows Unlocked Toolkit tray app - uninstall' -ForegroundColor Cyan
    Write-Host ''

    Write-Step 'Closing the app'
    if (Get-Process -Name 'WindowsUnlockedToolkit' -ErrorAction SilentlyContinue) {
        if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -ArgumentList '--exit' -Wait -WindowStyle Hidden }
        Get-Process -Name 'WindowsUnlockedToolkit' -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
    Write-Info 'closed'

    Write-Step 'Putting tools back the way they start on their own'
    if (Test-Path -LiteralPath $parked) {
        foreach ($f in Get-ChildItem -LiteralPath $parked -File) {
            if ($f.Name -like '*.run.txt') {
                $name = $f.Name.Substring(0, $f.Name.Length - '.run.txt'.Length)
                if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }
                Set-ItemProperty -Path $runKey -Name $name -Value ([IO.File]::ReadAllText($f.FullName).Trim())
                Write-Info "turned $name back on at sign-in"
            } else {
                $dest = Join-Path $startup $f.Name
                $target = (New-Object -ComObject WScript.Shell).CreateShortcut($f.FullName).TargetPath
                if ($target -and -not (Test-Path -LiteralPath $target)) { continue } # that tool was uninstalled since
                if (-not (Test-Path -LiteralPath $dest)) { Move-Item -LiteralPath $f.FullName -Destination $dest; Write-Info "turned $($f.BaseName) back on at sign-in" }
            }
        }
        Remove-Item -LiteralPath $parked -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $leanIni) {
        $text = [IO.File]::ReadAllText($leanIni)
        $new = [regex]::Replace($text, '(?im)^\s*ShowTrayIcon\s*=.*$', 'ShowTrayIcon=true')
        if ($new -ne $text) { [IO.File]::WriteAllText($leanIni, $new); Write-Info 'Leanback Mode shows its own tray icon again' }
    }

    Write-Step 'Removing the app'
    Remove-ItemProperty -Path $runKey -Name 'WindowsUnlockedToolkit' -ErrorAction SilentlyContinue
    $menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Windows Unlocked Toolkit.lnk'
    if (Test-Path -LiteralPath $menuLink) { Remove-Item -LiteralPath $menuLink -Force }
    foreach ($p in $appDir, (Join-Path $dataDir 'settings.ini'), (Join-Path $dataDir 'log.txt'), (Join-Path $dataDir 'log.txt.old'),
                (Join-Path $dataDir 'updates'), (Join-Path $dataDir 'update-log.txt'), (Join-Path $dataDir 'update-status.ini')) {
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
    Write-Info 'done'

    Write-Host ''
    Write-Host 'The toolkit tray app is removed. Your tools are still installed and running on their own.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host 'Uninstall failed:' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
