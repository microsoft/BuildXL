@echo off
setlocal
set SCRIPTROOT=%~dp0
call :SetFQN ENLISTMENTROOT=%~dp0..\..

REM Setting the dev lkg version
for /f "tokens=2-4 delims=/ " %%a in ('date /t') do (set currentDate=%%c%%a%%b)
  For /f "tokens=1-3 delims=1234567890 " %%a in ("%time%") Do set "timedelims=%%a%%b%%c"
  For /f "tokens=1-4 delims=%timedelims%" %%a in ("%time%") Do (
    Set _hh=%%a
    Set _min=%%b
  )

REM This env var indicates which version and nupkg files the build will produce.
set TF_BUILD_BUILDNUMBER=0.0.1.%currentDate%-LkgBy%username%At%_hh%%_min%
set nugetExe=%ENLISTMENTROOT%\Out\CoreXtPkgs\VSOCredentialProviderBundle.3.3.1\NuGet.exe
set nupkg=%ENLISTMENTROOT%\Out\Bin\release_pkgs\Domino.%TF_BUILD_BUILDNUMBER%.nupkg
set targetConfig=%ENLISTMENTROOT%\.corext\corext.config

echo --------------------------------------------------
echo Updating BuildXL to use your LKG version: %TF_BUILD_BUILDNUMBER%
echo --------------------------------------------------


REM Ensure the corext config works is original
git checkout -- %targetConfig%

REM Check if Dev is availalbe for building
if NOT EXIST "%ENLISTMENTROOT%\Out\SelfHost\Dev\bxl.exe" (
	echo "ERROR: Failed to find the dev deployment, the LKG will be built with the dev deployment. If you don't need the dev deployment, why do you need to publish this LKG ?!?! :)"
	exit /b 1
)

REM cleanup
echo Cleaning all outputs to ensure we have a proper clean build
rmdir /s/q out\bin 2> NUL
rmdir /s/q out\objects 2> NUL

REM BUILD a release version of BuildXL using the dev deployment.
call "%ENLISTMENTROOT%\bxl.cmd" -use dev /q:Release /f:output='%nupkg%'
if ERRORLEVEL 1 (
	echo "ERROR: Failed to build BuildXL release"
	goto :Failed
)

REM Pushing to nuget
echo.
echo Pushing BuildXL package to VSO
"%nugetExe%" push -Source "https://pkgs.dev.azure.com/mseng/_packaging/Domino.Public.Experimental/nuget/v3/index.json" -ApiKey VSTS %nupkg%

REM Update CoreXt Config
echo.
echo Update CoreXt config file with ^<package id="Domino" version="%TF_BUILD_BUILDNUMBER%" /^>
set tmpConfig=%TEMP%\testconfig
if EXIST "%tmpConfig%" (
	del "%tmpConfig%"
)
for /F "delims=" %%l in (%targetConfig%) do (
	echo "%%l" | findstr /c:"package id=\"Domino\" version=" > NUL
	if ERRORLEVEL 1 (
		echo %%l>>"%tmpConfig%"
	) ELSE (
		echo     ^<package id="Domino" version="%TF_BUILD_BUILDNUMBER%" /^>>>"%tmpConfig%"
	)
)
copy "%tmpConfig%" "%targetConfig%" > NUL
if ERRORLEVEL 1 (
	echo "ERROR: Failed to copy %tmpConfig% to %targetConfig%"
)

echo.
echo.
echo Dev Lkg Update complete
echo.

exit /b 0


:SetFQN
	set %1=%~f2
	exit /b 0

:Failed
	exit /b 1