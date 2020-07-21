@echo off
setlocal

set DEFAULT_DROP_EXE_LOCATION=%~dp0Out\SelfHost\Drop.App\lib\net45

if EXIST %DEFAULT_DROP_EXE_LOCATION%\drop.exe (
	set DROP_EXE_LOCATION=%DEFAULT_DROP_EXE_LOCATION%
)

if NOT DEFINED DROP_EXE_LOCATION (
	REM Potentiall init.cmd has set the credential provider path to a mapped b-drive. Temporarilly undo this.
	set OLD_NUGET_CREDENTIALPROVIDERS_PATH=%NUGET_CREDENTIALPROVIDERS_PATH%
	set NUGET_CREDENTIALPROVIDERS_PATH=

	REM Delete any leftovers, e.g. the exe is missing but other files are still present
	rmdir /Q /S %~dp0Out\SelfHost\Drop.App

	REM Create the drop app folder, download the latest version and unzip it
	mkdir %~dp0Out\SelfHost\Drop.App
	curl "https://artifacts.dev.azure.com/cloudbuild/_apis/drop/client/exe" --output %~dp0Out\SelfHost\Drop.App.zip
	tar -xf %~dp0Out\SelfHost\Drop.App.zip -C %~dp0Out\SelfHost\Drop.App
	del %~dp0Out\SelfHost\Drop.App.zip

	set DROP_EXE_LOCATION=%DEFAULT_DROP_EXE_LOCATION%
	
	REM Restore credential provider path.
	set NUGET_CREDENTIALPROVIDERS_PATH=%OLD_NUGET_CREDENTIALPROVIDERS_PATH%
)

%DROP_EXE_LOCATION%\drop.exe %*

endlocal && exit /b 0
