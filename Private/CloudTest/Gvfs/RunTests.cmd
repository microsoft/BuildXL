@ECHO OFF

REM ==============================================================================
REM This script is not used by CloudTest in any way. 
REM
REM Test execution step for CloudTest is specified in BuildXL.CloudTest.Gvfs.JobGroup.xml
REM 
REM This script is mainly useful for executing tests locally, especially when
REM the tests require admin privileges.
REM ==============================================================================

dotnet %~dp0\xunit.console.dll %~dp0\BuildXL.CloudTest.Gvfs.dll -parallel none -noshadow -noappdomain -diagnostics -verbose -xml %~dp0\xunit.results.xml %*