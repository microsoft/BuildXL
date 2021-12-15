@ECHO OFF

REM Builds using bxl.exe hooked up to AnyBuild for remoting
REM
REM Usage:
REM     bxlremote.cmd <passthrough-command-line-options>
REM where
REM - <passthrough-command-line-options>: Additional options to pass through to bxl.cmd.
REM
REM Example:
REM     bxlremote.cmd -deploy dev -minimal
REM
REM Prerequesites:
REM     Requires a debug built AnyBuild repository as a sibling of the current repo root.

SETLOCAL

set _AnyBuildDistrib=%~dp0..\AnyBuild\distrib\Debug
set Passthrough_Args=%*
set _ClusterId=07F427C5-7979-415C-B6D9-01BAD5118191


REM real cluster mode
REM To use loopback mode, replace --RemoveExecServiceUri for --loopback
%_AnyBuildDistrib%\AnyBuild\AnyBuild.exe --JsonConfigOverrides ProcessSubstitution.MaxParallelLocalExecutionsFactor=0 Run.DisableDirectoryMetadataDedup=true --remoteAll -v --DoNotUseMachineUtilizationForScheduling -w --NoSandboxingBuildEngine --CacheDir out\AnyBuildCache --LogDir out\AnyBuildLogs --DisableActionCache --RemoteExecServiceUri https://westus2.anybuild-test.microsoft.com/clusters/%_ClusterId%  -- bxl.cmd /enableProcessRemoting+ /enableLazyOutputs- -nosubst /maxprocmultiplier:0.75 /numRemoteLeases:100 /p:[Sdk.BuildXL]useManagedSharedCompilation=false %Passthrough_Args%

if %ERRORLEVEL% NEQ 0 (
    ENDLOCAL && EXIT /b 1
)

ENDLOCAL && EXIT /b 0