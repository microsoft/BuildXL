@echo off

powershell -NoProfile -ExecutionPolicy RemoteSigned %~dp0Shared\Scripts\buddy.ps1 %*
if %ERRORLEVEL% NEQ 0 (
    exit /b %ERRORLEVEL%
)

exit /b 0
