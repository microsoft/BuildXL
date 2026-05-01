@echo off
setlocal EnableDelayedExpansion

:: newcopilot.cmd - Create a new worktree and launch a Copilot CLI agent in it.
::
:: Usage: newcopilot.cmd [feature-name]
::   If feature-name is not provided, you will be prompted for one.
::
:: This script:
::   1. Creates a new branch dev/<username>/<feature> and worktree
::   2. Opens a new Windows Terminal tab (or falls back) in that directory
::   3. Launches `agency copilot` in the new worktree

:: --- Get feature name ---
set "FEATURE=%~1"
if "%FEATURE%"=="" (
    set /p "FEATURE=Enter feature name (e.g., fix-cache-bug): "
)
if "%FEATURE%"=="" (
    echo ERROR: Feature name is required.
    exit /b 1
)

:: --- Get and normalize username from git config ---
for /f "usebackq delims=" %%u in (`git config user.name 2^>nul`) do set "RAW_USERNAME=%%u"
if "!RAW_USERNAME!"=="" (
    for /f "usebackq delims=" %%u in (`git config --global user.name 2^>nul`) do set "RAW_USERNAME=%%u"
)
if "!RAW_USERNAME!"=="" set "RAW_USERNAME=%USERNAME%"
if "!RAW_USERNAME!"=="" (
    echo ERROR: Could not determine username from git config or environment.
    exit /b 1
)
:: Normalize: lowercase, replace spaces with hyphens, remove backslashes
set "USERNAME=%RAW_USERNAME: =-%"
set "USERNAME=%USERNAME:\=-%"
:: Convert to lowercase using PowerShell (batch has no native lowercase)
for /f "usebackq delims=" %%l in (`powershell -NoProfile -Command "'%USERNAME%'.ToLower()"`) do set "USERNAME=%%l"

:: --- Determine paths ---
:: Find the main worktree (first entry in git worktree list)
set "MAIN_WORKTREE="
for /f "usebackq tokens=1,* delims= " %%a in (`git worktree list --porcelain`) do (
    if "%%a"=="worktree" if "!MAIN_WORKTREE!"=="" set "MAIN_WORKTREE=%%b"
)
if "%MAIN_WORKTREE%"=="" (
    echo ERROR: Could not determine main worktree.
    exit /b 1
)
:: Worktrees directory is sibling to main worktree
for %%m in ("%MAIN_WORKTREE%") do set "PARENT_DIR=%%~dpm"
set "WORKTREES_DIR=%PARENT_DIR%BuildXL.Internal.worktrees"
set "WORKTREE_PATH=%WORKTREES_DIR%\%FEATURE%"
set "BRANCH=dev/%USERNAME%/%FEATURE%"

:: --- Validate ---
if exist "%WORKTREE_PATH%" (
    echo ERROR: Worktree path already exists: %WORKTREE_PATH%
    echo        Choose a different feature name or remove the existing worktree.
    exit /b 1
)

:: --- Create worktree and branch from main ---
echo Creating branch: %BRANCH%
echo Creating worktree: %WORKTREE_PATH%
echo.
git worktree add -b "%BRANCH%" "%WORKTREE_PATH%" main
if errorlevel 1 (
    echo ERROR: Failed to create worktree. The branch may already exist.
    echo        To reuse an existing branch: git worktree add "%WORKTREE_PATH%" "%BRANCH%"
    exit /b 1
)

echo.
echo Worktree created successfully!
echo.

:: --- Launch in new terminal tab ---
:: Try Windows Terminal first (wt.exe), fall back to start cmd
where wt >nul 2>nul
if %errorlevel%==0 (
    echo Launching Copilot CLI in a new Windows Terminal tab...
    wt -w 0 nt -d "%WORKTREE_PATH%" -- cmd /k "agency copilot"
) else (
    echo Windows Terminal not found. Opening in a new window...
    start "Copilot - %FEATURE%" cmd /k "cd /d \"%WORKTREE_PATH%\" && agency copilot"
)

echo.
echo Done! Your new worktree is at: %WORKTREE_PATH%
echo Branch: %BRANCH%
endlocal
