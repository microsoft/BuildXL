@echo off
setlocal

set ScriptDirPath=%~dp0
set NugetExePath=%ScriptDirPath%Nuget.exe

if not exist %NugetExePath% (
    echo Downloading Nuget.exe
    pushd %ScriptDirPath%
    powershell -File GetNugetExe.ps1
    popd
)

if not exist %NugetExePath% (
    echo Failed to acquire Nuget.exe
    exit /b 1
)
