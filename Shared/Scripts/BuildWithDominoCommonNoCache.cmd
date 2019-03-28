@echo off

REM Runs BuildXL. The first argument (%1) must be a BuildXL config (.dc)
REM This script does not set externally visible variables.
REM It must be called with the variables as defined by Init.cmd

setlocal

if "%_BUILDXL_INIT_DONE%" NEQ "1" (
    echo Assert failed ^(BuildWithBxlCommon.cmd^): Init.cmd not called
    endlocal && exit /b 2
)

if NOT DEFINED BUILDXL_BIN_DIRECTORY (
    set BUILDXL_BIN_DIRECTORY=%ENLISTMENTROOT%\Out\Bin\Debug\net472
)

if NOT EXIST %BUILDXL_BIN_DIRECTORY%\bxl.exe (
    echo ERROR: Could not find %BUILDXL_BIN_DIRECTORY%\bxl.exe. Please run BuildWithMSBuild.cmd to ensure BuildXL can run.
    endlocal && exit /b 1
)

if NOT EXIST %BUILDXL_BIN_DIRECTORY%\x86\DetoursServices.dll (
    echo ERROR: Could not find x86 version of the BuildXL DetoursService. Please run BuildWithMSBuild.cmd to ensure BuildXL can run.
    endlocal && exit /b 1
)

echo Starting BuildXL

set BUILDXL_COMMAND_LINE=%BUILDXL_BIN_DIRECTORY%\bxl.exe /nowarn:0042 /exp:UseHardLinks+ /logStats %*
%BUILDXL_COMMAND_LINE%
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo %BUILDXL_COMMAND_LINE%
    echo ERROR:BuildXL FAILED: %ERRORLEVEL%
    endlocal && exit /b %ERRORLEVEL%
)
endlocal
