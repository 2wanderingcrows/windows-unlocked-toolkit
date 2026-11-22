# Media Tab Jump - uninstaller
# Stops the helper, removes it from startup, and deletes its folder.

$AppName = 'MediaTabJump'
$target  = Join-Path $env:LOCALAPPDATA $AppName
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Write-Host ''
Write-Host 'Media Tab Jump - uninstall' -ForegroundColor Cyan
Write-Host ''

Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path $runKey -Name $AppName -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $target) {
    Write-Host "Mostly removed, but this folder couldn't be deleted: $target" -ForegroundColor Yellow
    Write-Host 'You can delete it by hand.'
}
else {
    Write-Host 'Removed. Nothing is left behind.' -ForegroundColor Green
}
