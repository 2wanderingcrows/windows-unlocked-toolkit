# AutoZoom - uninstaller
# Stops it, removes it from startup, and deletes its folder (settings included).
# Apps keep whatever zoom they are at; press Ctrl+0 in an app to put it back to 100%.

$AppName = 'AutoZoom'
$target  = Join-Path $env:LOCALAPPDATA $AppName
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$parked  = Join-Path $env:LOCALAPPDATA 'WindowsUnlockedToolkit\turned-off\AutoZoom.run.txt'

Write-Host ''
Write-Host 'AutoZoom - uninstall' -ForegroundColor Cyan
Write-Host ''

Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path $runKey -Name $AppName -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $parked -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $target) {
    Write-Host "Mostly removed, but this folder couldn't be deleted: $target" -ForegroundColor Yellow
    Write-Host 'You can delete it by hand.'
}
else {
    Write-Host 'Removed. Apps keep their current zoom; press Ctrl+0 in an app to reset it.' -ForegroundColor Green
}
