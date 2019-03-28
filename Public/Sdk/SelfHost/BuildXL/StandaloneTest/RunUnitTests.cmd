@ECHO OFF
SETLOCAL

REM Description:
REM     Uses BuildXL to run, but not build, all or some filtered subset of BuildXL Xunit tests.
REM
REM Prerequisities: 
REM     Run "./bxl.cmd -DeployStandaloneTest /q:<qualifier> Out\bin\tests\standalonetest\*" to build and deploy test dlls.
REM     This command will not run any tests.
REM
REM Usage:
REM     RunUnitTests.cmd <filter1> <filter2> ... <filterN>
REM         filter: <name>:<comma_separated_values>
REM         name  : limitCategories | skipCategories | parallelCategories | classes
REM
REM Example: 
REM     RunUnitTests.cmd limitCategories:RequiresAdmin -- Run tests that require admin privilege.

cd /D %~dp0
call :SetFQN BUILDXL_DEPLOYMENT_ROOT=%~dp0..\..

if NOT DEFINED BUILDXL_ADDITIONAL_FLAGS (
    set BUILDXL_ADDITIONAL_FLAGS=
)

if NOT DEFINED BUILDXL_BIN_DIRECTORY (
    set BUILDXL_BIN_DIRECTORY=%BUILDXL_DEPLOYMENT_ROOT%\debug\net472
)

REM Default is to just test net472 and .NET Core
set TEST_FILTERS=net472\* win-x64\*
set BUILDXL_EXE_PATH=%BUILDXL_BIN_DIRECTORY%\bxl.exe
set BUILDXL_ARGS=%TEST_FILTERS% /server- /IncrementalScheduling /nowarn:909 /nowarn:11318 /nowarn:11319 /unsafe_IgnorePreloadedDlls- /historicMetadataCache+ /reuseOutputsOnDisk+ /logProcessDetouringStatus+ /logProcessData+ /logProcesses+ %BUILDXL_ADDITIONAL_FLAGS%

FOR %%x in (%*) do (
    FOR /f "tokens=1,2 delims=:" %%a IN ("%%x") do (
        set BUILDXL_TEST_ARGS=%BUILDXL_TEST_ARGS% /p:[StandaloneTest]Filter.%%a=%%b
    )
)

if EXIST Out (
    rd /s/q Out
)
%BUILDXL_EXE_PATH% %BUILDXL_ARGS% %BUILDXL_TEST_ARGS% /c:config.dsc

if %ERRORLEVEL% NEQ 0 (
    endlocal && exit /b 1
)

endlocal && exit /b 0

:SetFQN
	set %1=%~f2
	exit /b 0