@echo off
setlocal
set RepoRoot=%~dp0\..\..
SET INETROOT=%RepoRoot%
set Object_ROOT=%RepoRoot%
SET BUILD_COREXT=1

IF NOT DEFINED QCLIENT_CACHE_DIRECTORY (
echo finding quickbuild in path.
   for /F %%I IN ('dir /s/b %localappdata%\cloudbuild\client\*quickbuild.exe') do (
        echo Using quickbuild at %%I
        set QCLIENT_PATH=%%I
        goto :callquickbuild
   )
   
   goto :error
) ELSE (
  set QCLIENT_PATH=%QCLIENT_CACHE_DIRECTORY%\quickbuild.exe
)

:callquickbuild
CALL %QCLIENT_PATH% buddy -a %*
goto :eof

:error
echo Unable to locate QuickBuild.

:eof
endlocal