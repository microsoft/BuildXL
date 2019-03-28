@echo off
setlocal

if NOT DEFINED ENLISTMENTROOT (
    set ENLISTMENTROOT=%~dp0\..\..\
)

echo =========================================================
echo  Building BuildXL
echo =========================================================
rem call %ENLISTMENTROOT%\bxl.cmd -deploy dev /server- /f:output='output='Out\Bin\debug\net472\*'

set BUILDXL_BIN_DIRECTORY=%ENLISTMENTROOT%\Out\Bin\Debug\

echo =========================================================
echo  Running Distributed Test locally (single-machine build)
echo =========================================================
set TEST_ROOT=%NLISTMENTROOT%\Private\DistributedIntegrationTest
set DARGS=-all -use dev /c:%TEST_ROOT%\config.dsc /server- /enableLazyOutputs:Minimal /s:%TEST_ROOT%\Src\OfficeDropTest\OfficeDropTest.dsc

REM call BuildXL to build OfficeDropTest without uploading drop (populates local cache)
call %ENLISTMENTROOT%\bxl.cmd %DARGS% /p:OfficeDropTestEnableDrop=False

REM delete Objects folder (where all the build artifacts are) 
rd /q /s %TEST_ROOT%\Out\Objects

REM call BuildXL again, now with uploading drop.  
call %ENLISTMENTROOT%\bxl.cmd %DARGS% /p:OfficeDropTestEnableDrop=True /logsDirectory:%TEST_ROOT%\Out\Logs

REM Expect 100% process pip cache hit rate.
findstr /C:"Process pip cache hit rate: 100%" Private\DistributedIntegrationTest\Out\Logs\BuildXL.log >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo "ERROR: process pip cache hit rate not 100%"
    findstr /C:"Process pip cache hit rate" Private\DistributedIntegrationTest\Out\Logs\BuildXL.log
    exit /b 1
)

REM Expect to find calls to MaterializeFile.
findstr /C:"ApiTotalMaterializeFileCalls" %TEST_ROOT%\Out\Logs\BuildXL.stats >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo "ERROR: didn't find any calls to MaterializeFile"
    exit /b 1
)

REM Check that the number of calls to MaterializeFile is not 0.
findstr /C:"ApiTotalMaterializeFileCalls=0" %TEST_ROOT%\Out\Logs\BuildXL.stats >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo "ERROR: Number of calls to MaterializeFile is 0"
    exit /b 1
)