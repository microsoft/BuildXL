@echo off
setlocal

SET FEATURE_NAME=%1

if NOT DEFINED FEATURE_NAME (
	echo.
	echo Must specify feature name as first argument
	echo.
	goto :error
)

if NOT DEFINED DROP_CONTENT_DIR (
    set DROP_CONTENT_DIR="%~dp0\out\bin"
)

SET DROP_NAME=%USERNAME%/%FEATURE_NAME%

echo Creating drop %DROP_NAME%
echo https://cloudbuild.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/%DROP_NAME%

call %~dp0\drop.cmd create -a -s https://cloudbuild.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%"

call %~dp0\drop.cmd publish -a -s https://cloudbuild.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%" -d %DROP_CONTENT_DIR%

call %~dp0\drop.cmd finalize -a -s https://cloudbuild.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%"

echo Created drop %DROP_NAME%

:error
if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)

endlocal && exit /b 0