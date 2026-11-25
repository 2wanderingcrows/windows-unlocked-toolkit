<#
.SYNOPSIS
    Installs the Windows Unlocked Toolkit tray app: one tray icon with quick switches and settings for every tool.

.DESCRIPTION
    1. Builds WindowsUnlockedToolkit.exe from WindowsUnlockedToolkit.cs and the .xaml layouts with the
       C# compiler that ships with Windows, into %LOCALAPPDATA%\WindowsUnlockedToolkit\App.
    2. Starts it at sign-in (a value in HKCU\...\Run, no admin rights) and adds it to the Start menu.
    3. Starts it and opens the settings window.

    Safe to re-run: it replaces the app and keeps your settings.
    When the setup app or an automatic update runs it (WUT_QUIET=1), it starts the app in the
    tray without opening the settings window, and leaves "Start with Windows" as you set it.

.PARAMETER NoStart
    Install but don't start it now.
#>
[CmdletBinding()]
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$dataDir = Join-Path $env:LOCALAPPDATA 'WindowsUnlockedToolkit'
$appDir  = Join-Path $dataDir 'App'
$srcDir  = Join-Path $appDir 'src'
$exe     = Join-Path $appDir 'WindowsUnlockedToolkit.exe'
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$fw      = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $fw 'csc.exe'))) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc     = Join-Path $fw 'csc.exe'
$wpf     = Join-Path $fw 'WPF'
$sources = 'WindowsUnlockedToolkit.cs', 'Theme.xaml', 'MainWindow.xaml', 'Flyout.xaml'
$quiet   = $env:WUT_QUIET -eq '1'
$hadApp  = Test-Path -LiteralPath $exe

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Info([string]$text) { Write-Host "    $text" }

try {
    Write-Host ''
    Write-Host 'Windows Unlocked Toolkit tray app - install' -ForegroundColor Cyan
    Write-Host ''

    foreach ($f in $sources) {
        if (-not (Test-Path -LiteralPath (Join-Path $here $f))) { throw "Missing $f. Keep it in the same folder as Install.ps1." }
    }
    if (-not (Test-Path -LiteralPath $csc)) { throw "The C# compiler wasn't found at $csc (it's part of .NET Framework 4.x, built into Windows 10 and 11)." }
    if (-not (Test-Path -LiteralPath (Join-Path $wpf 'PresentationFramework.dll'))) { throw "Windows Presentation Foundation wasn't found in $wpf (it's part of .NET Framework 4.x)." }

    Write-Step 'Closing the running copy'
    if (Get-Process -Name 'WindowsUnlockedToolkit' -ErrorAction SilentlyContinue) {
        if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -ArgumentList '--exit' -Wait -WindowStyle Hidden }
        Get-Process -Name 'WindowsUnlockedToolkit' -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 500
        Write-Info 'closed'
    } else {
        Write-Info 'not running'
    }

    Write-Step 'Building the tray app'
    New-Item -ItemType Directory -Force -Path $srcDir | Out-Null
    foreach ($f in $sources) { Copy-Item -LiteralPath (Join-Path $here $f) -Destination (Join-Path $srcDir $f) -Force }
    $cscArgs = @('/nologo', '/target:winexe', '/optimize+', "/lib:$wpf",
                 '/r:PresentationFramework.dll', '/r:PresentationCore.dll', '/r:WindowsBase.dll', '/r:System.Xaml.dll',
                 '/r:System.Xml.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
    foreach ($x in 'Theme.xaml', 'MainWindow.xaml', 'Flyout.xaml') { $cscArgs += "/resource:$(Join-Path $srcDir $x),$x" }
    $cscArgs += "/out:$exe"
    $cscArgs += (Join-Path $srcDir 'WindowsUnlockedToolkit.cs')
    $buildOutput = & $csc @cscArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw 'Building WindowsUnlockedToolkit.exe failed (see errors above).'
    }
    Write-Info "built $exe"

    Write-Step 'Start at sign-in'
    if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }
    $hadRun = $null -ne (Get-ItemProperty -Path $runKey -Name 'WindowsUnlockedToolkit' -ErrorAction SilentlyContinue)
    if ($quiet -and $hadApp -and -not $hadRun) {
        Write-Info 'left off, as you set it in the app'
    } else {
        Set-ItemProperty -Path $runKey -Name 'WindowsUnlockedToolkit' -Value ('"' + $exe + '"')
        Write-Info 'added (turn it off any time in the app: Updates and about > Start with Windows)'
    }
    $menuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Windows Unlocked Toolkit.lnk'
    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut($menuLink)
    $lnk.TargetPath = $exe
    $lnk.Arguments = '--settings'
    $lnk.WorkingDirectory = $appDir
    $lnk.Description = 'Settings for Windows Unlocked Toolkit'
    $lnk.Save()
    Write-Info 'added to the Start menu'

    if ($NoStart) {
        Write-Step 'Not starting it now (-NoStart); it starts at your next sign-in.'
    } else {
        Write-Step 'Starting'
        if ($quiet) { Start-Process -FilePath $exe -WorkingDirectory $appDir }
        else { Start-Process -FilePath $exe -ArgumentList '--settings' -WorkingDirectory $appDir }
        Write-Info 'The toolkit is in the system tray (click the ^ arrow next to the clock if you do not see it).'
    }

    Write-Host ''
    Write-Host 'The toolkit tray app is installed.' -ForegroundColor Green
    Write-Host '  Click its tray icon for quick switches, or "Open settings" for everything else.'
    Write-Host '  Tip: drag the icon from the ^ overflow onto the taskbar to keep it in view.'
}
catch {
    Write-Host ''
    Write-Host 'Install failed:' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
