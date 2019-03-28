@ECHO OFF
SETLOCAL

REM Enumerates all the .txt files in a directory, appends its contents to a single output file.
REM Parameters:
REM %1 Input directory to enumerate files from.
REM %2 Output file
REM %3 File with the expected outputs, should match %2 contents.

SET InputDirectory=%1
SET OutputFile=%2
SET ExpectedOutputFile=%3

ECHO. > %OutputFile%
FOR /F %%f IN ('dir /b /s "%InputDirectory%\*.txt"') DO type %%f >> %OutputFile%

REM Compares the content of output file and expected output file, ignore the actual diff, since we don't care for this test.
fc %OutputFile% %ExpectedOutputFile% > NUL
IF ERRORLEVEL 1 GOTO error
ENDLOCAL && EXIT /b 0
:error
ENDLOCAL && EXIT /b 1