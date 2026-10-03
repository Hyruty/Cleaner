@echo off
setlocal

set "SOURCE=%~dp0"
set "TARGET=D:\Code\CDriveCleaner\Cleaner\CDriveCleaner"
set "ENGINE=%LOCALAPPDATA%\CDriveCleaner\engine"

echo Updating CDriveCleaner...
echo.

if not exist "%TARGET%\" (
    echo Target folder was not found:
    echo %TARGET%
    echo.
    echo Move this updater next to the existing CDriveCleaner folder,
    echo or edit the TARGET path in this file.
    pause
    exit /b 1
)

copy /Y "%SOURCE%CDriveCleaner.ps1" "%TARGET%\CDriveCleaner.ps1" >nul
if errorlevel 1 goto :failed

copy /Y "%SOURCE%strings.zh-CN.json" "%TARGET%\strings.zh-CN.json" >nul
if errorlevel 1 goto :failed

copy /Y "%SOURCE%Start-CDriveCleaner.cmd" "%TARGET%\Start-CDriveCleaner.cmd" >nul
if errorlevel 1 goto :failed

if exist "%ENGINE%\" (
    copy /Y "%SOURCE%CDriveCleaner.ps1" "%ENGINE%\CDriveCleaner.ps1" >nul
    if errorlevel 1 goto :failed

    copy /Y "%SOURCE%strings.zh-CN.json" "%ENGINE%\strings.zh-CN.json" >nul
    if errorlevel 1 goto :failed
)

echo.
echo Update completed successfully.
echo Main program:
echo %TARGET%
echo.
echo If automatic cleanup is enabled, you can keep using it normally.
pause
exit /b 0

:failed
echo.
echo Update failed. Close CDriveCleaner and try again.
pause
exit /b 1
