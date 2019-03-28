@echo off

echo ^>^>^> Checking whether cb.exe is up-to-date.
call %~dp0Shared\Scripts\Init.cmd >NUl 2>NUl
if %ERRORLEVEL% NEQ 0 (
	echo Could not retrieve cb.exe. FAILED: %ERRORLEVEL%
	exit /b %ERRORLEVEL%
)

powershell -NoProfile -ExecutionPolicy RemoteSigned %~dp0Shared\Scripts\buddy.ps1 %*
if %ERRORLEVEL% NEQ 0 (
    exit /b %ERRORLEVEL%
)

exit /b 0