@echo off
setlocal
set FLAVOR=%1
if "%FLAVOR%"=="" set FLAVOR=Debug
pushd %~dp0..
call :Run
popd
exit /b 0

:Run
call :DoCall "..\bin\ContentStore\Test\%FLAVOR%\RunIntegrationTests.cmd" "Run %FLAVOR% ContentStore integration tests"
@REM call :DoCall "..\bin\ContentStore\GrpcTest\%FLAVOR%\RunIntegrationTests.cmd" "Run %FLAVOR% ContentStore GRPC integration tests"
call :DoCall "..\bin\MemoizationStore\Test\%FLAVOR%\RunIntegrationTests.cmd" "Run %FLAVOR% MemoizationStore integration tests"
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
