@REM Don't use -File or the exit code won't propagate out of powershell. No, it doesn't have to make sense.
@powershell -ExecutionPolicy Unrestricted -NoProfile %~dp0CreateDistributedCacheShare.ps1 -CacheDirectory %BUILDXL_CACHE_DIRECTORY% -PromptForElevation $false %*
