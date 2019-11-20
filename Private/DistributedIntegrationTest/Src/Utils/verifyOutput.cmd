@ECHO OFF
SETLOCAL

REM Compare expected output with the actual one.
REM Parameters:
REM %1 Expected output.
REM %2 Actual output.
REM %3 Result.

SET ExpectedOutput=%1
SET ActualOutput=%2
SET Result=%3

fc %ExpectedOutput% %ActualOutput% > %Result%
IF ERRORLEVEL 1 GOTO error
ENDLOCAL && EXIT /b 0
:error
ENDLOCAL && EXIT /b 1