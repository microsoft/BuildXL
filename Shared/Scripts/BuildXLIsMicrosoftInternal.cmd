@echo off

REM @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
REM 
REM Check if the value is set in arguments
REM
REM @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@

:argLoop
    if "%~1" == "/p:[Sdk.BuildXL]microsoftInternal=1" ( 
        set [Sdk.BuildXL]microsoftInternal=1
    ) else if "%~1" == "/p:[Sdk.BuildXL]microsoftInternal=0" ( 
        set [Sdk.BuildXL]microsoftInternal=0
    ) else if "%~1" == "/p:[Sdk.BuildXL]microsoftInternal=true" ( 
        set [Sdk.BuildXL]microsoftInternal=1
    ) else if "%~1" == "/p:[Sdk.BuildXL]microsoftInternal=false" ( 
        set [Sdk.BuildXL]microsoftInternal=0
    ) else if "%~1" == "/p:[Sdk.BuildXL]microsoftInternal" ( 
        REM Commandline processing split it on equals, the value is in the next arg

        if "%~2" == "1" ( 
            set [Sdk.BuildXL]microsoftInternal=1
        ) else if "%~2" == "0" ( 
            set [Sdk.BuildXL]microsoftInternal=0
        ) else if "%~2" == "true" ( 
            set [Sdk.BuildXL]microsoftInternal=1
        ) else if "%~2" == "false" ( 
            set [Sdk.BuildXL]microsoftInternal=0
        ) else if not "%~2" == "" ( 
            echo ERROR: Unexpected value for parameter '/p:[Sdk.BuildXL]microsoftInternal=%2'
            exit /b 1
        )
        shift
    )

    shift
if not "%~1"=="" goto argLoop

if not "%[Sdk.BuildXL]microsoftInternal%" == "" (
    REM Succesfully extracted value from arguments
    exit /b 0
)

REM @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
REM
REM Check if the environment variable is already set
REM
REM @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@

if "%[Sdk.BuildXL]microsoftInternal%" == "1" ( 
    exit /b 0 
)
if "%[Sdk.BuildXL]microsoftInternal%" == "0" ( 
    exit /b 0 
)
if "%[Sdk.BuildXL]microsoftInternal%" == "true" ( 
    REM standardize on 1 or 0
    set [Sdk.BuildXL]microsoftInternal=1
    exit /b 0 
)
if "%[Sdk.BuildXL]microsoftInternal%" == "false" ( 
    REM standardize on 1 or 0
    set [Sdk.BuildXL]microsoftInternal=0
    exit /b 0 
)
if not "%[Sdk.BuildXL]microsoftInternal%" == "" ( 
    echo ERROR: Unexpected value for environment variable '[Sdk.BuildXL]microsoftInternal'
    exit /b 1
)

REM @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
REM
REM Last resort if not specified to check the domain. 
REM If you are not internal to microsoft and your domain matches one of the domains here, 
REM you can set the environment varaible on your machine explicitly or pass it on the commandline when you build.
REM
REM @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@

if "%USERDOMAIN%" == "REDMOND" (
    set [Sdk.BuildXL]microsoftInternal=1
) 
if "%USERDOMAIN%" == "NORTHAMERICA" (
    set [Sdk.BuildXL]microsoftInternal=1
)
if "%USERDOMAIN%" == "EUROPE" (
    set [Sdk.BuildXL]microsoftInternal=1
)
if "%USERDOMAIN%" == "NTDEV" (
    set [Sdk.BuildXL]microsoftInternal=1
)
