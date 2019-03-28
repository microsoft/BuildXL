@echo off
setlocal
set FastBuild=0
pushd %~dp0..
call :CheckForDirtyEnv
call :SetupEnv
call :RestorePackages
call :Build
call :RunUnitTests
call :RunIntegrationTests
rem call :ResharperCheck
set OutRoot=%cd%\..\out
set PackageOutRoot=%OutRoot%\Nuget
powershell -NonInteractive -NoProfile -Command "Write-Host Packages created in %PackageOutRoot% -foregroundcolor green"
echo Success!
popd
exit /b 0

:CheckForDirtyEnv
:: Check for dirty repo
for /f "delims=" %%a in ('git status --short') do set OUTPUT=%%a
if not "%OUTPUT%"=="" (
    powershell -NonInteractive -NoProfile -Command "Write-Host `n*** Files are changed. Git commit id may be incorrect ***`n -foregroundcolor yellow"
)
goto :eof

:SetupEnv
call :DoCall ".\Script\SetupVsDevEnv.cmd" "Setup Visual Studio developement environment"
goto :eof

:RestorePackages
call :DoCall ".\Script\RestorePackages.cmd" "Restore Nuget packages"
goto :eof

:Build
call :DoCall ".\Script\Build.cmd Debug" "Build Debug configurations"
call :DoCall ".\Script\Build.cmd Release" "Build Release configurations"
goto :eof

:RunUnitTests
call :DoCall ".\Script\RunUnitTests.cmd Debug" "Run Debug unit tests"
call :DoCall ".\Script\RunUnitTests.cmd Release" "Run Release unit tests"
goto :eof

:RunIntegrationTests
call :DoCall ".\Script\RunIntegrationTests.cmd Debug" "Run Debug integration tests"
call :DoCall ".\Script\RunIntegrationTests.cmd Release" "Run Release integration tests"
goto :eof

:ResharperCheck
call :DoCall ".\Script\ResharperCheck.cmd" "Resharper check of source code"
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
