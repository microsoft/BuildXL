@echo off

echo Preparing to build.

call %~dp0\Init.cmd %*
if %ERRORLEVEL% NEQ 0 (
	echo %~dp0\Init.cmd FAILED: %ERRORLEVEL%
	exit /b %ERRORLEVEL%
)

REM We have to be a little tricky to get various characters to pass through our wrapper scripts.
REM Here we translate some characters which are later switched back in Bxl.ps1
set original=%*
set replaced1=%original:'=#singlequote#%
set replaced2=%replaced1:)=#closeparens#%
set replaced3=%replaced2:(=#openparens#%

REM Run the BuildXL self-host wrapper.
powershell -NoProfile -ExecutionPolicy RemoteSigned %~dp0\Bxl.ps1 %replaced3%
if %ERRORLEVEL% NEQ 0 (
    exit /b %ERRORLEVEL%
)

exit /b 0