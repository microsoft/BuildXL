@echo off
setlocal
SETLOCAL ENABLEEXTENSIONS
SETLOCAL ENABLEDELAYEDEXPANSION

set /A INDEX=0
set ENLISTMENTROOT=%~dp0
FOR %%a IN ("%ENLISTMENTROOT:~0,-1%") DO SET NETCOREROOT=%%~dpaBuildXL_CoreCLR
set SCRIPTROOT=%~dp0Shared\Scripts\
set EXE_DIR=%~dp0out\Bin\Debug\net472
set FINGERPRINT_ERROR_DIR=\\fsu\shares\MsEng\Domino\RunCheckInTests-FingerprintErrorReports
set BUILDXL_ARGS=
set RUN_PART_A=1
set RUN_PART_B=1
set MINIMAL_LAB=0

REM These are provided to bxl.cmd only when /lab is specified (automated builds).
REM For lab builds, log full outputs.
REM For lab builds, retry unit tests automatically.
set LAB_SPECIFIC_ARGS=/logOutput:FullOutputOnError /p:RetryXunitTests=1 /processRetries:3
set INTERNAL_BUILD_ARGS=/p:[Sdk.BuildXL]microsoftInternal=1

if not defined [BuildXL.Branding]SemanticVersion (
	if defined [BuildXL_Branding]SemanticVersion (
		echo PATCHING VSTS environment variable hack for SemanticVersion
		set [BuildXL.Branding]SemanticVersion=%[BuildXL_Branding]SemanticVersion%
	)
)
if not defined [BuildXL.Branding]PrereleaseTag (
	if defined [BuildXL_Branding]PrereleaseTag (
		echo PATCHING VSTS environment variable hack for PrereleaseTag
		set [BuildXL.Branding]PrereleaseTag=%[BuildXL_Branding]PrereleaseTag%
	)
)


if defined [BuildXL.Branding]SemanticVersion (
	echo BUILD VERSIONING: [BuildXL.Branding]SemanticVersion=%[BuildXL.Branding]SemanticVersion%
)
if defined [BuildXL.Branding]PrereleaseTag (
	echo BUILD VERSIONING: [BuildXL.Branding]PrereleaseTag=%[BuildXL.Branding]PrereleaseTag%
)

REM TODO: this list contains temporary nowarns as we tighten down the language and until specs are updated to the latest syntax
set NO_WARNS=""

call :ParseCommandLine %*
if %ERRORLEVEL% NEQ 0 (
    exit /b 1
)

REM we kill any old bxl.exe instances that accidentally lingered and cleanup the out/bin and out/objects folders
echo Terminating existing runnings builds on this machine first
call %ENLISTMENTROOT%\Shared\Scripts\KillBxlInstancesInRepo.cmd
if EXIST %ENLISTMENTROOT%\Out\Bin (
    echo Cleaning %ENLISTMENTROOT%\Out\Bin
    rmdir /S /Q %ENLISTMENTROOT%\Out\Bin
)
if EXIST %ENLISTMENTROOT%\Out\Objects (
    echo Cleaning %ENLISTMENTROOT%\Out\Objects
    rmdir /S /Q %ENLISTMENTROOT%\Out\Objects
)
if EXIST %ENLISTMENTROOT%\Out\Selfhost (
    echo Cleaning %ENLISTMENTROOT%\Out\Selfhost
    rmdir /S /Q %ENLISTMENTROOT%\Out\Selfhost
)
if EXIST %ENLISTMENTROOT%\Out\frontend\Nuget\specs (
    echo Cleaning %ENLISTMENTROOT%\Out\frontend\Nuget\specs
    rmdir /S /Q %ENLISTMENTROOT%\Out\frontend\Nuget\specs
)

set start=%time%
set stepName=Building 'debug\net472' and 'debug\win-x64' using Lkg and deploying to RunCheckinTests
call :StatusMessage %stepName%
    call :RunBxl -Use LKG -Deploy RunCheckinTests /q:DebugNet472 /q:DebugDotNetCore /f:output='%ENLISTMENTROOT%\Out\Bin\debug\net472\*'oroutput='%ENLISTMENTROOT%\Out\Bin\debug\win-x64\*'oroutput='%ENLISTMENTROOT%\Out\Bin\tests\debug\*' %BUILDXL_ARGS% /enableLazyOutputs- /TraceInfo:RunCheckinTests=LKG /useCustomPipDescriptionOnConsole-
    if %ERRORLEVEL% NEQ 0 goto BadLKGMessage
call :RecordStep "%stepName%" %start%

IF "%RUN_PART_A%" == "1" (
    call :PartA
    if !ERRORLEVEL! NEQ 0 goto error
)

IF "%RUN_PART_B%" == "1" (
    call :PartB
    if !ERRORLEVEL! NEQ 0 goto error
)

IF DEFINED FULL (
set start=%time%
set stepName=Verifying memoized file hashes
call :StatusMessage %stepName%
    call %SCRIPTROOT%VerifyFileContentTable.cmd
    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo =================================================================================
        echo.
        echo Verifying memoized file hashes
        echo.
        echo Your local cache of file hashes could not be verified or became inaccurate.
        echo This suggests an implementation fault in the File Content Table, disk corruption,
        echo or a yet-uncovered file system race. Please contact domdev.
        echo.
        echo =================================================================================
        echo.
        goto error
    )
call :RecordStep "%stepName%" %start%
)

call :PrintStatistics
echo.
echo ++++++++++++++++++++++++++++++++++++++++++++++++
echo + SUCCESS  :-)  You may now push your changes. +
echo ++++++++++++++++++++++++++++++++++++++++++++++++
echo.
echo Remember, if you perform any further commits, merges or rebases before pushing, you MUST RunCheckInTests.cmd again.
echo.
call %ENLISTMENTROOT%\Shared\Scripts\KillBxlInstancesInRepo.cmd
title
endlocal && exit /b 0

:PartA
    set start=!time!
    set stepName=Building using BuildXL
    if "!MINIMAL_LAB!" == "1" (
        set options=-Use RunCheckinTests %BUILDXL_ARGS% /f:output='%ENLISTMENTROOT%\Out\Bin\*'oroutput='%ENLISTMENTROOT%\Public\Src\Cache\Out\Bin\*' /q:ReleaseNet472 /q:ReleaseDotNetCore /q:ReleaseDotNetCoreMac /TraceInfo:RunCheckinTests=NewBitsMinimal
    ) else (
        REM WARNING: Cache only runs tests for the Debug qualifiers! Please do not remove Debug qualifiers from this step.
        set options=-Use RunCheckinTests -All %BUILDXL_ARGS% /q:DebugNet472 /q:ReleaseNet472 /q:DebugDotNetCore /q:ReleaseDotNetCore /q:DebugDotNetCoreMac /q:ReleaseDotNetCoreMac /TraceInfo:RunCheckinTests=NewBits
    )
    call :StatusMessage !stepName!
        call :RunBxl !options!
        if !ERRORLEVEL! NEQ 0 (exit /b 1)
    call :RecordStep "!stepName!" !start!

    exit /b 0

:PartB
    set start=!time!
    set stepName=Running BuildXL on the CoreCLR with a minimal end to end scenario
    call :StatusMessage !stepName!
        echo Running BuildXL on the CoreCLR, preparing a few things...
        robocopy %ENLISTMENTROOT%\Out\Bin\debug\win-x64 %NETCOREROOT% /E /MT:8 /NS /NC /NFL /NDL /NP
        call :RunBxlCoreClr /p:[Sdk.BuildXL]microsoftInternal=1 /f:spec='%ENLISTMENTROOT%\Public\Src\Utilities\Collections\*' /c:%ENLISTMENTROOT%\config.dsc /server- /cacheGraph-
        set CORECLR_ERRORLEVEL=%ERRORLEVEL%
        rmdir /s /q %NETCOREROOT%
        if !CORECLR_ERRORLEVEL! NEQ 0 (exit /b 1)
    call :RecordStep "!stepName!" !start!

    set start=!time!
    set stepName=Running Example DotNetCoreBuild on CoreCLR
    call :StatusMessage !stepName!
        set BUILDXL_BIN=%ENLISTMENTROOT%\Out\Bin\debug\win-x64
        call %ENLISTMENTROOT%\Examples\DotNetCoreBuild\build.bat
        set EXAMPLE_BUILD_ERRORLEVEL=%ERRORLEVEL%
        if !EXAMPLE_BUILD_ERRORLEVEL! NEQ 0 (exit /b 1)
    call :RecordStep "!stepName!" !start!

    set start=!time!
    set stepName=Performing a /cleanonly build
    call :StatusMessage !stepName!
        call :RunBxl -Use RunCheckinTests /q:DebugNet472 %BUILDXL_ARGS% /cleanonly /f:spec='%ENLISTMENTROOT%\Public\Src\Utilities\Instrumentation\LogGen\BuildXL.LogGen.dsc' /viewer:disable /TraceInfo:RunCheckinTests=CleanOnly
        if !ERRORLEVEL! NEQ 0 (exit /b 1)
    call :RecordStep "!stepName!" !start!

    set start=!time!
    set stepName=Building detached to produce stable fingerprints file
    call :StatusMessage !stepName!
        REM Incremental scheduling is disabled so we can deterministically get all pip fingerprints exported.
        REM This build and the next are disconnected from the shared cache to ensure that they don't converge with a remote build happening at the same time.
        set COMPARE_FINGERPRINTS_LOGS_DIR=%ENLISTMENTROOT%\Out\Logs\CompareFingerprints\
        REM Neither /cacheGraph- nor /scriptShowSlowest need be used here (and in the next step).
        REM The reason why they are used here is to exercise DScript front end on .NET Core
        call :RunBxl /cacheGraph- /scriptShowSlowest -Use RunCheckinTests -minimal %BUILDXL_ARGS% /incrementalScheduling- /TraceInfo:RunCheckinTests=CompareFingerprints1 /logsDirectory:!COMPARE_FINGERPRINTS_LOGS_DIR! -SharedCacheMode disable
        if !ERRORLEVEL! NEQ 0 (exit /b 1)

        REM Produce a fingerprint file of the first run.
        set FIRST_FINGERPRINT_LOG=!COMPARE_FINGERPRINTS_LOGS_DIR!BuildXL.fgrprnt.txt
        %EXE_DIR%\bxlAnalyzer.exe /mode:FingerprintText /xl:!COMPARE_FINGERPRINTS_LOGS_DIR!BuildXL.xlg /compress- /o:!FIRST_FINGERPRINT_LOG!
        if !ERRORLEVEL! NEQ 0 (exit /b 1)
    call :RecordStep "!stepName!" !start!

    set start=!time!
    set stepName=Building using BuildXL a second time to ensure all tasks are cached
    call :StatusMessage !stepName!
        REM At some point we should validate that all source files were 'unchanged' in this build.
        REM incremental scheduling is disabled so we can deterministically get all pip fingerprints exported
        REM Graph caching is disabled in case there is nondeterminism during graph construction.
        REM We use the same logs directory but with different prefix.
        set SECOND_PREFIX=BuildXL.2
        call :RunBxl /cacheGraph- /scriptShowSlowest -Use RunCheckinTests -minimal %BUILDXL_ARGS% /incrementalScheduling- /TraceInfo:RunCheckinTests=CompareFingerprints2 /logsDirectory:!COMPARE_FINGERPRINTS_LOGS_DIR! -SharedCacheMode disable /logPrefix:!SECOND_PREFIX!
        if !ERRORLEVEL! NEQ 0 (exit /b 1)

        REM Produce a fingerprint file of the second run.
        set SECOND_FINGERPRINT_LOG=!COMPARE_FINGERPRINTS_LOGS_DIR!!SECOND_PREFIX!.fgrprnt.txt
        %EXE_DIR%\bxlAnalyzer.exe /mode:FingerprintText /xl:!COMPARE_FINGERPRINTS_LOGS_DIR!!SECOND_PREFIX!.xlg /compress- /o:!SECOND_FINGERPRINT_LOG!
        if !ERRORLEVEL! NEQ 0 (exit /b 1)

        REM Compare fingerprints
        fc !FIRST_FINGERPRINT_LOG! !SECOND_FINGERPRINT_LOG! > !SECOND_FINGERPRINT_LOG!.diff.txt
        if !ERRORLEVEL! NEQ 0 (
            echo .
            echo ERROR: BuildWithBuildXL-Minimal used different fingerprints when run a second time  ERRORLEVEL:!ERRORLEVEL! 1>&2
            echo First Fingerprints Log: !FIRST_FINGERPRINT_LOG! 1>&2
            echo Second Fingerprints Log: !SECOND_FINGERPRINT_LOG! 1>&2
            echo Fingerprint diff: !SECOND_FINGERPRINT_LOG!.diff.txt 1>&2

            for /F %%i IN ('echo !COMPARE_FINGERPRINTS_LOGS_DIR!') DO (set LOGS_DIR_NAME=%%~pi)
            if "%LOGS_DIR_NAME%" == "" (
                set LOGS_DIR_NAME=%COMPUTERNAME%\%RANDOM%
            )
            echo Copying logs to %FINGERPRINT_ERROR_DIR%\%LOGS_DIR_NAME%
            robocopy /E /MT:8 !COMPARE_FINGERPRINTS_LOGS_DIR! %FINGERPRINT_ERROR_DIR%\%LOGS_DIR_NAME%
            exit /b 1
        )

        REM verify fully cached
        call :VerifyLogIsFullyCached !COMPARE_FINGERPRINTS_LOGS_DIR!!SECOND_PREFIX!.log
        if !ERRORLEVEL! NEQ 0 (
            echo.
            echo BuildWithBuildXL-Minimal cached was not fully cached    FAILED  ERRORLEVEL:!ERRORLEVEL! 1>&2

            exit /b 1
        )
    call :RecordStep "!stepName!" !start!

    echo Administrative permissions required for symlink tests. Detecting permissions...
    net session >nul 2>&1
    if !ERRORLEVEL! NEQ 0 (
        echo Administrative permissions not present. Skipping symlink tests...
        goto :SkipSymLinkTest
    )
    echo Success: Administrative permissions confirmed. Running symlink tests...
    set start=!time!
    set stepName=Running SymLink Tests
    call :StatusMessage !stepName!
        call :RunBxl -Use RunCheckinTests %BUILDXL_ARGS% /unsafe_IgnoreProducingSymlinks+ /c:%ENLISTMENTROOT%\Public\Src\Sandbox\Windows\DetoursTests\SymLink1\config.dsc /viewer:disable /TraceInfo:RunCheckinTests=Symlink /logsDirectory:%~dp0out\Logs\SymLinkTest\
        rmdir /s /q %ENLISTMENTROOT%\Public\Src\Sandbox\Windows\DetoursTests\SymLink1\Out
        if !ERRORLEVEL! NEQ 0 (exit /b 1)
    call :RecordStep "!stepName!" !start!
    :SkipSymLinkTest

    if "!MINIMAL_LAB!" == "0" (

        REM populate Release\*
        set start=!time!
        set stepName=Populating release for distribution tests
        call :StatusMessge !stepName!
            call :RunBxl -Use RunCheckinTests /q:ReleaseNet472 /f:output='%ENLISTMENTROOT%\Out\Bin\release\net472\*' %BUILDXL_ARGS%
            if !ERRORLEVEL! NEQ 0 (
                echo.
                echo !stepName! FAILED  ERRORLEVEL:!ERRORLEVEL! 1>&2
                exit /b 1
            )
        call :RecordStep "!stepName!" !start!

        set BUILDXL_BIN_DIRECTORY=%~dp0out\Bin\release\net472
        set start=!time!
        set stepName=Building Test Project Distributed
        call :StatusMessage !stepName!
            call %SCRIPTROOT%\BuildDistributedTest.cmd
            if !ERRORLEVEL! NEQ 0 (
                echo.
                echo !stepName! FAILED  ERRORLEVEL:!ERRORLEVEL! 1>&2
                exit /b 1
            )
        call :RecordStep "!stepName!" !start!

        REM build distributed integration tests
        set start=!time!
        set stepName=Building Distributed Integration Tests
        call :StatusMessage !stepName!
            setlocal
            call %ENLISTMENTROOT%\Private\DistributedIntegrationTest\BuildDistributed.cmd
            if !ERRORLEVEL! NEQ 0 (
                echo.
                echo !stepName! FAILED  ERRORLEVEL:!ERRORLEVEL! 1>&2
                exit /b 1
            )
            endlocal
        call :RecordStep "!stepName!" !start!
    )

    exit /b 0

:error
    call :PrintStatistics
    echo.
    echo ---------------------------------------------------------------
    echo - FAILURE  :-(  Fix the issues and RunCheckInTests.cmd again. -
    echo ---------------------------------------------------------------
    call %ENLISTMENTROOT%\Shared\Scripts\KillBxlInstancesInRepo.cmd
    title
    endlocal && exit /b 1

:ParseCommandLine
    if /I "%1" == "/maxproc" (
        set BUILDXL_ARGS=%BUILDXL_ARGS% /maxProc:%2

        shift
        shift
    )

    if /I "%1" == "/lab" (
        set BUILDXL_ARGS=%LAB_SPECIFIC_ARGS% %BUILDXL_ARGS%
        set [Sdk.BuildXL]xunitSemaphoreCount=20
        echo ***Running in /lab mode: Note that this build will populate the distributed cache.***
        shift
    ) else if /I "%1" == "/minimal_lab" (
        set BUILDXL_ARGS=%LAB_SPECIFIC_ARGS% %BUILDXL_ARGS%
        set MINIMAL_LAB=1
        echo ***Running minimal lab suite.***
        shift
    )

    if /I "%1" == "/partA" (
        set RUN_PART_A=1
        set RUN_PART_B=0
        echo ***Running only Part A***
        shift
    ) else if /I "%1" == "/partB" (
        set RUN_PART_A=0
        set RUN_PART_B=1
        echo ***Running only Part B***
        shift
    )

    if /I "%1" == "/full" (
        rem Set this to perform the full suite
        set FULL=1
        echo ***Running full suite, including very expensive tests***
        shift
    )

    if /I "%1" == "/internal" (
        set BUILDXL_ARGS=%INTERNAL_BUILD_ARGS% %BUILDXL_ARGS%
        echo ***Running internal build***
        shift
    )

    if "%1" NEQ "" (
        echo Unrecognized argument: %1 1>&2
        exit /b 1
    )
    exit /b 0


:RunBxl
    call %ENLISTMENTROOT%\Bxl.cmd %*
    if %ERRORLEVEL% NEQ 0 (
        echo. 1>&2
        echo --------------------------------------------------------------- 1>&2
        echo - Failed BuildXL invocation:  1>&2
        echo -    %ENLISTMENTROOT%\Bxl.cmd %* 1>&2
        echo - ERRORLEVEL:%ERRORLEVEL% 1>&2
        echo --------------------------------------------------------------- 1>&2
        echo. 1>&2
        exit /b 1
    )
    exit /b 0

:RunBxlCoreClr
    REM BUG: 1199393: Temporary have to hack the generated nuspecs since the coreclr run doesn't run under b:
    rmdir /s/q %ENLISTMENTROOT%\Out\frontend\Nuget\specs
    set cmd=%NETCOREROOT%\bxl.exe %*
    echo %cmd%
    call %cmd%
    if %ERRORLEVEL% NEQ 0 (
        echo. 1>&2
        echo --------------------------------------------------------------- 1>&2
        echo - Failed BuildXL CoreCLR invocation:  1>&2
        echo -    %NETCOREROOT%\bxl.exe %* 1>&2
        echo - ERRORLEVEL:%ERRORLEVEL% 1>&2
        echo --------------------------------------------------------------- 1>&2
        echo. 1>&2
        exit /b 1
    )
    REM BUG: 1199393: Temporary have to hack the generated nuspecs since the coreclr run doesn't run under b:
    rmdir /s/q %ENLISTMENTROOT%\Out\frontend\Nuget\specs

    exit /b 0

:VerifyLogIsFullyCached
REM DScript relies on journaling to get a pip graph hit. This is not always available, since it either needs an elevated prompt or the scan journal service running
REM TODO: Uncomment when these restrictions are relaxed.
REM    SET PIP_GRAPH_CACHED=
REM    for /F "tokens=1-2 delims=]" %%a IN ('findstr /c:^"Reloading pip graph from previous build^" %1') DO ( SET PIP_GRAPH_CACHED=true)
REM    IF "%PIP_GRAPH_CACHED%"=="" (
REM        echo.
REM        echo Unable to find 'Reloading pip graph from previous build' in %1 1>&2
REM        endlocal && exit /b 1
REM    )

    SET EXISTING_SERVER_STRING=
    for /F "tokens=1-2 delims=]" %%a IN ('findstr /c:^"Running from existing BuildXL server process^" %1') DO ( SET EXISTING_SERVER_STRING=%%b)
    IF "%EXISTING_SERVER_STRING%"=="" (
        echo.
        echo Build should have reused an existing server process. Unable to find server reuse message
        endlocal && exit /b 1
    )

    SET PROCESS_LAUNCHED_COUNT_STRING=
    for /F "tokens=1-2 delims=]" %%a IN ('findstr /c:^"Processes that were launched^" %1') DO ( SET PROCESS_LAUNCHED_COUNT_STRING=%%b)
    IF "%PROCESS_LAUNCHED_COUNT_STRING%"=="" (
        echo.
        echo Unable to find process count in %1 1>&2
        endlocal && exit /b 1
    )

    SET PROCESS_LAUNCHED_COUNT=
    for /F "tokens=2-3 delims=:" %%a IN ('echo %PROCESS_LAUNCHED_COUNT_STRING%') DO ( SET PROCESS_LAUNCHED_COUNT=%%b)
    IF "%PROCESS_LAUNCHED_COUNT%"=="" (
        echo.
        echo Unable to find cached process count in %1 1>&2
        endlocal && exit /b 1
    )

    SET CACHED_COUNT_STRING=
    for /F "tokens=1-2 delims=]" %%a IN ('findstr /c:^"Processes that were skipped due to cache hit^" %1') DO ( SET CACHED_COUNT_STRING=%%b)
    IF "%CACHED_COUNT_STRING%"=="" (
        echo.
        echo Unable to find process count in %1 1>&2
        endlocal && exit /b 1
    )

    SET CACHED_COUNT=
    for /F "tokens=2-3 delims=:" %%a IN ('echo %CACHED_COUNT_STRING%') DO ( SET CACHED_COUNT=%%b)
    IF "%CACHED_COUNT%"=="" (
        echo.
        echo Unable to find cached process count in %1 1>&2
        endlocal && exit /b 1
    )

    IF NOT "%PROCESS_LAUNCHED_COUNT%"==" 0" (
        echo.
        echo Cached build of BuildXL was not fully cached. Process launched count [%PROCESS_LAUNCHED_COUNT%] was not zero. 1>&2
        endlocal && exit /b 1
    )
    echo Most recent run at %1 cached %CACHED_COUNT% of %PROCESS_COUNT% process executions.
    endlocal && exit /b 0

:StatusMessage
    echo -----------------------------------------------------------------------------------------------------------
    echo -- RunCheckinTest -- %*
    echo -----------------------------------------------------------------------------------------------------------
    title RunCheckinTest -- %*
    exit /b 0

:RecordStep
    set _stepname=%~1
    set _starttime=%~2
    call :ElapsedSeconds %_starttime% %time% _duration
    set STEPS[%INDEX%][0]=%_stepname%
    set STEPS[%INDEX%][1]=%_duration%
    set /A INDEX = INDEX+1
    exit /b

:PrintStatistics
    echo.
    echo ================================== Instrumentation Results ==================================
    set /A "length=INDEX-1"
    set _sum=0
    for /L %%i in (0,1,!length!) do (
        set _step=!STEPS[%%i][0]!
        set "_spaces=                                                                                 "
        set "_duration=!_spaces!!STEPS[%%i][1]!"
        set "_line=!_step!!_spaces!"
        set "_line=!_line:~0,80!:!_duration:~-5! sec"
        call echo == !_line!
        set /A "_sum=_sum+_duration"
    )
    set "_line=Total!_spaces!"
    set "_sum=!_spaces!!_sum!"
    set "_line=!_line:~0,80!:!_sum:~-5! sec"
    echo == !_line!
    echo =============================================================================================
    exit /b

:ElapsedSeconds
    for /f "tokens=1-4 delims=:.," %%a in ("%~1") do set /a _start=(((%%a*60)+1%%b %% 100)*60+1%%c %% 100)
    for /f "tokens=1-4 delims=:.," %%a in ("%~2") do set /a _stop=(((%%a*60)+1%%b %% 100)*60+1%%c %% 100)
    set /a _elapsedseconds=_stop-_start
    if %_elapsedseconds% lss 0 set /a _elapsedseconds+=86400
    set %~3=%_elapsedseconds%
    exit /b

:BadLKGMessage
echo. 1>&2
echo ================================================================================= 1>&2
echo. 1>&2
echo Building BuildXL using the NuGet LKG Failed:1>&2
echo. 1>&2
echo It seems that you have made a change that is incompatible with the NuGetPackage 1>&2
echo we use to bootstrap the BuildXL build. 1>&2
echo You will have to publish a new NuGet Package.  1>&2
echo. 1>&2
echo Please run the following command: 1>&2
echo    Shared\Scripts\PublishDevLkg.cmd 1>&2
echo to update it with your updated code. 1>&2
echo. 1>&2
echo ================================================================================= 1>&2
echo. 1>&2
goto error
