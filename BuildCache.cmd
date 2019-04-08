@echo off
setlocal

set EXE_DIR=%~dp0out\Bin\debug\net472
set EXE=%EXE_DIR%\BuildCache.Tool.exe
set CACHE_ROOT=%BUILDXL_CACHE_DIRECTORY%

REM Directory that receives exported cache
set BUILDXL_CACHE_EXPORT_DIRECTORY=%~d0\BuildXLCacheExport

REM CacheRoot for local import test
set TEST_CACHE_ROOT=%~d0\BuildXLCacheImportTest

REM AppFabric Cache was created with following command
REM New-Cache -CacheName BuildXLSelfhost -Eviction LRU -Expirable false -Force -Secondaries 1 -MinSecondaries 1

set CMD=%1
if "%CMD%"=="" (
    echo Unknown command argument
    echo BuildCache.cmd ShowContentBag CD8F61D41C579DC2B676BF6ACC1E008EF01417C6
    echo BuildCache.cmd ShowContentToken EE879A24868414BD709CDE5378F6647192EE0074
    echo BuildCache.cmd ShowContentProvenance EE879A24868414BD709CDE5378F6647192EE0074
    echo BuildCache.cmd ShowContentProvenance %~dp0Out\Bin\Debug\bxl.exe
    exit /b 1
)

if /i "%CMD%"=="ShowContentBag" (
    if "%2"=="" (
        echo Missing first argument: fingerprint
        exit /b 1
    )
    set ARGS_CMD=/fingerprint=%2 /forceJson=false
)

if /i "%CMD%"=="DeleteContentBag" (
    if "%2"=="" (
        echo Missing first argument: fingerprint
        exit /b 1
    )
    set ARGS_CMD=/fingerprint=%2
)

if /i "%CMD%"=="ShowContentToken" (
    if "%2"=="" (
        echo Missing first argument: hash
        exit /b 1
    )
    set ARGS_CMD=/contentHash=%2
)

if /i "%CMD%"=="ShowContentProvenance" (
    if "%2"=="" (
        echo Missing first argument: hash
        exit /b 1
    )
    set ARGS_CMD=/contentHashOrPath=%2
)

if /i "%CMD%"=="ListContent" (
    set ARGS_CMD=
)

if /i "%CMD%"=="Validate" (
    set ARGS_CMD=
)

if /i "%CMD%"=="DiskStats" (
    set ARGS_CMD=
)

if /i "%CMD%"=="Export" (
    set ARGS_CMD=/directoryPath=%BUILDXL_CACHE_EXPORT_DIRECTORY% /exportMode=Append /includeContent
)

if /i "%CMD%"=="Import" (
    set CACHE_ROOT=%TEST_CACHE_ROOT%
    set ARGS_CMD=/machine=%COMPUTERNAME% /directoryPath=%BUILDXL_CACHE_EXPORT_DIRECTORY%
)

set ARGS_COMMON=^
/cacheRoot=%CACHE_ROOT% ^
/hashType=Vso0 ^
/contentbagtypeassemblypath=%EXE_DIR%\BuildXL.Engine.Cache.dll ^
/contentbagtypename=BuildXL.Engine.Cache.Fingerprints.PipFingerprintEntry ^
/cacheConfigPaths=%~dp0BuildCacheDefault.json;%~dp0BuildCacheOverride.json

set CMDLINE=%EXE% %CMD% %ARGS_COMMON% %ARGS_CMD%
echo %CMDLINE%
%CMDLINE%
