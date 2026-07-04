@echo off
cd /d "%~dp0"
if not exist "release\windows-uipath\INSTALUJ.cmd" (
    echo BLAD: Brak folderu release\windows-uipath
    echo Sciagnij najnowszy kod: git pull
    pause
    exit /b 1
)
call "release\windows-uipath\INSTALUJ.cmd"
