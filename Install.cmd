@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install.ps1" %*
if errorlevel 1 (echo Installation failed. Please read the error above. & pause & exit /b 1)
echo Installation complete.
pause
