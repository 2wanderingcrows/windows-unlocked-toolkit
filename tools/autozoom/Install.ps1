# AutoZoom - installer
#
# Builds AutoZoom.exe from AutoZoom.cs using the C# compiler that is built into
# Windows, puts it in %LOCALAPPDATA%\AutoZoom, sets it to start when you sign in,
# and starts it. Nothing is downloaded and no admin rights are needed.
# Safe to run again: your settings.ini is kept.

$ErrorActionPreference = 'Stop'
$AppName  = 'AutoZoom'
$here     = Split-Path -Parent $MyInvocation.MyCommand.Path
$source   = Join-Path $here 'AutoZoom.cs'
$target   = Join-Path $env:LOCALAPPDATA $AppName
$exe      = Join-Path $target "$AppName.exe"
$settings = Join-Path $target 'settings.ini'
$log      = Join-Path $target 'log.txt'
$runKey   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }

try {
    Say ''
    Say 'AutoZoom - install' 'Cyan'
    Say ''

    if (-not (Test-Path -LiteralPath $source)) {
        throw "AutoZoom.cs wasn't found next to this installer. Extract the whole zip to a folder first, then run Install.cmd from that folder."
    }

    Say '1/5  Stopping any copy that is already running...'
    Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    New-Item -ItemType Directory -Force -Path $target | Out-Null

    # Claude AutoZoom was the first, Claude-only version: a PowerShell script started by a
    # scheduled task. Retire it so the two don't both press zoom keys, and keep its zoom levels.
    Say '2/5  Retiring the older Claude AutoZoom, if it is here...'
    $oldMax = $null; $oldNormal = $null
    $oldTask = Get-ScheduledTask -TaskName 'ClaudeAutoZoom' -ErrorAction SilentlyContinue
    if ($oldTask) {
        $oldDir = $oldTask.Actions | Select-Object -First 1 -ExpandProperty WorkingDirectory -ErrorAction SilentlyContinue
        if ($oldDir -and (Test-Path -LiteralPath (Join-Path $oldDir 'settings.json'))) {
            try {
                $old = Get-Content -LiteralPath (Join-Path $oldDir 'settings.json') -Raw | ConvertFrom-Json
                if ($null -ne $old.MaximizedSteps) { $oldMax = [int]$old.MaximizedSteps }
                if ($null -ne $old.NormalSteps) { $oldNormal = [int]$old.NormalSteps }
            } catch { }
        }
        Stop-ScheduledTask -TaskName 'ClaudeAutoZoom' -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName 'ClaudeAutoZoom' -Confirm:$false -ErrorAction SilentlyContinue
        Say '     Removed the ClaudeAutoZoom scheduled task.'
    }
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*ClaudeAutoZoom.ps1*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    $oldLnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'ClaudeAutoZoom.lnk'
    if (Test-Path -LiteralPath $oldLnk) { Remove-Item -LiteralPath $oldLnk -Force }

    if (-not (Test-Path -LiteralPath $settings)) {
        $max = 4; $normal = 1
        if ($null -ne $oldMax) { $max = $oldMax }
        if ($null -ne $oldNormal) { $normal = $oldNormal }
        $lines = @(
            '# AutoZoom settings. The toolkit tray app edits this file; you can too.',
            '# One line per app:  App.<process name>=<steps when maximized>,<steps when not maximized>',
            '# A step is one press of Ctrl+= (positive) or Ctrl+- (negative), counted from the app''s 100%.',
            '# Saved changes apply within a second.',
            "App.claude=$max,$normal",
            'PollMs=10',
            'KeyGapMs=0',
            'Log=true'
        )
        [System.IO.File]::WriteAllLines($settings, $lines)
        Say "     Created settings.ini with Claude as a starting example ($max steps maximized, $normal not)."
    } else {
        Say '     Keeping your settings.ini.'
    }

    Say '3/5  Building it with the compiler built into Windows...'
    $code = [System.IO.File]::ReadAllText($source)
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
    Add-Type -TypeDefinition $code -Language CSharp `
             -OutputAssembly $exe -OutputType WindowsApplication -IgnoreWarnings
    if (-not (Test-Path -LiteralPath $exe)) { throw 'The build finished but AutoZoom.exe was not created.' }

    Say '4/5  Setting it to start automatically when you sign in...'
    # If you turned AutoZoom off in the toolkit, leave it off.
    $parked = Join-Path $env:LOCALAPPDATA 'WindowsUnlockedToolkit\turned-off\AutoZoom.run.txt'
    if (Test-Path -LiteralPath $parked) {
        [System.IO.File]::WriteAllText($parked, '"' + $exe + '"')
        Say '     It is turned off in the toolkit, so it stays off. Turn it on from the toolkit when you want it.'
    } else {
        Set-ItemProperty -Path $runKey -Name $AppName -Value ('"' + $exe + '"')
    }

    Say '5/5  Starting it...'
    if (-not (Test-Path -LiteralPath $parked)) {
        Start-Process -FilePath $exe
        Start-Sleep -Seconds 2
    }

    Say ''
    if ((Test-Path -LiteralPath $parked) -or (Get-Process -Name $AppName -ErrorAction SilentlyContinue)) {
        Say 'Done! AutoZoom is installed.' 'Green'
        Say ''
        Say 'Try it: maximize Claude, then restore it. Its zoom follows the window.'
        Say 'Add more apps and pick their zoom levels in the toolkit tray app (AutoZoom page),'
        Say "or edit $settings"
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
