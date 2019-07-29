@echo off
setlocal

if NOT DEFINED ENLISTMENTROOT (
    set ENLISTMENTROOT=%~dp0\..\..\
)

echo =======================================================
echo  Building BuildXL
echo =======================================================
call %ENLISTMENTROOT%\bxl.cmd -deploydev /server- 
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: BuildXL build failed.
    echo.
    endlocal && exit /b 1
)

set BUILDXL_BIN_DIRECTORY=%ENLISTMENTROOT%\Out\Bin\Debug\win-x64

if NOT DEFINED TF_ROLLING_DROPNAME (
    set TF_ROLLING_DROPNAME=%USERNAME%-%random%
)

echo =======================================================
echo  Running DropServiceTest locally (single-machine build)
echo =======================================================
set TEST_ROOT=%~dp0
call %ENLISTMENTROOT%\bxl.cmd -all -use dev /c:%TEST_ROOT%\config.dsc /server- /p:OfficeDropTestEnableDrop=True /unsafe_MonitorFileAccesses- /disableProcessRetryOnResourceExhaustion+ %*