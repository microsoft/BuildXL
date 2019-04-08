@REM Wrapper to run .ps1 from CMD environments

@REM Nasty Hack - If we find that we are not running in native CMD
@REM we need to restart the script with native CMD such that
@REM Powershell actually works correctly with all of its cmd-lets
@REM Most commonly seen when CMD is started from 32-bit process on 64-bit OS
@if exist "%WinDir%\SysNative\cmd.exe" goto :Run64

@if /I "%~1"=="-?" goto :Help
@if /I "%~1"=="/?" goto :Help
@if /I "%~1"=="-h" goto :Help
@if /I "%~1"=="/h" goto :Help
@if /I "%~1"=="/help" goto :Help

powershell.exe -ExecutionPolicy Bypass %~dpn0.ps1 %*
@exit /b %ERRORLEVEL%

:Run64
@"%WinDir%\SysNative\cmd.exe" /c "%~f0" %*
@exit /b %ERRORLEVEL%

:Help
@powershell.exe -ExecutionPolicy Bypass Get-Help %~dpn0.ps1 -Detailed