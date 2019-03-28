@echo off

Set BUILDXL_BIN_DIRECTORY=%~dp0..\..\..\Out\Bin\debug\net472

%BUILDXL_BIN_DIRECTORY%\bxl.exe /c:config.dsc %*
