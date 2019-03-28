@echo off

set /A INDEX=0
set STEPS[%INDEX%][0]="Test"

setlocal
SETLOCAL ENABLEEXTENSIONS
SETLOCAL ENABLEDELAYEDEXPANSION

set ENLISTMENTROOT=%~dp0..\..\..
set PRROOT=%ENLISTMENTROOT%\Shared\Scripts\PR

REM Kill BuildXL and clean some output dirs
call %PRROOT%\Utilities\PrepareEnvironment.cmd

REM Ensure the Selfhost directory will only contain the deployed files
if EXIST %ENLISTMENTROOT%\Out\Selfhost (
    echo Cleaning %ENLISTMENTROOT%\Out\Selfhost
    rmdir /S /Q %ENLISTMENTROOT%\Out\Selfhost
)

set start=%time%
set "stepName=Build BuildXL using LKG"
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
call %PRROOT%\Utilities\RunBxl.cmd "-Use LKG -Deploy RunCheckinTests /q:Debug /q:DebugDotNetCore /f:output='%ENLISTMENTROOT%\Out\Bin\debug\net472\*'oroutput='%ENLISTMENTROOT%\Out\Bin\debug\win-x64\*' /logOutput:FullOutputOnError /p:RetryXunitTests=1 /processRetries:3 /enableLazyOutputs- /TraceInfo:RunCheckinTests=LKG /useCustomPipDescriptionOnConsole-"
if %ERRORLEVEL% NEQ 0 (
    call %PRROOT%\Utilities\Error.cmd 
    exit /b 1
)

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