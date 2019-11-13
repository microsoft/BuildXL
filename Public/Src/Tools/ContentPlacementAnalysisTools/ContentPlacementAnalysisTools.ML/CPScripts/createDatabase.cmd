@echo off
@REM This script is used to create a database given a downloaded set of results

@REM Build the command
set EXE=..\cptools.ml.consolidate
@REM you need the configutaion file
set CONF=/ac:%1
@REM an existing input directory
set IDIR=/id:%2
@REM and an exisiting output directory
set ODIR=/od:%3
set COMM=%EXE% %CONF% %IDIR% %ODIR%
@REM and execute
echo Using configuration at [%1]
echo Building database with directory=[%2]
echo Saving results to [%3]
@REM specify the queue data flag and the machine map flag
%COMM%