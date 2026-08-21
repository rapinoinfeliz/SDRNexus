@echo off
setlocal
title SDRNexus Installer
echo Installing SDRNexus...
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "SDRNEXUS_INSTALL_EXIT=%ERRORLEVEL%"
echo.
if not "%SDRNEXUS_INSTALL_EXIT%"=="0" (
    echo Installation failed. Review the message above and try again.
) else (
    echo Installation completed successfully.
)
echo.
pause
exit /b %SDRNEXUS_INSTALL_EXIT%
