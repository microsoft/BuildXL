@echo off
setlocal
SETLOCAL ENABLEEXTENSIONS
SETLOCAL ENABLEDELAYEDEXPANSION

set /A INDEX=0
set ENLISTMENTROOT=%~dp0

if EXIST %ENLISTMENTROOT%\Out\frontend\Nuget\specs (
   echo Cleaning %ENLISTMENTROOT%\Out\frontend\Nuget\specs
   rmdir /S /Q %ENLISTMENTROOT%\Out\frontend\Nuget\specs
)

set start=%time%
set stepName=Building BuildXL with DebugDotNetCoreMac qualifier
call :StatusMessage %stepName%
    setlocal
    call :RunBuildXL /q:DebugDotNetCoreMac /q:ReleaseDotNetCoreMac /scrub %*
    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo %stepName% FAILED ERRORLEVEL:%ERRORLEVEL% 1>&2
        goto error
    )
    endlocal
call :RecordStep "%stepName%" %start%

call :PrintStatistics
echo.
echo ++++++++++++++++++++++++++++++++++++++++++++++++
echo + SUCCESS  :-)  Mac tests are ready to run. +
echo ++++++++++++++++++++++++++++++++++++++++++++++++
echo.
title
endlocal && exit /b 0

:error
    call :PrintStatistics
    echo.
    echo ---------------------------------------------------------------
    echo - FAILURE  :-(  Fix the issues and SlimBuildForMacTests.cmd again. -
    echo ---------------------------------------------------------------
    title
    endlocal && exit /b 1


:RunBuildXL
    call %ENLISTMENTROOT%\bxl.cmd %*
    if %ERRORLEVEL% NEQ 0 (
        echo. 1>&2
        echo --------------------------------------------------------------- 1>&2
        echo - Failed BuildXL invocation:  1>&2
        echo -    %ENLISTMENTROOT%\bxl.cmd %* 1>&2
        echo - ERRORLEVEL:%ERRORLEVEL% 1>&2
        echo --------------------------------------------------------------- 1>&2
        echo. 1>&2
        exit /b 1
    )
    exit /b 0

:StatusMessage
    echo -----------------------------------------------------------------------------------------------------------
    echo -- SlimBuildForMacTests -- %*
    echo -----------------------------------------------------------------------------------------------------------
    title SlimBuildForMacTests -- %*
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
