@echo off
setlocal

set DropVersion=0.100.0-rc3146171

set DEFAULT_DROP_EXE_LOCATION=%~dp0Out\SelfHost\Drop\Drop.App.%DropVersion%\lib\net45

if EXIST %DEFAULT_DROP_EXE_LOCATION%\drop.exe (
	set DROP_EXE_LOCATION=%DEFAULT_DROP_EXE_LOCATION%
)

if NOT DEFINED DROP_EXE_LOCATION (
	REM Potentiall init.cmd has set the credential provider path to a mapped b-drive. Temporarilly undo this.
	set OLD_NUGET_CREDENTIALPROVIDERS_PATH=%NUGET_CREDENTIALPROVIDERS_PATH%
    set NUGET_CREDENTIALPROVIDERS_PATH=

	%~dp0Shared\Tools\nuget.exe install -OutputDirectory %~dp0Out\SelfHost\Drop -Source "https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json" Drop.App -Version %DropVersion%
	set DROP_EXE_LOCATION=%DEFAULT_DROP_EXE_LOCATION%
	
	REM Restore credential provider path.
	set NUGET_CREDENTIALPROVIDERS_PATH=%OLD_NUGET_CREDENTIALPROVIDERS_PATH%
)

%DROP_EXE_LOCATION%\drop.exe %*

endlocal && exit /b 0
