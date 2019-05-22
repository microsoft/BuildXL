@ECHO OFF
SETLOCAL
SETLOCAL ENABLEDELAYEDEXPANSION

REM Description:
REM     Uses the Xunit console to run tests that call the BuildXL executable directly
REM
REM Prerequisities:
REM     Run "bxl.cmd -DeployStandaloneTest /q:<qualifier> Out\bin\tests\standalonetest\*" to build and deploy test dlls.
REM     The executable tests are deployed separately from the StandaloneTest deployment (see StandaloneTest.dsc),
REM     but re-use the tools deployed alongside the standalone tests (see StadaloneTestSupport.dsc) to execute the executable tests using Xunit.
REM
REM Usage:
REM     RunExecutableTests.cmd /configuration <release|debug> /framework <net472|win-x64|etc..>
REM
REM Example:
REM     RunExecutableTests.cmd /configuration release /framework net472

SET TEST_CONFIGURATION=
SET TEST_FRAMEWORK=

CALL :ParseCommandLine %*

IF "%TEST_CONFIGURATION%" == "" (
    ECHO Must specify /configuration ^<release^|debug^>
    EXIT /b 1
)

IF "%TEST_FRAMEWORK%" == "" (
    ECHO Must specify /framework ^<net472^|win-x64^|etc..^>
    EXIT /b 1
)

if NOT DEFINED STANDALONETEST_DIR (
    SET STANDALONETEST_DIR=%~dp0
)

SET TEST_BIN=%STANDALONETEST_DIR%executabletests\%TEST_CONFIGURATION%\%TEST_FRAMEWORK%\IntegrationTest.BuildXL.Executable
SET TEST_DLL=%TEST_BIN%\IntegrationTest.BuildXL.Executable.dll

IF "%TEST_FRAMEWORK%"=="win-x64" (
    SET XUNIT_DLL=%TEST_BIN%\xunit.console.dll
    SET DOTNET=%STANDALONETEST_DIR%tools\NETCoreSDK.2.2.0-preview3\dotnet.exe

    !DOTNET! !XUNIT_DLL! %TEST_DLL% -parallel none
) ELSE (
    SET XUNIT_EXE=%STANDALONETEST_DIR%tools\xunit.runner.console.2.4.1\tools\net461\xunit.console.exe
    !XUNIT_EXE! %TEST_DLL% -parallel none
)

if %ERRORLEVEL% NEQ 0 (
    ENDLOCAL && EXIT /b 1
)

:ParseCommandLine
    IF /I "%1" == "/configuration" (
        SET TEST_CONFIGURATION=%2
        SHIFT
        SHIFT
    )

    IF /I "%1" == "/framework" (
        SET TEST_FRAMEWORK=%2
        SHIFT
        SHIFT
    )

    IF "%1" NEQ "" (
        ECHO Unrecognized argument: %1 1>&2
        EXIT /b 1
    )