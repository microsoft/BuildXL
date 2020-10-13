@echo off
setlocal

SET FEATURE_NAME=%1

if NOT DEFINED FEATURE_NAME (
	echo.
	echo Must specify feature name as first argument
	echo.
	goto :error
)

SET ACCOUNT_NAME=%2

if NOT DEFINED ACCOUNT_NAME (
	SET ACCOUNT_NAME=mseng
)

SET USE_FEATURE_NAME=%3

if DEFINED USE_FEATURE_NAME (
	SET DROP_NAME=%FEATURE_NAME%
)

if NOT DEFINED DROP_CONTENT_DIR (
    set DROP_CONTENT_DIR="%~dp0\out\bin"
)

if NOT DEFINED DROP_NAME (
	SET DROP_NAME=%USERNAME%/%FEATURE_NAME%
)

echo Creating drop %DROP_NAME%
echo https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/%DROP_NAME%

call %~dp0\drop.cmd create -a -s https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%"

call %~dp0\drop.cmd publish -a -s https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%" -d %DROP_CONTENT_DIR%

call %~dp0\drop.cmd finalize -a -s https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%"

echo Created drop %DROP_NAME%
echo https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/%DROP_NAME%

:error
if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)

endlocal && exit /b 0