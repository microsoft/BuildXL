@echo off
setlocal

set DropVersion=0.100.0-rc3146171
	
if NOT DEFINED DROP_EXE_LOCATION (
	%~dp0Shared\Tools\nuget.exe install -OutputDirectory %~dp0Out\SelfHost\Drop -Source "https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json" Drop.App -Version %DropVersion%
	set DROP_EXE_LOCATION=%~dp0Out\SelfHost\Drop\Drop.App.%DropVersion%\lib\net45
)

%DROP_EXE_LOCATION%\drop.exe %*

endlocal && exit /b 0
