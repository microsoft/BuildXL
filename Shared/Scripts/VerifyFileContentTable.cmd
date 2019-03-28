@echo off
REM This script composes Init.cmd and VerifyFileContentTable.exe (on the cache directory)

setlocal

call %~dp0\Init.cmd
if %ERRORLEVEL% NEQ 0 (
	echo %~dp0\Init.cmd FAILED: %ERRORLEVEL%
	endlocal && exit /b %ERRORLEVEL%
)

set FctVerifier=%ENLISTMENTROOT%\Out\Bin\tests\debug\VerifyFileContentTable.exe 
if NOT EXIST %FctVerifier% (
	echo.
	echo Could not find: %FctVerifier%
	endlocal && exit /b 1
)

%FctVerifier% ^"%BUILDXL_CACHE_DIRECTORY%\EngineCache\FileContentTable^"
if %ERRORLEVEL% NEQ 0 (
	endlocal && exit /b %ERRORLEVEL%
)

endlocal