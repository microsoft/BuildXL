@echo off
setlocal

echo.
echo --------------------------------------------------------------
echo -   FAILURE  :-(  Fix the issues and run validation again.   -
echo --------------------------------------------------------------
call %~dp0..\..\KillBxlInstancesInRepo.cmd
title
endlocal && exit /b 1