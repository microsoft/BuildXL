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
set stepName=Running BuildXL on the CoreCLR with a minimal end to end scenario
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    echo Running BuildXL on the CoreCLR, preparing a few things...
    robocopy %ENLISTMENTROOT%\Out\Bin\debug\win-x64 %NETCOREROOT% /E /MT:8 /NS /NC /NFL /NDL /NP
    call :RunBxlCoreClr /f:spec='%ENLISTMENTROOT%\Public\Src\Core\Collections\*' /c:%ENLISTMENTROOT%\config.dsc /server- /cacheGraph-
    set CORECLR_ERRORLEVEL=%ERRORLEVEL%
    rmdir /s /q %NETCOREROOT%
    if %CORECLR_ERRORLEVEL% NEQ 0 (exit /b 1)
call :RecordStep "%stepName%" %start%

set start=%time%
set stepName=Running Example DotNetCoreBuild on CoreCLR
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    set BUILDXL_BIN=%ENLISTMENTROOT%\Out\Bin\debug\win-x64
    call %ENLISTMENTROOT%\Examples\DotNetCoreBuild\build.bat
    set EXAMPLE_BUILD_ERRORLEVEL=%ERRORLEVEL%
    if %EXAMPLE_BUILD_ERRORLEVEL% NEQ 0 (exit /b 1)
call :RecordStep "%stepName%" %start%

set start=%time%
set stepName=Performing a /cleanonly build
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    call %PRROOT%\Utilities\RunBxl.cmd "-Use RunCheckinTests %BUILDXL_ARGS% /cleanonly /f:spec='%ENLISTMENTROOT%\Public\Src\Utilities\Storage\BuildXL.Storage.dsc' /viewer:disable /TraceInfo:RunCheckinTests=CleanOnly"
    if %ERRORLEVEL% NEQ 0 (exit /b 1)
call :RecordStep "%stepName%" %start%

set start=%time%
set stepName=Checking help text
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    call %EXE_DIR%\bxl.exe /help
    if %ERRORLEVEL% NEQ 0 (exit /b 1)
call :RecordStep "%stepName%" %start%

set start=%time%
set stepName=Testing building of VS Solution
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    call %PRROOT%\Utilities\RunBxl.cmd -Use RunCheckinTests %BUILDXL_ARGS% /vs /TraceInfo:RunCheckinTests=VsSolution
    if %ERRORLEVEL% NEQ 0 (exit /b 1)
call :RecordStep "%stepName%" %start%

echo Administrative permissions required for symlink tests. Detecting permissions...
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Administrative permissions not present. Skipping symlink tests...
    goto :SkipSymLinkTest
)
echo Success: Administrative permissions confirmed. Running symlink tests...
set start=%time%
set stepName=Running SymLink Tests
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    call %PRROOT%\Utilities\RunBxl.cmd -Use RunCheckinTests %BUILDXL_ARGS% /unsafe_IgnoreProducingSymlinks+ /c:%ENLISTMENTROOT%\Public\Src\Sandbox\Windows\DetoursTests\SymLink1\config.dsc /viewer:disable /TraceInfo:RunCheckinTests=Symlink /logsDirectory:%~dp0out\Logs\SymLinkTest\
    rmdir /s /q %ENLISTMENTROOT%\Public\Src\Sandbox\Windows\DetoursTests\SymLink1\Out
    if %ERRORLEVEL% NEQ 0 (exit /b 1)
call :RecordStep "%stepName%" %start%
:SkipSymLinkTest

REM Run distributed in non-PR builds
set start=%time%
set stepName=Building Test Project Distributed
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    call %SCRIPTROOT%\BuildDistributedTest.cmd
    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo Building Test Project Distributed        FAILED  ERRORLEVEL:%ERRORLEVEL% 1>&2
        exit /b 1
    )
call :RecordStep "%stepName%" %start%

REM build distributed integration tests
set start=%time%
set stepName=Building Distributed Integration Tests
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
    setlocal
    call %ENLISTMENTROOT%\Private\DistributedIntegrationTest\BuildDistributed.cmd
    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo %stepName% FAILED  ERRORLEVEL:%ERRORLEVEL% 1>&2
        exit /b 1
    )
    endlocal
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

endlocal