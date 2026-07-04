@echo off
setlocal
cd /d "%~dp0\.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-windows.ps1" %*
if errorlevel 1 (
    echo.
    echo Build nie powiodl sie. Sprawdz komunikaty powyzej.
    pause
    exit /b 1
)
pause
