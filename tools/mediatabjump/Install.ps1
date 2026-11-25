# Media Tab Jump - installer
#
# Builds the helper from MediaTabJump.cs using the C# compiler that is built into
# Windows, puts it in %LOCALAPPDATA%\MediaTabJump, sets it to start when you sign
# in, and starts it. Nothing is downloaded and no admin rights are needed.

$ErrorActionPreference = 'Stop'
$AppName = 'MediaTabJump'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$source  = Join-Path $here 'MediaTabJump.cs'
$target  = Join-Path $env:LOCALAPPDATA $AppName
$exe     = Join-Path $target "$AppName.exe"
$log     = Join-Path $target 'log.txt'
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

try {
    Say ''
    Say 'Media Tab Jump - install' 'Cyan'
    Say ''

    if (-not (Test-Path -LiteralPath $source)) {
        throw "MediaTabJump.cs wasn't found next to this installer. Extract the whole zip to a folder first, then run Install.cmd from that folder."
    }

    Say '1/4  Stopping any copy that is already running...'
    Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    # Start each version with a fresh log (older versions could record more than they needed to).
    Remove-Item -LiteralPath (Join-Path $target 'log.txt'), (Join-Path $target 'log.old.txt') -Force -ErrorAction SilentlyContinue

    Say '2/4  Building it with the compiler built into Windows...'
    $code = [System.IO.File]::ReadAllText($source)
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    Add-Type -TypeDefinition $code -Language CSharp `
             -OutputAssembly $exe -OutputType WindowsApplication -IgnoreWarnings
    if (-not (Test-Path -LiteralPath $exe)) { throw 'The build finished but MediaTabJump.exe was not created.' }

    Say '3/4  Setting it to start automatically when you sign in...'
    # If you turned Media Tab Jump off in the toolkit tray app, leave it off (updates included).
    $parked = Join-Path $env:LOCALAPPDATA 'WindowsUnlockedToolkit\turned-off\MediaTabJump.run.txt'
    if (Test-Path -LiteralPath $parked) {
        [System.IO.File]::WriteAllText($parked, '"' + $exe + '"')
        Say '     It is turned off in the toolkit, so it stays off. Turn it on from the toolkit when you want it.'
    } else {
        Set-ItemProperty -Path $runKey -Name $AppName -Value ('"' + $exe + '"')
    }

    Say '4/4  Starting it...'
    if (-not (Test-Path -LiteralPath $parked)) {
        Start-Process -FilePath $exe
        Start-Sleep -Seconds 2
    }

    Say ''
    if ((Test-Path -LiteralPath $parked) -or (Get-Process -Name $AppName -ErrorAction SilentlyContinue)) {
        Say 'Done! Media Tab Jump is running in the background.' 'Green'
        Say ''
        Say 'Try it: play something in Edge, click the volume icon on the taskbar,'
        Say 'then click the media card (the title or picture, not the play/pause buttons).'
        Say ''
        Say "Log file (handy if something doesn't work): $log"
        Say 'To remove it, run Uninstall.cmd.'
    }
    else {
        Say "It was built, but it didn't stay running." 'Yellow'
        Say 'If Windows Security showed a "blocked" message, Smart App Control or antivirus stopped it.'
        Say "The log may explain more: $log"
    }
}
catch {
    Say ''
    Say 'Install failed:' 'Red'
    Say $_.Exception.Message 'Red'
    exit 1
}
