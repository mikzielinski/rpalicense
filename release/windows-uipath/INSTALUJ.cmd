@echo off
chcp 65001 >nul
setlocal EnableExtensions

set "SRC=%~dp0"
set "DEST=%USERPROFILE%\OpsRuntime"
set "DESKTOP=%USERPROFILE%\Desktop\OpsRuntime"

echo.
echo ========================================
echo   UiPath.System.RoboticSecurity - instalacja UiPath
echo ========================================
echo.
echo Zrodlo:  %SRC%
echo Cel:     %DEST%
echo.

if not exist "%SRC%lib\UiPath.System.RoboticSecurity.dll" (
    echo BLAD: Brak pliku lib\UiPath.System.RoboticSecurity.dll w folderze skryptu.
    echo Upewnij sie, ze uruchamiasz INSTALUJ.cmd z folderu release\windows-uipath
    pause
    exit /b 1
)

echo Tworzenie folderow...
mkdir "%DEST%\lib" 2>nul
mkdir "%DEST%\nuget" 2>nul
mkdir "%DEST%\catalog" 2>nul
mkdir "%DESKTOP%\lib" 2>nul
mkdir "%DESKTOP%\nuget" 2>nul
mkdir "%DESKTOP%\catalog" 2>nul

echo Kopiowanie plikow...
copy /Y "%SRC%lib\UiPath.System.RoboticSecurity.dll"           "%DEST%\lib\" >nul
copy /Y "%SRC%nuget\UiPath.System.RoboticSecurity.1.0.2.nupkg" "%DEST%\nuget\" >nul
copy /Y "%SRC%catalog\seed.jwt"                   "%DEST%\catalog\" >nul
copy /Y "%SRC%test-config.json"                   "%DEST%\" >nul
copy /Y "%SRC%offline-env.txt"                    "%DEST%\" >nul
copy /Y "%SRC%INSTRUKCJA-UIPATH.txt"              "%DEST%\" >nul

copy /Y "%SRC%lib\UiPath.System.RoboticSecurity.dll"           "%DESKTOP%\lib\" >nul
copy /Y "%SRC%nuget\UiPath.System.RoboticSecurity.1.0.2.nupkg" "%DESKTOP%\nuget\" >nul
copy /Y "%SRC%catalog\seed.jwt"                   "%DESKTOP%\catalog\" >nul
copy /Y "%SRC%INSTRUKCJA-UIPATH.txt"              "%DESKTOP%\" >nul

echo.
echo ========================================
echo   GOTOWE!
echo ========================================
echo.
echo Folder glowny:
echo   %DEST%
echo.
echo Kopia na Pulpicie:
echo   %DESKTOP%
echo.
echo UiPath - dodaj feed NuGet:
echo   %DEST%\nuget
echo.
echo Token testowy: RT-TEST-REPORT-001
echo.

start "" explorer "%DEST%"
start "" notepad "%DEST%\INSTRUKCJA-UIPATH.txt"

pause
