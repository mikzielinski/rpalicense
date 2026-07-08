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

if not exist "%SRC%lib\UiPath.System.RoboticSecurity.dll" goto :err_dll

set "NUPKG_SRC="
set "NUPKG_NAME="
for %%F in ("%SRC%nuget\UiPath.System.RoboticSecurity.*.nupkg") do set "NUPKG_SRC=%%~fF" & set "NUPKG_NAME=%%~nxF"
if not defined NUPKG_SRC goto :err_nupkg

echo Tworzenie folderow...
mkdir "%DEST%\lib" 2>nul
mkdir "%DEST%\nuget" 2>nul
mkdir "%DEST%\catalog" 2>nul
mkdir "%DESKTOP%\lib" 2>nul
mkdir "%DESKTOP%\nuget" 2>nul
mkdir "%DESKTOP%\catalog" 2>nul

echo Kopiowanie plikow...
copy /Y "%SRC%lib\UiPath.System.RoboticSecurity.dll" "%DEST%\lib\" >nul
copy /Y "%NUPKG_SRC%" "%DEST%\nuget\" >nul
copy /Y "%SRC%catalog\seed.jwt" "%DEST%\catalog\" >nul
if exist "%SRC%test-config.json" copy /Y "%SRC%test-config.json" "%DEST%\" >nul
if exist "%SRC%offline-env.txt" copy /Y "%SRC%offline-env.txt" "%DEST%\" >nul
if exist "%SRC%INSTRUKCJA-UIPATH.txt" copy /Y "%SRC%INSTRUKCJA-UIPATH.txt" "%DEST%\" >nul

copy /Y "%SRC%lib\UiPath.System.RoboticSecurity.dll" "%DESKTOP%\lib\" >nul
copy /Y "%NUPKG_SRC%" "%DESKTOP%\nuget\" >nul
copy /Y "%SRC%catalog\seed.jwt" "%DESKTOP%\catalog\" >nul
if exist "%SRC%INSTRUKCJA-UIPATH.txt" copy /Y "%SRC%INSTRUKCJA-UIPATH.txt" "%DESKTOP%\" >nul

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
echo Pakiet NuGet: %NUPKG_NAME%
echo.
echo UiPath - dodaj feed NuGet:
echo   %DEST%\nuget
echo.
echo Token testowy: RT-TEST-REPORT-001
echo.

start "" explorer "%DEST%"
if exist "%DEST%\INSTRUKCJA-UIPATH.txt" start "" notepad "%DEST%\INSTRUKCJA-UIPATH.txt"

pause
exit /b 0

:err_dll
echo.
echo BLAD: Brak pliku lib\UiPath.System.RoboticSecurity.dll w folderze skryptu.
echo Upewnij sie, ze uruchamiasz INSTALUJ.cmd z folderu release\windows-uipath
pause
exit /b 1

:err_nupkg
echo.
echo BLAD: Brak pliku nuget\UiPath.System.RoboticSecurity.*.nupkg w folderze skryptu.
echo Pobierz najnowszy kod z GitHub (main) i uruchom ponownie.
pause
exit /b 1
