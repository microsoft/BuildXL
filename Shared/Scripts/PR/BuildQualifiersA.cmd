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

set start=%time%
set "stepName=Build full BuildXL using deployment"
call %PRROOT%\Utilities\StatusMessage.cmd %stepName%
call %PRROOT%\Utilities\RunBxl.cmd "-Use RunCheckInTests -All /logOutput:FullOutputOnError /p:RetryXunitTests=1 /processRetries:3 /q:Debug /q:Release /q:DebugNet451 /q:ReleaseNet451 /p:FeatureUploadSymbolsAndDrops=1 /TraceInfo:RunCheckinTests=NewBits"
if %ERRORLEVEL% NEQ 0 (
    call %PRROOT%\Utilities\Error.cmd 
    exit /b 1
)

echo.
echo +++++++++++++++++++++++++++++++++++++++++++++++++++++
echo + SUCCESS  :-)  The first set of qualifiers passed. +
echo +++++++++++++++++++++++++++++++++++++++++++++++++++++
echo.
echo Remember, if you perform any further commits, merges or rebases before pushing, you MUST RunCheckInTests.cmd again.
echo.
call %ENLISTMENTROOT%\Shared\Scripts\KillBxlInstancesInRepo.cmd
title

endlocal && exit /b 0