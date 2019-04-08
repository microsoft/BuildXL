@echo off

set /A INDEX=0
set STEPS[%INDEX%][0]="Test"

setlocal
SETLOCAL ENABLEEXTENSIONS
SETLOCAL ENABLEDELAYEDEXPANSION

set ENLISTMENTROOT=%~dp0..\..\..
set SCRIPTROOT=%ENLISTMENTROOT%\Shared\Scripts
set PRROOT=%ENLISTMENTROOT%\Shared\Scripts\PR
set EXE_DIR=%ENLISTMENTROOT%\Out\Selfhost\RunCheckinTests
set FINGERPRINT_ERROR_DIR=\\fsu\shares\MsEng\Domino\RunCheckInTests-FingerprintErrorReports

FOR %%a IN ("%ENLISTMENTROOT:~0,-1%") DO SET NETCOREROOT=%%~dpaBuildXL_CoreCLR

set BUILDXL_ARGS=/logOutput:FullOutputOnError /p:RetryXunitTests=1 /processRetries:3

REM we kill any old BuildXL instances that accidentally lingered and cleanup the out/bin and out/objects folders
call %ENLISTMENTROOT%\Shared\Scripts\KillBxlInstancesInRepo.cmd

set start=%time%
set stepName=Building detached to produce stable fingerprints file
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    REM Incremental scheduling is disabled so we can deterministically get all pip fingerprints exported.
    REM This build and the next are disconnected from the shared cache to ensure that they don't converge with a remote build happening at the same time.
    set COMPARE_FINGERPRINTS_LOGS_DIR=%ENLISTMENTROOT%\Out\Logs\CompareFingerprints\
    call %PRROOT%\Utilities\RunBxl.cmd -Use RunCheckinTests -All %BUILDXL_ARGS% /qualifier:Debug /qualifier:Release /p:FeatureUploadSymbolsAndDrops=1 /incrementalscheduling- /TraceInfo:RunCheckinTests=CompareFingerprints1 /logsDirectory:%COMPARE_FINGERPRINTS_LOGS_DIR% -SharedCacheMode disable
    if %ERRORLEVEL% NEQ 0 (exit /b 1)

    REM Produce a fingerprint file of the first run.
    set FIRST_FINGERPRINT_LOG=%COMPARE_FINGERPRINTS_LOGS_DIR%BuildXL.fgrprnt.txt
    %EXE_DIR%\bxlcacheanalyzer.exe /mode:FingerprintText /xl:%COMPARE_FINGERPRINTS_LOGS_DIR%BuildXL.xlg /compress- /o:%FIRST_FINGERPRINT_LOG%
    if %ERRORLEVEL% NEQ 0 (exit /b 1)
call :RecordStep "%stepName%" %start%

set start=%time%
set stepName=Building using BuildXL a second time to ensure all tasks are cached
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    REM At some point we should validate that all source files were 'unchanged' in this build.
    REM incremental scheduling is disabled so we can deterministically get all pip fingerprints exported
    REM Graph caching is disabled in case there is nondeterminism during graph construction.
    REM We use the same logs directory but with different prefix.
    REM
    REM We also explicitly use /enableLazyOutputs- here to make sure that tools needed for the rest of this Scripts
    REM (e.g., DistributedBuildRunner) are materialized on disk
    set SECOND_PREFIX=BuildXL.2
    call %PRROOT%\Utilities\RunBxl.cmd -Use RunCheckinTests -All %BUILDXL_ARGS% /qualifier:Debug /qualifier:Release /logsDirectory:%COMPARE_FINGERPRINTS_LOGS_DIR% /logPrefix:%SECOND_PREFIX% /IncrementalScheduling- /enableLazyOutputs- /p:FeatureUploadSymbolsAndDrops=1  /f:"~(tag='artifact-services-drop-pip')and~(tag='LongRunningTest')" /TraceInfo:RunCheckinTests=CompareFingerprints2 -SharedCacheMode disable /cachegraph-
    if %ERRORLEVEL% NEQ 0 (exit /b 1)

    REM Produce a fingerprint file of the second run.
    set SECOND_FINGERPRINT_LOG=%COMPARE_FINGERPRINTS_LOGS_DIR%%SECOND_PREFIX%.fgrprnt.txt
    %EXE_DIR%\bxlcacheanalyzer.exe /mode:FingerprintText /xl:%COMPARE_FINGERPRINTS_LOGS_DIR%%SECOND_PREFIX%.xlg /compress- /o:%SECOND_FINGERPRINT_LOG%
    if %ERRORLEVEL% NEQ 0 (exit /b 1)

    REM Compare fingerprints
    fc %FIRST_FINGERPRINT_LOG% %SECOND_FINGERPRINT_LOG% > %SECOND_FINGERPRINT_LOG%.diff.txt
    if %ERRORLEVEL% NEQ 0 (
        echo .
        echo ERROR: BuildWithBuildXL-Debug-Release used different fingerprints when run a second time  ERRORLEVEL:%ERRORLEVEL% 1>&2
        echo First Fingerprints Log: %FIRST_FINGERPRINT_LOG% 1>&2
        echo Second Fingerprints Log: %SECOND_FINGERPRINT_LOG% 1>&2
        echo Fingerprint diff: %SECOND_FINGERPRINT_LOG%.diff.txt 1>&2

        for /F %%i IN ('echo %COMPARE_FINGERPRINTS_LOGS_DIR%') DO (set LOGS_DIR_NAME=%%~pi)
        if "%LOGS_DIR_NAME%" == "" (
            set LOGS_DIR_NAME=%COMPUTERNAME%\%RANDOM%
        )
        echo Copying logs to %FINGERPRINT_ERROR_DIR%\%LOGS_DIR_NAME%
        robocopy /E /MT:8 %COMPARE_FINGERPRINTS_LOGS_DIR% %FINGERPRINT_ERROR_DIR%\%LOGS_DIR_NAME%
        exit /b 1
    )

    REM verify fully cached
    call :VerifyLogIsFullyCached %COMPARE_FINGERPRINTS_LOGS_DIR%%SECOND_PREFIX%.log
    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo BuildWithBuildXL-Debug-Release cached was not fully cached    FAILED  ERRORLEVEL:%ERRORLEVEL% 1>&2

        exit /b 1
    )
call :RecordStep "%stepName%" %start%

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

endlocal