@echo off
title Oasis Protocol Launcher
cd /d "%~dp0Builds"
if exist "Oasis Protocol.exe" (
    echo ========================================================
    echo   Launching Oasis Protocol...
    echo ========================================================
    start "" "Oasis Protocol.exe"
) else (
    echo [ERROR] Oasis Protocol.exe not found in Builds directory!
    pause
)
exit /b 0
