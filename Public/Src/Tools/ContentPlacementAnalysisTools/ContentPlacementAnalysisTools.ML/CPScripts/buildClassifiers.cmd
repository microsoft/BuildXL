@echo off
@REM This script is used to create a database given a downloaded set of results

@REM Build the command
set EXE=..\cptools.ml.consolidate
@REM you need the configutaion file
set CONF=/ac:%1
@REM an existing input directory
set IDIR=/id:%2
set COMM=%EXE% %CONF% %IDIR%
@REM and execute
echo Using configuration at [%1]
echo Building classifiers with directory=[%2]
echo Saving results to [%2]
@REM done
%COMM% /brfo