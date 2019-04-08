@echo off

REM This script ensures that the current LKG as defined in BuildXLLkgVersion.cmd is downloaded to the machine
REM
REM It communicates this via the environment variable:
REM 	BUILDXL_LKG (path to the LKG package pulled with nuget)
REM 
REM This script will also prep the cache and set the environment variable BUILDXL_CACHE_DIRECTORY and BUILDXL_CACHE_IS_ENLISTMENT_LOCAL
REM     BUILDXL_CACHE_DIRECTORY (location of the enlistment or machine wide cache)
REM     BUILDXL_CACHE_IS_ENLISTMENT_LOCAL (1 or 0; indicates if the cache is enlistment local, so zero means 'machine-wide')
REM If you have multiple enlistments it is recommeded to set the BUILDXL_CACHE_DIRECTORY as a global environment variable to a folder on the 
REM drive where you have your enlistments so all of them can share the same local cache.
REM
REM This script tries to be fast and incremental by checking a hash value of some of the files via the environment variables:
REM     _BUILDXL_INIT_DONE (init ran successfully)
REM     _BUILDXL_INIT_HASH (hash of relevant files, used to detect when environment init should be rerun)

call :SetFQN ENLISTMENTROOT=%~dp0..\..
set TOOLROOT=%ENLISTMENTROOT%\Shared\Tools
set SCRIPTROOT=%ENLISTMENTROOT%\Shared\Scripts

REM *********************************
REM CheckForShortCut
REM *********************************
:InternalVsExternalCheck
	REM Determine if we are internal or external.
	call %SCRIPTROOT%\BuildXLIsMicrosoftInternal.cmd %*
	if ERRORLEVEL 1 (
		echo ERROR: Failed to determine if this build is internal or external
		exit /b 1
	)

REM *********************************
REM CheckForShortCut
REM *********************************
:CheckForShortCut
	CALL :CreateInitHash _BUILDXL_INIT_HASH_NEW
	IF DEFINED _BUILDXL_INIT_HASH (
		IF "%_BUILDXL_INIT_HASH_NEW%" == "%_BUILDXL_INIT_HASH%" (
			GOTO :InitComplete
		)
	)
	SET _BUILDXL_INIT_HASH=
	SET _BUILDXL_INIT_DONE=

	CALL :Init
	if ERRORLEVEL 1 (
		echo ERROR: Failed to initialize
		exit /b 1
	)

	CALL :SetCacheState
	if ERRORLEVEL 1 (
		echo ERROR: Failed set cache state
		exit /b 1
	)

	CALL :SetExportedVariables
	if ERRORLEVEL 1 (
		echo ERROR: Failed to Set export variables
		exit /b 1
	)

REM *********************************
REM InitComplete
REM *********************************
:InitComplete
	SET _BUILDXL_INIT_HASH_NEW=
	SET _BUILDXL_INIT_DONE=1
	exit /b 0



REM *********************************
REM Init
REM *********************************
:Init
	REM Get the current version
	if "%[Sdk.BuildXL]microsoftInternal%" == "1" (
		call %SCRIPTROOT%\BuildXLLkgVersion.cmd
	) else (
		call %SCRIPTROOT%\BuildXLLkgVersionPublic.cmd
	)
	if ERRORLEVEL 1 (
		echo ERROR: Failed to determine lkg version
		exit /b 1
	)

	set _BUILDXL_BOOTSTRAP_OUT=%ENLISTMENTROOT%\Out\BootStrap

	REM use nuget to pull the current LKG down
	echo BUILDXL-Init: Using nuget to Pull package '%BUILDXL_LKG_NAME%' version '%BUILDXL_LKG_VERSION%'
	%TOOLROOT%\nuget.exe install -OutputDirectory %_BUILDXL_BOOTSTRAP_OUT% -Source %BUILDXL_LKG_FEED_1% %BUILDXL_LKG_NAME% -Version %BUILDXL_LKG_VERSION%
	if ERRORLEVEL 1 (
		echo ERROR: Failed to pull nuget package
		exit /b 1
	)

	goto :EOF

REM *********************************
REM SetExportedVariables
REM *********************************
:SetExportedVariables
	set BUILDXL_LKG=%_BUILDXL_BOOTSTRAP_OUT%\%BUILDXL_LKG_NAME%.%BUILDXL_LKG_VERSION%
	set BUILDXL_LKG_NAME=
	set BUILDXL_LKG_VERSION=
	set BUILDXL_LKG_FEED_1=
	set _BUILDXL_INIT_HASH=%_BUILDXL_INIT_HASH_NEW%
	
	REM We'll conditionally set the credential provider if not set on the machine.
	REM If not set we will set it to the local one in the enlistment but iwth the b-drive substitution
	if NOT DEFINED NUGET_CREDENTIALPROVIDERS_PATH (
		set NUGET_CREDENTIALPROVIDERS_PATH=B:\Shared\Tools
	)
	goto :EOF



REM *********************************
REM CreateInitHash
REM
REM Computes the crc hash of all files in the folders that this
REM Script depends on like scriptroot, toolroot and the installed bits
REM *********************************
:CreateInitHash
	IF "%~1"=="" ECHO ERROR: %~nx0 %%1 must be a variable name in which to store the init hash& GOTO :EOF

	SET _hash=0
	FOR /F %%I IN ('%TOOLROOT%\crc.exe %SCRIPTROOT%')            DO SET /A _hash ^^= 0x%%I
	FOR /F %%I IN ('%TOOLROOT%\crc.exe %TOOLROOT%')              DO SET /A _hash ^^= 0x%%I
	SET "%~1=%_hash%-%~dp0-%[Sdk.BuildXL]microsoftInternal%"
	GOTO :EOF


REM *********************************
REM SetFQN
REM 
REM Simple function that sets the named variable (%1) to the fully
REM qualified path of %2.  This normalizes the path too, such as
REM handling the ".." syntax and removing any redundant path elements
REM just like calling GetFullPath(%2)
REM *********************************
:SetFQN
	set %1=%~f2
	exit /b 0


REM *********************************
REM SetCacheState
REM *********************************
:SetCacheState
	REM Legacy environment variable
	IF DEFINED DOMINO_CACHE_DIRECTORY (
		set BUILDXL_CACHE_DIRECTORY=%DOMINO_CACHE_DIRECTORY%
	)

	IF DEFINED DOMINO_CACHE_DIRECTORY (
		set DOMINO_CACHE_IS_ENLISTMENT_LOCAL=0
	) ELSE (
		set DOMINO_CACHE_IS_ENLISTMENT_LOCAL=1
		CALL %SCRIPTROOT%\SetDefaultCacheDirectory.cmd
	)

	GOTO :EOF
