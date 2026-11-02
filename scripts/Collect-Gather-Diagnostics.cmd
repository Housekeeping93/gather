@echo off
setlocal
if "%~1"=="" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Collect-Gather-Diagnostics.ps1"
) else (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Collect-Gather-Diagnostics.ps1" -AppPath "%~1"
)
echo.
pause
