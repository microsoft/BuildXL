@echo off
setlocal

if NOT DEFINED ENLISTMENTROOT (
    set ENLISTMENTROOT=%~dp0\..\..
)

if "%_BUILDXL_INIT_DONE%" NEQ "1" (
    call %ENLISTMENTROOT%\Shared\Scripts\Init.cmd
)

if NOT DEFINED BUILDXL_BIN_DIRECTORY (
    set BUILDXL_BIN_DIRECTORY=%ENLISTMENTROOT%\Out\Bin\debug\win-x64
) 

if NOT DEFINED BUILDXL_TEST_BIN_DIRECTORY (
    set BUILDXL_TEST_BIN_DIRECTORY=%ENLISTMENTROOT%\Out\Bin\debug\tools\DistributedBuildRunner\win-x64
)

set TEST_SOLUTION_ROOT=%~dp0
if %TEST_SOLUTION_ROOT:~-1%==\ set TEST_SOLUTION_ROOT=%TEST_SOLUTION_ROOT:~0,-1%
if EXIST %TEST_SOLUTION_ROOT%\Out rd /s/q %TEST_SOLUTION_ROOT%\Out

REM Set environment variables consumed by distributed build runner
set BUILDXL_EXE_PATH=%BUILDXL_BIN_DIRECTORY%\bxl.exe
set DOMINO_EXE_PATH=%BUILDXL_BIN_DIRECTORY%\bxl.exe

set SMDB.CACHE_CONFIG_TEMPLATE_PATH=%ENLISTMENTROOT%\Shared\CacheCore.SingleMachineDistributed.json
set SMDB.CACHE_CONFIG_OUTPUT_PATH=%TEST_SOLUTION_ROOT%\Out\M{machineNumber}\CacheCore.SingleMachineDistributed.json
set SMDB.CACHE_TEMPLATE_PATH=%TEST_SOLUTION_ROOT%\Out\SharedCache
set BuildXLExportFileDetails=1
set BUILDXL_MASTER_ARGS=/maxProc:2 %BUILDXL_MASTER_ARGS%
set BUILDXL_WORKER_ARGS=/maxProc:6 %BUILDXL_WORKER_ARGS%

REM The following variables are for ChangeAffectedInputTest
REM Due to root maps, all paths in the graph will have the mapped root. Because the content of SOURCECHANGE_FILE below will be an absolute path
REM and the path is obtained prior to BuildXL invocation, where no substs or root maps have been applied, BuildXL invocation needs directory translation
REM to translate the absolute path to the translated one. Otherwise, the source change is not propagated properly.
REM One can avoid using directory translation by specifying the SOURCE_FILE below as a relative path Src\ChangeAffectedInputTest\inputfile.txt.
set SOURCE_FILE=%TEST_SOLUTION_ROOT%\Src\ChangeAffectedInputTest\inputfile.txt
set SOURCECHANGE_FILE=%TEST_SOLUTION_ROOT%\Src\ChangeAffectedInputTest\sourceChange.txt
echo %SOURCE_FILE%>%SOURCECHANGE_FILE%

@REM disabled warnings:
@REM   - DX2841: Virus scanning software is enabled for.
@REM   - DX2200: Failed to clean temp directory. Reason: unable to enumerate the directory or a descendant directory to verify that it has been emptied.
set BUILDXL_COMMON_ARGS=/server- /exp:NewCache /exp:TwoPhaseFingerprinting /exp:DontBringOutputsToMaster /nowarn:2841 /nowarn:2200 /p:OfficeDropTestEnableDrop=True /f:~(tag='exclude-drop-file'ortag='dropd-finalize') "/storageRoot:{objectRoot}:\ " "/config:{sourceRoot}:\config.dsc" "/cacheConfigFilePath:%SMDB.CACHE_CONFIG_OUTPUT_PATH%" "/rootMap:{sourceRoot}=%TEST_SOLUTION_ROOT%" "/rootMap:{objectRoot}=%TEST_SOLUTION_ROOT%\Out\M{machineNumber}" "/cacheDirectory:{objectRoot}:\Cache"  /logObservedFileAccesses /substTarget:{objectRoot}:\ /substSource:%TEST_SOLUTION_ROOT%\Out\M{machineNumber}\ /logsDirectory:{objectRoot}:\Logs /disableProcessRetryOnResourceExhaustion+ "/translateDirectory:%TEST_SOLUTION_ROOT%<{sourceRoot}:\\" /inputChanges:%TEST_SOLUTION_ROOT%\Src\ChangeAffectedInputTest\sourceChange.txt

if NOT DEFINED DISABLE_DBD_TESTRUN (
    %BUILDXL_TEST_BIN_DIRECTORY%\DistributedBuildRunner.exe 2 %*
)

del %SOURCECHANGE_FILE%

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Distributed Build Failed.
    echo.
    endlocal && exit /b 1
)

REM if TF_ROLLING_DROPNAME was not set --> drop was not uploaded so we are done
if NOT DEFINED TF_ROLLING_DROPNAME (
    endlocal && exit /b 0
)

REM if TF_ROLLING_DROPNAME was set --> check if the drop was finalized
set BUILDXL_STATS_FILE=%TEST_SOLUTION_ROOT%\Out\M00\Logs\BuildXL.stats
findstr DropDaemon.FinalizeTime %BUILDXL_STATS_FILE% >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Drop not finalized.
    echo.
    endlocal && exit /b 1
)

endlocal && exit /b 0
