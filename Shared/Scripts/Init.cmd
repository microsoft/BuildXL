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

	REM Install latest Azure Artifacts Credentials Provider (https://github.com/microsoft/artifacts-credprovider)
	powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; iex ""& { $(irm https://aka.ms/install-artifacts-credprovider.ps1) } -AddNetfx"""

	set _BUILDXL_BOOTSTRAP_OUT=%ENLISTMENTROOT%\Out\BootStrap

	SETLOCAL

	REM If not on ADO, don't use the NuGet cache when downloading the LKG
	if "%TF_BUILD%" == "" (
		set DIRECTDOWNLOAD="-DirectDownload"
	)

	REM use nuget to pull the current LKG down
	echo BUILDXL-Init: Using nuget to Pull package '%BUILDXL_LKG_NAME%' version '%BUILDXL_LKG_VERSION%'
	%TOOLROOT%\nuget.exe install %DIRECTDOWNLOAD% -OutputDirectory %_BUILDXL_BOOTSTRAP_OUT% -Source %BUILDXL_LKG_FEED_1% %BUILDXL_LKG_NAME% -Version %BUILDXL_LKG_VERSION% %BUILDXL_NUGET_WORKAROUNDS%
	if ERRORLEVEL 1 (
		echo ERROR: Failed to pull nuget package
		call :DiagnoseNugetLongPathFailure "%_BUILDXL_BOOTSTRAP_OUT%"
		exit /b 1
	)

	ENDLOCAL

	goto :EOF

REM *********************************
REM DiagnoseNugetLongPathFailure
REM
REM Best-effort diagnostic that runs after a nuget install failure. nuget.exe (and the .NET
REM APIs it uses) is not long-path aware, so when this enlistment is checked out at a long
REM path and Windows long path support (LongPathsEnabled) is not turned on, nuget's install/
REM extract step can fail with a confusing generic error such as:
REM     Could not find a part of the path '...\Out\BootStrap\...\some\deeply\nested\file.dll'
REM which never mentions MAX_PATH or long paths. This routine tries to detect that risk
REM condition and, when likely, prints a clear explanation and the exact fix. It never masks
REM or changes the original failure/exit code; it only adds extra diagnostic output.
REM
REM %1 - the output/bootstrap directory path used for the nuget install (used to estimate
REM      how much room is left before hitting the 260 character MAX_PATH limit)
REM *********************************
:DiagnoseNugetLongPathFailure
	SETLOCAL ENABLEDELAYEDEXPANSION

	set "_LP_PATH=%~1"
	if "%_LP_PATH%" == "" set "_LP_PATH=%ENLISTMENTROOT%"

	call :StrLen "%_LP_PATH%" _LP_PATHLEN

	REM Query the registry value that controls Windows long path support. Do this defensively:
	REM on some machines/locales 'reg query' may fail or format output unexpectedly, and that
	REM must never be treated as a script error - just as "could not confirm long paths are on".
	set "_LP_ENABLED="
	for /f "tokens=3" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled 2^>nul ^| findstr /i "LongPathsEnabled"') do set "_LP_ENABLED=%%A"

	REM Treat long paths as "at risk" unless we positively confirmed the registry value is 1 (0x1).
	set "_LP_RISK=1"
	if "%_LP_ENABLED%" == "0x1" (
		if %_LP_PATHLEN% LSS 150 set "_LP_RISK=0"
	)

	echo(
	if "%_LP_RISK%" == "1" (
		echo ERROR: This can happen when the repository is checked out at a long path and
		echo        Windows long path support is disabled ^(or could not be confirmed as enabled^).
		echo        The bootstrap output path is %_LP_PATHLEN% characters long:
		echo            %_LP_PATH%
		echo        nuget.exe and the .NET APIs it uses are not long-path aware, so instead of a
		echo        clear MAX_PATH error you may just see a generic "Could not find a part of the
		echo        path" error above.
		echo(
		echo        FIX: Run the following command as Administrator ^(in an elevated cmd.exe^),
		echo        then re-run the build:
		echo(
		echo            reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled /t REG_DWORD /d 1 /f
	) else (
		echo NOTE: If the error above mentions a path that could not be found, it can still be a
		echo       Windows MAX_PATH ^(260 character^) limitation even though long path support
		echo       appears to be enabled. Consider checking out the repository at a shorter path,
		echo       and double check that 'HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled'
		echo       is set to 1.
	)
	echo(

	ENDLOCAL
	exit /b 0

REM *********************************
REM StrLen
REM
REM Sets the variable named by %2 to the length of the string passed as %1.
REM Usage: call :StrLen "some string" ResultVarName
REM *********************************
:StrLen
	SETLOCAL ENABLEDELAYEDEXPANSION
	set "_SL_STR=%~1"
	set /a _SL_LEN=0
	:StrLenLoop
	if defined _SL_STR (
		set "_SL_STR=%_SL_STR:~1%"
		set /a _SL_LEN+=1
		goto :StrLenLoop
	)
	ENDLOCAL & set "%~2=%_SL_LEN%"
	exit /b 0

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
	REM The location below is where the powershell script for the artifacts credential provider places the binaries
	if NOT DEFINED NUGET_CREDENTIALPROVIDERS_PATH (
		ECHO NUGET_CREDENTIALPROVIDERS_PATH not set. Setting it to %USERPROFILE%\.nuget\plugins\netfx\CredentialProvider.Microsoft\
		set NUGET_CREDENTIALPROVIDERS_PATH=%USERPROFILE%\.nuget\plugins\netfx\CredentialProvider.Microsoft\
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
	REM Change this salt whenever the init script has to be forced to re-run
	SET _salt=1

	SET _hash ^^= 0x$%_salt%
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
