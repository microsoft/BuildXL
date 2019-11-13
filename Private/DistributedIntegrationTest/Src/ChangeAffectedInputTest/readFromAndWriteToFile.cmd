@ECHO OFF
SETLOCAL

REM Read from input file and write the content to output file
REM Parameters:
REM %1 Input file.
REM %2 Output file .

SET InputFile=%1
SET OutputFile=%2

type %InputFile% > %OutputFile%

ENDLOCAL && EXIT /b 0