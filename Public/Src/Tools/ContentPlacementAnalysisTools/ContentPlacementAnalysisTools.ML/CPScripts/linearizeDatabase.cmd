@echo off
@REM This script is used to create a database given a downloaded set of results

@REM Build the command
set EXE=..\cptools.ml.consolidate
@REM you need the configutaion file
set CONF=/ac:%1
@REM an existing input directory
set IDIR=/id:%2
@REM the number of samples
set NS=/ns:%3
@REM the size of samples
set SS=/ss:%4
set COMM=%EXE% %CONF% %IDIR% %NS% %SS%
@REM and execute
echo Using configuration at [%1]
echo Building database with directory=[%2]
echo Getting %3 samples of size %4
echo Saving results to [%2]
@REM specify the queue data flag and the machine map flag
%COMM% /lo