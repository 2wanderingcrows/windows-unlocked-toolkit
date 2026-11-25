<#
.SYNOPSIS
    Builds dist\WindowsUnlockedToolkitSetup.exe, the setup app with buttons instead of command-line prompts.

.DESCRIPTION
    Run on Windows after "python scripts/build.py" (GitHub Actions does both). It compiles setup\Setup.cs
    with the C# compiler that ships with Windows (.NET Framework 4.x) and embeds:
      setup\Setup.xaml, tools\toolkit\Theme.xaml (the tray app's styles),
      dist\install.ps1 (every tool, the same file as the website's) and dist\manifest.ini.
    It compiles twice: once to draw the icon (--make-icon), then with that icon.

.PARAMETER SelfTest
    Folder for the self-test: renders every screen to PNG and fails on binding errors.
#>
[CmdletBinding()]
param(
    [string]$SelfTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dist = Join-Path $root 'dist'
$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc  = Join-Path $fw 'csc.exe'
$wpf  = Join-Path $fw 'WPF'
$work = Join-Path ([IO.Path]::GetTempPath()) 'wut-setup-build'
$exe  = Join-Path $dist 'WindowsUnlockedToolkitSetup.exe'
$ico  = Join-Path $work 'setup.ico'

foreach ($f in (Join-Path $dist 'install.ps1'), (Join-Path $dist 'manifest.ini')) {
    if (-not (Test-Path -LiteralPath $f)) { throw "Missing $f. Run python scripts/build.py first." }
}
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-Csc([string]$out, [string]$icon) {
    $cscArgs = @('/nologo', '/target:winexe', '/optimize+', "/lib:$wpf",
                 '/r:PresentationFramework.dll', '/r:PresentationCore.dll', '/r:WindowsBase.dll', '/r:System.Xaml.dll',
                 '/r:System.Xml.dll', '/r:System.Drawing.dll',
                 "/resource:$(Join-Path $root 'setup\Setup.xaml'),Setup.xaml",
                 "/resource:$(Join-Path $root 'tools\toolkit\Theme.xaml'),Theme.xaml",
                 "/resource:$(Join-Path $dist 'install.ps1'),install.ps1",
                 "/resource:$(Join-Path $dist 'manifest.ini'),manifest.ini")
    if ($icon) { $cscArgs += "/win32icon:$icon" }
    $cscArgs += "/out:$out"
    $cscArgs += (Join-Path $root 'setup\Setup.cs')
    $log = & $csc @cscArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $log | ForEach-Object { Write-Host "$_" }
        throw 'Compiling the setup app failed.'
    }
}

$plain = Join-Path $work 'setup-noicon.exe'
Invoke-Csc $plain $null
$p = Start-Process -FilePath $plain -ArgumentList '--make-icon', "`"$ico`"" -Wait -PassThru
if ($p.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ico)) { throw 'Drawing the icon failed.' }
Invoke-Csc $exe $ico
Write-Host "Built $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"

if ($SelfTest) {
    $p = Start-Process -FilePath $exe -ArgumentList '--selftest', "`"$SelfTest`"" -Wait -PassThru
    Get-Content (Join-Path $SelfTest 'selftest.txt') -ErrorAction SilentlyContinue
    if ($p.ExitCode -ne 0) { throw "The setup app's self-test failed (exit $($p.ExitCode))." }
}
