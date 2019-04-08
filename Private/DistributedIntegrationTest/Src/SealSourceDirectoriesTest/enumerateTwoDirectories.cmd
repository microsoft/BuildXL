@ECHO OFF
SETLOCAL

REM Enumerates all the .txt files in a directory, appends its contents to a single output file.
REM Parameters:
REM %1 Input directory1 to enumerate files from.
REM %2 Input directory2 to enumerate files from.
REM %3 Output file

SET InputDirectory1=%1
SET InputDirectory2=%2
SET OutputFile=%3

ECHO. > %OutputFile%
FOR /F %%f IN ('dir /b /s "%InputDirectory1%\*.txt"') DO type %%f >> %OutputFile%

FOR /F %%f IN ('dir /b /s "%InputDirectory2%\*.txt"') DO type %%f >> %OutputFile%

ENDLOCAL && EXIT /b 0