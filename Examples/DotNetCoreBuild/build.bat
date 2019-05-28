@echo off
setlocal
SETLOCAL ENABLEEXTENSIONS
SETLOCAL ENABLEDELAYEDEXPANSION

if "%BUILDXL_BIN%" EQU "" (
    echo [error] BUILDXL_BIN not set.  Please set it to a BuildXL deployment folder
    exit /b 1
)

if exist %~dp0sdk\Sdk.Prelude (
    rd /Q /S %~dp0sdk\Sdk.Prelude
)
mklink /D /J %~dp0sdk\Sdk.Prelude %BUILDXL_BIN%\Sdk\Sdk.Prelude

if exist %~dp0sdk\Sdk.Transformers (
    rd /Q /S %~dp0sdk\Sdk.Transformers
)
mklink /D /J %~dp0sdk\Sdk.Transformers %BUILDXL_BIN%\Sdk\Sdk.Transformers

set buildCmd=%BUILDXL_BIN%\bxl.exe /server- /cacheGraph- /remoteTelemetry+ /nowarn:0909,2840 /c:%~dp0config.dsc
echo Executing: %buildCmd%
%buildCmd%
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

rd /Q /S %~dp0sdk\Sdk.Prelude
rd /Q /S %~dp0sdk\Sdk.Transformers