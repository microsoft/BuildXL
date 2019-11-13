@echo off
@REM This script is used to download monthly queue data from kusto. It will query kusto using
@REM CPResources/Query/get_monthly_queue_data.kql to get a csv with the queue similarity numbers and it will create
@REM a folder (at the output folder) called QueueMap that will contain the sorted distances for each queue (queue to queue) and
@REM a folder called MachineMap that contains the sorted by frequency machines for each queue. The logging for this
@REM process is stored at cptools.builddownloader.exe.log in the same directory as the executable. This file includes debug level
@REM and its cleaned at each run 


@REM Build the command
set EXE=..\cptools.builddownloader
@REM you need the configutaion file
set CONF=/ac:%1
@REM a year (numeric)
set YEAR=/y:%2
@REM a month (numeric (1-12))
set MONTH=/m:%3
@REM and an exisiting output directory
set ODIR=/od:%4
set COMM=%EXE% %CONF% %YEAR% %MONTH% %ODIR%
@REM and execute
echo Using configuration at [%1]
echo Downloading queue data for year=%2, month=%3
echo Saving results to [%4]
@REM specify the queue data flag and the machine map flag
%COMM% /qdo /imm
