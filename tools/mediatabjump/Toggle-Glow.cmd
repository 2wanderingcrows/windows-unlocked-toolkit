@echo off
title Media Tab Jump - Hover glow on/off
set "FLAG=%LOCALAPPDATA%\MediaTabJump\glow-off.txt"
if not exist "%LOCALAPPDATA%\MediaTabJump" (
    echo Media Tab Jump isn't installed yet. Run Install.cmd first.
    goto done
)
if exist "%FLAG%" (
    del "%FLAG%"
    echo The hover glow is now ON.
) else (
    type nul > "%FLAG%"
    echo The hover glow is now OFF. Run this again to turn it back on.
)
:done
echo.
pause
