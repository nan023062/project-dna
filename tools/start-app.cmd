@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-app.ps1"
set EXIT_CODE=%ERRORLEVEL%
if not "%EXIT_CODE%"=="0" (
  echo.
  echo App startup failed. Press any key to close...
  pause >nul
)
exit /b %EXIT_CODE%
