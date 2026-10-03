@echo off
setlocal
set "SCRIPT=%~dp0CDriveCleaner.ps1"
set "POWERSHELL=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"

if not exist "%SCRIPT%" (
    echo CDriveCleaner.ps1 was not found next to this launcher.
    pause
    exit /b 1
)

start "" "%POWERSHELL%" -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%SCRIPT%"
exit /b 0
