@echo off
setlocal EnableDelayedExpansion

if not exist %~dp0..\nuget.exe (
    echo Nuget executable couldn't be found, please make sure its available!
    exit /b 1
)

for /f %%i in ('dotnet VersionUpdater\VersionUpdater.dll ..\..\Build\Versions.props ContentStoreInterfaces\ContentStoreInterfaces.nuspec MemoizationStoreInterfaces\MemoizationStoreInterfaces.nuspec') do set VERSION=%%i
if  %VERSION% == "" (
    echo Couldn't parse the current CloudStore version!
    exit /b 1
)

pushd %~dp0..\..

call :DoCall "%~dp0..\nuget.exe push -Source "https://pkgs.dev.azure.com/mseng/_packaging/Domino.Public.Experimental/nuget/v3/index.json" -ApiKey VSTS %~dp0ContentStoreInterfaces.%VERSION%-netcore.nupkg" "Pushing the Content Store Interfaces Nuget package to the experimental feed"
call :DoCall "%~dp0..\nuget.exe push -Source "https://pkgs.dev.azure.com/mseng/_packaging/Domino.Public.Experimental/nuget/v3/index.json" -ApiKey VSTS %~dp0MemoizationStoreInterfaces.%VERSION%-netcore.nupkg" "Pushing the Memoization Store Interfaces Nuget package to the experimental feed"

goto :eof

:DoCall
:: %1 = "full command to run with arguments in quotes"
:: %2 = "command description"
echo Executing %~2: %~1
call %~1
if ERRORLEVEL 1 (
    echo '*******'
    echo %~2
    echo %~1
    echo '*******'
    call :FailFromFunction 2>NUL
)
goto :eof
:FailFromFunction
popd
:: Yes, this looks weird
:: http://stackoverflow.com/questions/10534911/how-can-i-exit-a-batch-file-from-within-a-function 
()
exit /b 1
