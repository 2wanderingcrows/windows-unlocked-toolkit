@echo off
title Media Tab Jump - Install
rem "Bypass" applies only to this one run of PowerShell; it doesn't change any Windows settings.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
echo.
pause
