@ECHO OFF
SETLOCAL

REM Enumerates all the .txt files in a directory, appends its contents to a single output file.
REM Parameters:
REM %1 Input directory1 to enumerate files from.
REM %2 Input directory2 to enumerate files from.
REM %3 Output file
REM %4 File with the expected outputs, should match %3 contents.

SET InputDirectory1=%1
SET InputDirectory2=%2
SET OutputFile=%3
SET ExpectedOutputFile=%4

ECHO. > %OutputFile%
FOR /F %%f IN ('dir /b /s "%InputDirectory1%\*.txt"') DO type %%f >> %OutputFile%

REM Compares the content of output file and expected output file, ignore the actual diff, since we don't care for this test.
fc %OutputFile% %ExpectedOutputFile% > NUL
IF ERRORLEVEL 1 GOTO error

REM Empty output file.
ECHO. > %OutputFile%
FOR /F %%f IN ('dir /b /s "%InputDirectory2%\*.txt"') DO type %%f >> %OutputFile%
fc %OutputFile% %ExpectedOutputFile% > NUL
IF ERRORLEVEL 1 GOTO error

ENDLOCAL && EXIT /b 0
:error
ENDLOCAL && EXIT /b 1