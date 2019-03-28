@echo off
setlocal
SETLOCAL ENABLEEXTENSIONS
SETLOCAL ENABLEDELAYEDEXPANSION

REM -SharedCacheMode ConsumeAndPublish

echo Performing build with LKG to populate shared cache
echo .

	REM Perform initial build schedule-only build with graph caching disabled to publish nuget packages
	call %~dp0\..\..\bxl.cmd /q:Release /q:Debug /q:ReleaseNet461 /q:DebugNet461 /q:DebugDotNetCore /q:ReleaseDotNetCore /exp:incrementalscheduling- /cacheGraph- /forcePopulatePackageCache -All -SharedCacheMode ConsumeAndPublish /phase:Schedule
	if %ERRORLEVEL% NEQ 0 (
		echo. 1>&2
		echo --------------------------------------------------------------- 1>&2
		echo - BuildXL failed to populate shared cache:  1>&2
		echo - ERRORLEVEL:%ERRORLEVEL% 1>&2
		echo --------------------------------------------------------------- 1>&2
		echo. 1>&2
		exit /b 1
	)

	REM Clear the build version environment variable so it matches the value on developer machines
	SET TF_BUILD_SOURCEGETVERSION_OLD=%TF_BUILD_SOURCEGETVERSION%
	SET TF_BUILD_SOURCEGETVERSION=
	SET TF_BUILD_BUILDNUMBER_OLD=%TF_BUILD_BUILDNUMBER%
	SET TF_BUILD_BUILDNUMBER=
	
	REM Perform actual build to cache pips
	call %~dp0\..\..\bxl.cmd /q:Release /q:Debug /q:ReleaseNet461 /q:DebugNet461 /q:DebugDotNetCore /q:ReleaseDotNetCore /exp:incrementalscheduling- -All -SharedCacheMode ConsumeAndPublish /logsDirectory:Out\Logs\PopulateSharedCache
	if %ERRORLEVEL% NEQ 0 (
		echo. 1>&2
		echo --------------------------------------------------------------- 1>&2
		echo - BuildXL failed to populate shared cache:  1>&2
		echo - ERRORLEVEL:%ERRORLEVEL% 1>&2
		echo --------------------------------------------------------------- 1>&2
		echo. 1>&2
		exit /b 1
	)

	REM Reset the variable in case anything else gets added to this script that might need it
	SET TF_BUILD_SOURCEGETVERSION = %TF_BUILD_SOURCEGETVERSION_OLD%
	SET TF_BUILD_BUILDNUMBER=%TF_BUILD_BUILDNUMBER_OLD%
	exit /b 0