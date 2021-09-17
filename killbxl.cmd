@echo off
REM kill any bxl.exe instances running in the machine
echo Terminating existing instances of BuildXL
call %~dp0\Shared\Scripts\KillBxlInstancesInRepo.cmd