@echo off
setlocal

set %ENLISTMENTROOT%=%~dp0..\..\..\..

REM We kill any old BuildXLs that accidentally lingered and cleanup the out/bin and out/objects folders
call %ENLISTMENTROOT%\Shared\Scripts\KillBxlInstancesInRepo.cmd
if EXIST %ENLISTMENTROOT%\Out\Bin (
    echo Cleaning %ENLISTMENTROOT%\Out\Bin
    rmdir /S /Q %ENLISTMENTROOT%\Out\Bin
)
if EXIST %ENLISTMENTROOT%\Out\Objects (
    echo Cleaning %ENLISTMENTROOT%\Out\Objects
    rmdir /S /Q %ENLISTMENTROOT%\Out\Objects
)
if EXIST %ENLISTMENTROOT%\Out\frontend\Nuget\specs (
    echo Cleaning %ENLISTMENTROOT%\Out\frontend\Nuget\specs
    rmdir /S /Q %ENLISTMENTROOT%\Out\frontend\Nuget\specs
)

endlocal && exit /b 0