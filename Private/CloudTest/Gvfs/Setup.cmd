mkdir c:\temp

set GitVersion=2.22.0.vfs.1.1.57.gbaf16c8
set GvfsVersion=1.0.19224.1

echo Dumping env vars
set

echo Checking Admin
powershell ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

REM Install DotNetCore
Echo Download and install dotnet core
powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -channel LTS -installdir c:\dotnet"


call :InstallApp Git "https://github.com/microsoft/VFSForGit/releases/download/v%GvfsVersion%/Git-%GitVersion%-64-bit.exe" "%ProgramFiles%\git\cmd\git.exe"
if ERRORLEVEL 1 (
    exit /b 1
)

call :InstallApp Gvfs "https://github.com/microsoft/VFSForGit/releases/download/v%GvfsVersion%/SetupGVFS.%GvfsVersion%.exe" "%ProgramFiles%\gvfs\gvfs.exe"
if ERRORLEVEL 1 (
    exit /b 1
)

goto :Done

:InstallApp
    set Name=%1
    set DownloadUrl=%2
    set InstalledExe=%3


    Echo Downloading %Name%
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%DownloadUrl%' -OutFile c:\temp\setup%Name%.exe"
    Echo Installing %Name%
    c:\temp\setup%Name%.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /SP- /LOG
    Echo Waiting until %Name% is installed
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "Start-Sleep -Seconds 120"
    %InstalledExe% --version
    if ERRORLEVEL 1 (
        echo Did not find %Name%. Install must have failed
        echo.
        echo dir %ProgramFiles% /s/b
        echo.
        exit /b 1
    )
    goto :EOF

:Done
    echo Done
    exit /b 0