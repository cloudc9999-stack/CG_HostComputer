@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-vci-bridge.ps1"
exit /b %ERRORLEVEL%
