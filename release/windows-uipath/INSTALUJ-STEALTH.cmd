@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion

:: Wymaga uprawnien administratora (zmienne maszynowe)
net session >nul 2>&1
if errorlevel 1 (
    echo Uruchom ten skrypt jako Administrator.
    pause
    exit /b 1
)

set "SRC=%~dp0"
set "DEST=%ProgramData%\Enterprise\WorkflowHost"

echo.
echo ========================================
echo   STEALTH — instalacja hosta workflow
echo ========================================
echo.
echo Cel: %DEST%
echo.

if not exist "%SRC%lib\Ops.Runtime.Seed.dll" (
    echo BLAD: Brak lib\Ops.Runtime.Seed.dll — zbuduj release\windows-uipath najpierw.
    pause
    exit /b 1
)

if not exist "%SRC%host\Enterprise.WorkflowHost.dll" (
    echo BLAD: Brak host\Enterprise.WorkflowHost.dll — zbuduj release\windows-uipath najpierw.
    pause
    exit /b 1
)

set "TOKEN="
set /p TOKEN=Podaj FLOW_RUNTIME_TOKEN (np. RT-2026-Qiagen-69): 
if "!TOKEN!"=="" (
    echo Anulowano — token jest wymagany.
    pause
    exit /b 1
)

echo Tworzenie folderow...
mkdir "%DEST%" 2>nul
mkdir "%DEST%\catalog" 2>nul

echo Kopiowanie plikow...
copy /Y "%SRC%host\Enterprise.WorkflowHost.dll" "%DEST%\" >nul
copy /Y "%SRC%lib\Ops.Runtime.Seed.dll"           "%DEST%\" >nul
if exist "%SRC%catalog\seed.jwt" copy /Y "%SRC%catalog\seed.jwt" "%DEST%\catalog\" >nul

set "HOOK_DLL=%DEST%\Enterprise.WorkflowHost.dll"

echo Ustawianie zmiennych maszynowych...
setx DOTNET_STARTUP_HOOKS "%HOOK_DLL%" /M >nul
setx FLOW_RUNTIME_TOKEN "!TOKEN!" /M >nul
setx OPS_SEED_QUIET "1" /M >nul
setx OPS_SEED_KILL_ON_DENY "1" /M >nul
setx OPS_SEED_CATALOG_FILE "%DEST%\catalog\seed.jwt" /M >nul

echo.
echo ========================================
echo   GOTOWE (STEALTH)
echo ========================================
echo.
echo Hook:   %HOOK_DLL%
echo Token:  !TOKEN!
echo.
echo Projekt UiPath NIE wymaga zadnej paczki ani aktywnosci.
echo Zrestartuj UiPath Robot / Assistant.
echo.
copy /Y "%SRC%INSTRUKCJA-STEALTH.txt" "%DEST%\" >nul
start "" notepad "%DEST%\INSTRUKCJA-STEALTH.txt"
pause
