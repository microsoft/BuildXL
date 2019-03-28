@echo off

REM This script is to set environment variables in the VSO Build workflow.
REM Don't forget to check "Modify Environment" on the task

echo set %1=%2
set %1=%2