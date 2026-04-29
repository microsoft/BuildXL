REM Determine the cache directory. For git worktrees, use the main worktree's Out\Cache
REM so that all worktrees share the same local cache and get cache hits from each other.
REM The git-common-dir points to the main worktree's .git directory, which lets us
REM find the main worktree root regardless of which worktree we're building from.

set BUILDXL_CACHE_DIRECTORY=%~dp0..\..\Out\Cache

REM Check if git is available
where git >nul 2>nul
if ERRORLEVEL 1 goto :_SetCacheDirDone

REM Get the git common dir (points to main worktree's .git for worktrees, or local .git)
for /f "delims=" %%G in ('git rev-parse --git-common-dir 2^>nul') do set _GIT_COMMON_DIR=%%G
if not defined _GIT_COMMON_DIR goto :_SetCacheDirDone

REM Resolve to the main worktree root by going up one level from the .git dir.
REM For worktrees this navigates to the main worktree; for the main worktree it's a no-op.
pushd "%_GIT_COMMON_DIR%\.."
set BUILDXL_CACHE_DIRECTORY=%CD%\Out\Cache
popd
set _GIT_COMMON_DIR=

:_SetCacheDirDone

IF NOT EXIST "%BUILDXL_CACHE_DIRECTORY%" (
	mkdir %BUILDXL_CACHE_DIRECTORY%
)

pushd %BUILDXL_CACHE_DIRECTORY%
set BUILDXL_CACHE_DIRECTORY=%CD%
popd