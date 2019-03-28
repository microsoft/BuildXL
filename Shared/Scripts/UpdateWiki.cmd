@echo off
setlocal

set GIT_EXE="C:\Program Files\Git\cmd\git.exe"
set mdpath=%~dp0wiki
set wikiName=Domino.Wiki
set wikipath=%~dp0Domino.wiki
set rootLink=%1

call :StatusMessage "Generating documentation for root %wikipath% with wiki path %rootLink%"
%~dp0\..\..\Out\Bin\debug\net472\bxlScriptAnalyzer.exe /c:%~dp0..\..\config.dsc /f:spec='public\sdk\public*' /a:Documentation /outputFolder:'%mdpath%' /rootLink:%rootLink%

if EXIST %wikipath% (rmdir /s/q %wikipath%)

call :StatusMessage "Clone %wikiName%"
call %GIT_EXE% clone https://mseng.visualstudio.com/Domino/_git/%wikiName% %wikipath%
if %ERRORLEVEL% neq 0 goto error

cd %wikiPath%
call :StatusMessage "Pull latest from %wikiName%"
call %GIT_EXE% pull
if %ERRORLEVEL% neq 0 goto error

set rootOfChanges=%wikiPath%\%rootLink:/=\%

IF NOT EXIST %rootOfChanges% (
    md %rootOfChanges%
) ELSE (
    del /Q %rootOfChanges%\*
)

call :StatusMessage "Copy new documentation into local wiki repo"
copy %mdpath%\* %rootOfChanges%
if %ERRORLEVEL% neq 0 goto error

call :StatusMessage "Add all doc files if they aren't already there"
call %GIT_EXE% add %rootOfChanges%\*
if %ERRORLEVEL% neq 0 goto error

call :StatusMessage "Check if there are any differences"
call %GIT_EXE% diff --exit-code
if %ERRORLEVEL% neq 1 goto end

call :StatusMessage "Commit"
call %GIT_EXE% commit -m "Update Wiki via DsDoc"
if %ERRORLEVEL% neq 0 goto error

call :StatusMessage "Push"
call %GIT_EXE% push
if %ERRORLEVEL% neq 0 goto error
exit /b 0

:StatusMessage
    echo -----------------------------------------------------------------------------------------------------------
    echo -- Update wiki -- %*
    echo -----------------------------------------------------------------------------------------------------------
    title Update wiki -- %*
    exit /b 0

:error
    echo.
    echo --------------------------------------------------------------
    echo -                        FAILURE  :(                         -
    echo --------------------------------------------------------------  
    title
    exit /b 1

:end
    echo.
    echo --------------------------------------------------------------
    echo -                        SUCCESS  :)                         -
    echo --------------------------------------------------------------  
    title
    exit /b 0

endlocal