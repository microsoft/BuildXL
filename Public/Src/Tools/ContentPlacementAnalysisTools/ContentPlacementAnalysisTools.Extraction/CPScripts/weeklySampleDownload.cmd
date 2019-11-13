@echo off
@REM This script is used to download and sample artifacts from a week, using CPResources\Query\get_build_data.kql.
@REM That query downloads data for a single day, so here we will need to invoke it using a start day (numeric)
@REM and a span (number of days since the start day, inclusive). In this script, the span is fixed to 7


@REM Build the command
set EXE=..\cptools.builddownloader
@REM you need the configutaion file
set CONF=/ac:%1
@REM a year (numeric)
set YEAR=/y:%2
@REM a month (numeric (1-12))
set MONTH=/m:%3
@REM a day (numeric (1-31))
set DAY=/d:%4
@REM a number of builds
set NUMBUILDS=/nb:%5
@REM and an exisiting output directory
set ODIR=/od:%6
@REM a span (numeric (1-31))
set SPAN=/sp:7
set COMM= %EXE% %CONF% %YEAR% %MONTH% %DAY% %NUMBUILDS% %SPAN% %ODIR%
@REM and execute
echo Using configuration at [%1]
echo Downloading nb=%5 builds for year=%2, month=%3, day=%4 with a span of 7 days 
echo Saving results to [%6]
@REM no flags here
%COMM%
