<#
    .SYNOPSIS

    Run the BasicFilesystem Cache GC

    .DESCRIPTION

    This tool provides a command line interface to the BasicFilesystem GC

    You must provide to it the path to the BuildXL binaries and the path
    to the cache config you wish to use.  It will look at the config,
    constructing the cache, and then see if either it or the remote cache
    for an aggregator is the BasicFilesystem cache.

    .PARAMETER CacheConfigFile

    This is the file that has the cache config that will be loaded to
    construct the cache.

    .PARAMETER DominoPath

    This needs to be the directory path to the BuildXL binaries.

    .PARAMETER CacheFactoryDll

    This is the name of the DLL that holds the Cache.Interfaces.CacheFactory
    class.  This defaults to the correct DLL name: Cache.Interfaces.dll

    .PARAMETER LogFile

    Optional log file to write all of the log entries to.  Note that
    this does not remove output from the console.

    .PARAMETER Times

    Turn on timestamps for all log entries

    .PARAMETER Stats

    Output the statistics Key/Value collection after a successful collection.
    These are output to the PowerShell output.

    Thus, you can run the script with:

    $stats = .\gcShared.ps1 -Stats

    And see the output of the GC just like always but also collect into
    $stats a dictionary of key-value pairs about what happened during
    the GC

    .PARAMETER SessionDuration

    This is the maximum amount of time a session takes to complete for builds
    in this cache.  Expressed in hours:minutes or hours:minutes:seconds

    This is used to determine the GC setting for the Session->Fingerprint
    asymetric fence.  Do not set this too low.  The default is 12 hours.

    This value is multiplied by 4 for extra safety margin and passed
    to the CG as the minimum Fingerprint age for collection.

    This effectively matches the current default in the cache and is a
    very safe fence for making the system reliable.

    .PARAMETER PipDuration

    This is the maximum amount of time a pip takes to complete for builds
    in this cache.  Expressed in hours:minutes or hours:minutes:seconds

    This is used to determine the GC setting for the Fingerprint->CAS
    asymetric fence.  Do not set this too low.  The default is 1 hour.

    This value is multiplied by 4 for extra safety margin and passed
    to the CG as the minimum CAS age for collection.

    This effectively matches the current default in the cache and is a
    very safe fence for making the system reliable.
#>
Param(
    [Parameter(Mandatory=$true)]
    [string]$CacheConfigFile,

    [Parameter(Mandatory=$true)]
    [string]$DominoPath,

    [Parameter(Mandatory=$false)]
    [string]$CacheFactoryDll = 'Cache.Interfaces.dll',

    [Parameter(Mandatory=$false)]
    [string]$LogFile = '',

    [Parameter(Mandatory=$false)]
    [Switch]$Times,

    [Parameter(Mandatory=$false)]
    [Switch]$Stats,

    [Parameter(Mandatory=$false)]
    [TimeSpan]$SessionDuration = '12:00:00',

    [Parameter(Mandatory=$false)]
    [TimeSpan]$PipDuration = '1:00:00'
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Virtual constants

# Load the cache factory class into this PowerShell process
Add-Type -Path (Join-Path $DominoPath $CacheFactoryDll)

# We should use an activity ID that is either new or,
# if the environment has an activity ID, that one
if (Test-Path env:_RELATED_ACTIVITY_ID) {
    $ActivityId = [System.Guid]($env:_RELATED_ACTIVITY_ID)
}
else {
    $ActivityId = [System.Guid]::NewGuid()
}

# Write-Host colors must be ConsoleColor type
# Default to Red and Yellow but pick them up from the environment if defined
[ConsoleColor]$ErrorColor = 'Red'
[ConsoleColor]$HighlightColor = 'Yellow'
if (($Host -ne $null) -and ($Host.PrivateData -ne $null)) {
    if ($Host.PrivateData.ErrorForegroundColor -ne $null) {
        $color = $Host.PrivateData.ErrorForegroundColor
        if ($color -is [ConsoleColor]) {
            $ErrorColor = $color
        }
    }
    if ($Host.PrivateData.WarningForegroundColor -ne $null) {
        $color = $Host.PrivateData.WarningForegroundColor
        if ($color -is [ConsoleColor]) {
            $HighlightColor = $color
        }
    }
}

# Functions we will use

function Get-Timestamp()
{
    $timestamp = ''
    if ($Times) {
        $timestamp = '{0:yyyy-MM-dd HH:mm:ss.ff} ' -f (Get-Date)
    }

    return $timestamp
}

# We try to do all output via this function such that we can optionally
# include timestamps and/or write to a file.  It supports the basic
# -ForegroundColor option that matches that of Write-Host
function Write-HostAndLog
{
    param(
        [Parameter(Mandatory=$false, ValueFromPipeline=$true)]
        [string]$Text = '',

        [Parameter(Mandatory=$false)]
        [System.ConsoleColor]$ForegroundColor
    )

    PROCESS
    {
        $timestamp = Get-Timestamp

        Write-Host -NoNewline $timestamp

        if ($ForegroundColor -ne $null) {
            Write-Host -ForegroundColor $ForegroundColor $Text
        }
        else {
            Write-Host $Text
        }

        if ($LogFile -ne '') {
            Add-Content -Path $LogFile -Encoding ASCII -Value "$timestamp$Text" -ErrorAction SilentlyContinue
        }
    }
}

# Now construct the cache based on the config file
$CacheConfig = Get-Content $CacheConfigFile
$PossibleRootCache = [Cache.Interfaces.CacheFactory]::InitializeCacheAsync($CacheConfig).Result

if (-not $PossibleRootCache.Succeeded) {
    'Error:  Failed to construct cache:' | Write-HostAndLog -ForegroundColor $ErrorColor
    '{0}' -f ($PossibleRootCache.Failure.DescribeIncludingInnerFailures()) | Write-HostAndLog -ForegroundColor $ErrorColor
    exit 1
}

# Get the actual cache and report on what we have opened
$RootCache = $PossibleRootCache.Result
'Opened Cache ID: {0}' -f ($RootCache.CacheId) | Write-HostAndLog

# Note that we are going to go below the ICache interface for some things here
# Namely, to get at the underlying cache (if there is one) and start playing
# with the cache looking for the instance specific GC APIs

# Get the cache we we be working with (the RemoteCache)
# This may just be the cache we had to start with or it is the remote cache
# of a multi-level cache via an aggregator.  (In which case we already
# shut down the local cache.
while (Get-Member -InputObject $RootCache -Name 'RemoteCache' -MemberType Properties) {
    # First, if there is a LocalCache property, we need to close that cache
    # right away as we don't actually want it and it is single-user so we don't
    # want to keep it open when we don't need to.
    if (Get-Member -InputObject $RootCache -Name 'LocalCache' -MemberType Properties) {
        # We should shut down the local cache as it really is not needed and it is only single instance
        'Shutting down local cache: {0}   GUID: {1:B}' -f ($RootCache.LocalCache.CacheId, $RootCache.LocalCache.CacheGuid) | Write-HostAndLog
        $RootCache.LocalCache.ShutdownAsync() | Out-Null
    }

    $RootCache = $RootCache.RemoteCache
}

# This just checks that the remote cache has the methods
# we think it needs since the cache type is not enough
# to tell if the GC could run
try {
    $tmp = $RootCache.CollectUnreferencedFingerprints
    $tmp = $RootCache.CollectUnreferencedCasItems
}
catch {
    'Error: Cache does not seem to have the required GC methods' | Write-HostAndLog -ForegroundColor $ErrorColor
    'Error: Cache type: {0}  Cache ID: {1}  GUID {2:B}' -f ($RootCache.GetType().Name, $RootCache.CacheId, $RootCache.CacheGuid) | Write-HostAndLog -ForegroundColor $ErrorColor
    $RootCache.ShutdownAsync() | Out-Null
    exit 1
}

'Starting Cache GC on cache ID: {0}   GUID: {1:B}' -f ($RootCache.CacheId, $RootCache.CacheGuid) | Write-HostAndLog

# I wish we could pass in our write-host capability but
# PowerShell is strange in the way it handles pipes and
# does not do stderr/stdout in the same way as standard
# shells
if ($LogFile -ne '') {
    $tmpOut = New-Object System.IO.StringWriter
    'Starting Unreferenced Fingerprint Collection' | Write-HostAndLog
}
else {
    $tmpOut = [System.Console]::Out
}
$statsFp = $RootCache.CollectUnreferencedFingerprints($tmpOut, [int]($SessionDuration.TotalSeconds * 4), '', $ActivityId)
if ($LogFile -ne '') {
    $tmpOut.ToString() | Write-HostAndLog
}
if (-not $statsFp.Succeeded) {
    'Error:  Failed to collect unreferenced fingerprints:' | Write-HostAndLog -ForegroundColor $ErrorColor
    '{0}' -f ($statsFp.Failure.DescribeIncludingInnerFailures()) | Write-HostAndLog -ForegroundColor $ErrorColor
    exit 1
}
$statsFp = $statsFp.Result

if ($LogFile -ne '') {
    $tmpOut = New-Object System.IO.StringWriter
    'Starting Unreferenced CAS Collection' | Write-HostAndLog
}
else {
    $tmpOut = [System.Console]::Out
}
$statsCas = $RootCache.CollectUnreferencedCasItems($tmpOut, [int]($PipDuration.TotalSeconds * 4), '', $ActivityId)
if ($LogFile -ne '') {
    $tmpOut.ToString() | Write-HostAndLog
}
if (-not $statsCas.Succeeded) {
    'Error:  Failed to collect unreferenced CAS items:' | Write-HostAndLog -ForegroundColor $ErrorColor
    '{0}' -f ($statsCas.Failure.DescribeIncludingInnerFailures()) | Write-HostAndLog -ForegroundColor $ErrorColor
    exit 1
}
$statsCas = $statsCas.Result

$RootCache.ShutdownAsync() | Out-Null

'Completed GC on cache ID: {0}' -f ($RootCache.CacheId) | Write-HostAndLog

if ($Stats) {
    ($statsFp, $statsCas) | Write-Output
}
