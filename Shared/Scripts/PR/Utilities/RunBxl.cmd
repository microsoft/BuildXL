REM @echo off
setlocal

call %ENLISTMENTROOT%\Bxl.cmd %*
if %ERRORLEVEL% NEQ 0 (
    echo. 1>&2
    echo --------------------------------------------------------------- 1>&2
    echo - Failed BuildXL invocation:  1>&2
    echo -    %ENLISTMENTROOT%\Bxl.cmd %* 1>&2
    echo - ERRORLEVEL:%ERRORLEVEL% 1>&2
    echo --------------------------------------------------------------- 1>&2
    echo. 1>&2
    endlocal && exit /b 1
)
endlocal && exit /b 0