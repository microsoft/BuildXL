@ECHO OFF

REM ==============================================================================
REM This script is executed as part of CloudTest's "setup" step and its purpose is
REM to install all prerequisites necessary for running these GVFS tests.
REM
REM The BuildXL.CloudTest.Gvfs.JobGroup.xml configuration file is where this is
REM formally specified.
REM
REM Concretely, this script does the following:
REM   - enables ProjFS
REM   - downloads and installs a specified version of VFSForGit 
REM   - downloads and installs the corresponding version fo Git
REM   - downloads and installs a specified version of .NET Core SDK
REM   - clones a test repo (to be used in unit tests) both with git and with GVFS
REM ==============================================================================

mkdir C:\Temp

REM CODESYNC: TestBase.cs
set RepoParentDir=C:\Temp
set GitRepoName=BuildXL.Test
set GvfsRepoName=BuildXL.Test.Gvfs
set GitRepoLocation=%RepoParentDir%\%GitRepoName%
set GvfsRepoLocation=%RepoParentDir%\%GvfsRepoName%

REM https://github.com/microsoft/VFSForGit/releases/download/v1.0.20112.1/Git-2.26.2.vfs.1.1-64-bit.exe
REM https://github.com/microsoft/VFSForGit/releases/download/v1.0.20112.1/SetupGVFS.1.0.20112.1.exe

set RepoUrl=https://almili@dev.azure.com/almili/Public/_git/Public.BuildXL.Test.Gvfs

echo "==== Updating PATH"
set PATH=C:\Program Files\Git\cmd;C:\Program Files\GVFS;%PATH%

echo Dumping env vars
set

echo Checking Admin
powershell ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

echo === Enabling ProjFS
powershell -NoProfile -ExecutionPolicy unrestricted -Command "Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart"
if ERRORLEVEL 1 (
    echo "**** [WARNING] Could not enable Client-ProjFS; continuing hoping for the best"
)

REM Install DotNetCore
echo === Downloading and installing .NET Core
powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -channel 3.1 -installdir c:\dotnet"
if ERRORLEVEL 1 (
    echo "**** [WARNING] Could not install .NET Core; continuing hoping for the best"
)

call :InstallApp %~dp0..\GvfsInstallers\gvfs\SetupGVFS.0.3.20147.1.exe "%ProgramFiles%\gvfs\gvfs.exe"
if ERRORLEVEL 1 (
    echo "**** [ERROR] Failed to install GVFS"
    exit /b 1
)

call :DownloadAndInstallApp Git https://github.com/microsoft/VFSForGit/releases/download/v1.0.20112.1/Git-2.26.2.vfs.1.1-64-bit.exe "%ProgramFiles%\git\cmd\git.exe"
if ERRORLEVEL 1 (
    echo "**** [ERROR] Failed to install Git for GVFS"
    exit /b 1
)

echo === Configuring GVFS usn.updateDirectories option
"C:\Program Files\GVFS\GVFS.exe" config usn.updateDirectories true

echo === Cloning repository %RepoUrl% (both with git and gvfs) into C:\Temp\

C:
cd %RepoParentDir%
"C:\Program Files\Git\cmd\git.exe" clone %RepoUrl% %GitRepoName%
if ERRORLEVEL 1 (
    echo "**** [ERROR] Could not git clone repo %RepoUrl%"
    exit /b 1
)

"C:\Program Files\GVFS\GVFS.exe" clone %RepoUrl% %GvfsRepoName%
if ERRORLEVEL 1 (
    echo "**** [ERROR] Could not gvfs clone repo %RepoUrl%"
    exit /b 1
)

echo === Configuring git user name and email for %GitRepoLocation%
cd %GitRepoLocation%
git config user.name "BuildXL CloudTest"
git config user.email domdev@microsoft.com
git config --list

echo === Configuring git user name and email for %GitRepoLocation%
cd %GvfsRepoLocation%/src
git config user.name "BuildXL CloudTest"
git config user.email domdev@microsoft.com 
git config --list

goto :Done

:DownloadAndInstallApp
    set Name=%1
    set DownloadUrl=%2
    set InstalledExe=%3

    echo =====================================
    Echo == Downloading %Name% from %DownloadUrl%
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%DownloadUrl%' -OutFile C:\Temp\setup%Name%.exe"
    call :InstallApp C:\Temp\setup%Name%.exe %InstalledExe%
    goto :EOF

:InstallApp
    set Installer=%1
    set InstalledExe=%2

    Echo == Installing %Installer%
    %Installer% /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /SP- /LOG
    Echo == Waiting until %Installer% is installed
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "Start-Sleep -Seconds 120"
    %InstalledExe% --version
    if ERRORLEVEL 1 (
        echo Did not find %InstalledExe%. Install must have failed
        echo.
        echo dir %ProgramFiles% /s/b
        echo.
        exit /b 1
    )
    goto :EOF

:Done
    echo Done
    exit /b 0