REM @echo off
setlocal

if "%_BUILDXL_INIT_DONE%" NEQ "1" (
	call %~dp0\Init.cmd
)

if NOT DEFINED BUILDXL_BIN_DIRECTORY (
    set BUILDXL_BIN_DIRECTORY=%ENLISTMENTROOT%\Out\Bin\debug\net472
    set BUILDXL_TEST_BIN_DIRECTORY=%ENLISTMENTROOT%\Out\Bin\tests\debug
) else (
    set BUILDXL_TEST_BIN_DIRECTORY=%BUILDXL_BIN_DIRECTORY%\..\..\tests\debug
)

if NOT DEFINED DBD_TESTGEN_COUNT (
	set DBD_TESTGEN_COUNT=100
)

if NOT DEFINED TEST_COMMITID (
	set TEST_COMMITID=47e77cd946c0d37a09cad752dee603db84ce2d92
)

set TEST_SOLUTION_ROOT=%ENLISTMENTROOT%\Out\Tests\SMDB

if DEFINED CLEAN_DBD_TESTGEN_OUTPUTONLY (

REM Clean the output directory

rmdir /S /Q %TEST_SOLUTION_ROOT%\Out


REM Ensure source is not cleaned

set DISABLE_DBD_TESTGEN=1

)

if NOT DEFINED DISABLE_DBD_TESTGEN (

REM Clean directory

rmdir /S /Q %TEST_SOLUTION_ROOT%

REM Generate test solution

call "%ProgramFiles%\Git\cmd\git" clone https://mseng.visualstudio.com/Domino/_git/Domino.DistributedBuildTest %TEST_SOLUTION_ROOT%
if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)

pushd %TEST_SOLUTION_ROOT%
call "%ProgramFiles%\Git\cmd\git" reset %TEST_COMMITID% --hard
if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)
popd


call %TEST_SOLUTION_ROOT%\TestSolution\PrepSdk.cmd %ENLISTMENTROOT%
if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)

)

REM Set environment variables consumed by distributed build runner
set BUILDXL_EXE_PATH=%BUILDXL_BIN_DIRECTORY%\bxl.exe

set SMDB.CACHE_CONFIG_TEMPLATE_PATH=%ENLISTMENTROOT%\Shared\CacheCore.SingleMachineDistributed.json
set SMDB.CACHE_CONFIG_OUTPUT_PATH=%TEST_SOLUTION_ROOT%\Out\M{machineNumber}\CacheCore.SingleMachineDistributed.json
set SMDB.CACHE_TEMPLATE_PATH=%TEST_SOLUTION_ROOT%\Out\SharedCache
set BuildXLExportFileDetails=1
set BUILDXL_MASTER_ARGS=/maxProc:2 /replicateOutputsToWorkers %BUILDXL_MASTER_ARGS%
set BUILDXL_WORKER_ARGS=/maxProc:6 %BUILDXL_WORKER_ARGS%
set BUILDXL_COMMON_ARGS=/server- /inCloudBuild /redirectUserProfile- /distributeCacheLookups /enableAsyncLogging /historicMetadataCache "/storageRoot:{objectRoot}:\ " "/config:{sourceRoot}:\Config.dsc" "/cacheConfigFilePath:%SMDB.CACHE_CONFIG_OUTPUT_PATH%" "/rootMap:{sourceRoot}=%TEST_SOLUTION_ROOT%\TestSolution" "/rootMap:{objectRoot}=%TEST_SOLUTION_ROOT%\Out\M{machineNumber}" "/cacheDirectory:{objectRoot}:\Cache" "/p:TestCscToolPath=%ProgramFiles(x86)%\MSBuild\14.0\Bin" /verifyCacheLookupPin /disableProcessRetryOnResourceExhaustion+

REM Add subst source/target to ensure real path to logs are printed on console
set BUILDXL_COMMON_ARGS=%BUILDXL_COMMON_ARGS% /substTarget:{objectRoot}:\  /substSource:"%TEST_SOLUTION_ROOT%\Out\M{machineNumber}"


if NOT DEFINED DISABLE_DBD_TESTRUN (

if NOT EXIST %BUILDXL_TEST_BIN_DIRECTORY%\DistributedBuildRunner.exe (
	echo ERROR: Could not find: '%BUILDXL_TEST_BIN_DIRECTORY%\DistributedBuildRunner.exe'.
	echo.
	echo You must build a full debug BuildXL to 'out\bin\debug\net472. You can't use `bxl.cmd -minimal` for this test.
	echo.
	exit /b 1
)

%BUILDXL_TEST_BIN_DIRECTORY%\DistributedBuildRunner.exe 2 %*

)

if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)

endlocal && exit /b 0