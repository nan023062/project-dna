@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-server.ps1"
echo.
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Server exited with code %ERRORLEVEL%
)
pause
