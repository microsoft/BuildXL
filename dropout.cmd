@echo off
setlocal EnableDelayedExpansion

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

SET DROP_CONTENT_DIR=%3

if NOT DEFINED DROP_CONTENT_DIR (
  set DROP_CONTENT_DIR="%~dp0\out\bin"
)

if %DROP_CONTENT_DIR% == ' ' (
  set DROP_CONTENT_DIR="%~dp0\out\bin"
)

SET USE_FEATURE_NAME=%4

if DEFINED USE_FEATURE_NAME (
  SET DROP_NAME=%FEATURE_NAME%
)

if NOT DEFINED DROP_NAME (
  SET DROP_NAME=%USERNAME%/%FEATURE_NAME%
)

SET EXPIRATION_DATE=%5
if NOT DEFINED EXPIRATION_DATE (
  for /f %%i in ('powershell -command "(Get-Date).AddDays(90).ToString('yyyy-MM-dd')"') do set EXPIRATION_DATE=%%i
  
  echo Drop expiration date is set to: !EXPIRATION_DATE!
)

echo Creating drop %DROP_NAME%
echo https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/%DROP_NAME%
echo.

call %~dp0\drop.cmd create -a -s https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%" -x %EXPIRATION_DATE%

call %~dp0\drop.cmd publish -a -s https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%" -d %DROP_CONTENT_DIR%

call %~dp0\drop.cmd finalize -a -s https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection  -n "%DROP_NAME%"

echo.
echo Created drop %DROP_NAME%
echo https://%ACCOUNT_NAME%.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/%DROP_NAME%

:error
if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)

endlocal && exit /b 0