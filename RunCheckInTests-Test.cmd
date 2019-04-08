@echo off
setlocal

set SCRIPTS=%~dp0Shared\Scripts
set PRROOT=%SCRIPTS%\PR
set StatusMessage=%PRROOT%\Utilities\StatusMessage.cmd
set Error=%PRROOT%\Utilities\Error.cmd

call %StatusMessage% "Build latest with LKG and deploy"
call %PRROOT%\DeployLatestWithLKG.cmd
if %ERRORLEVEL% NEQ 0 (
    call %Error%
    exit /b 1
)

call %StatusMessage% "Validate the deployment"
call %PRROOT%\TestWithDeployment.cmd
if %ERRORLEVEL% NEQ 0 (
    call %Error%
    exit /b 1
)

call %StatusMessage% "Build the first set of qualifiers"
call %PRROOT%\BuildQualifiersA.cmd
if %ERRORLEVEL% NEQ 0 (
    call %Error%
    exit /b 1
)

call %StatusMessage% "Build the second set of qualifiers"
call %PRROOT%\BuildQualifiersB.cmd
if %ERRORLEVEL% NEQ 0 (
    call %Error%
    exit /b 1
)

echo.
echo ++++++++++++++++++++++++++++++++++++++++++++++++
echo + SUCCESS  :-)  You may now push your changes. +
echo ++++++++++++++++++++++++++++++++++++++++++++++++
echo.
call %SCRIPTS%\KillBxlInstancesInRepo.cmd

endlocal %% exit /b 0