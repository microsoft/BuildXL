@ECHO OFF

IF "%FAILME%" == "1" GOTO ERROR 

type %1 > %2

ENDLOCAL && EXIT /b 0
:ERROR
ENDLOCAL && EXIT /b 1