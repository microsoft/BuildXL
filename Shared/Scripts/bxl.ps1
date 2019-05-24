<#
.SYNOPSIS

Wrapper for bxl.exe that provides support for self-hosting.

.DESCRIPTION

This script is a wrapper for bxl.exe (BuildXL proper) that delegates to an LKG (NuGet)
or locally-built (Dev) deployment. It adds two parameters for managing BuildXL deployments: -Use and -Deploy.
-Use specifies the deployment to use while building (the LKG deployment is used by default).
-Deploy (if specified) indicates which deployment to update upon a successful build.

.PARAMETER Vanilla

Disables non-default (usually experimental) options. Without this switch, experimental or
otherwise off-by-default features may be enabled for dogfooding.

.EXAMPLE

bxl -SelfhostHelp

Prints this help text (for the selfhost wrapper itself)

.EXAMPLE

bxl /?

Prints the help for the LKG bxl.exe

.EXAMPLE

bxl -Use Dev /?

Prints the help for the Dev bxl.exe

.EXAMPLE

bxl -Deploy Dev -DeployConfig Debug

Uses the LKG deployment to update the Dev deployment with Debug binaries

.EXAMPLE

bxl -Deploy Dev -DeployConfig Debug -Minimal

Uses the LKG deployment to update the Dev deployment with Debug binaries, skipping unittest and other tools that are not part of the core deployment.
.EXAMPLE

bxl -Use Dev -Deploy Dev

Uses the Dev deployment to update the Dev deployment

#>

[CmdletBinding(PositionalBinding=$false)]
param(
    [switch]$SelfhostHelp,

    [ValidateSet("LKG", "Dev", "RunCheckinTests", "RunCheckinTestSamples", "ChangeJournalService")]
    [string]$Use = "LKG",

    [ValidateSet("Release", "Debug")]
    [string]$DeployConfig = "Debug", # must match defaultQualifier.configuration in config.dsc 

    [ValidateSet("net472", "win-x64", "osx-x64")]
    [string]$DeployRuntime = "win-x64", # must correspond to defaultQualifier.targetFramework in config.dsc 

    [Parameter(Mandatory=$false)]
    [string]$DominoDeploymentRoot = "Out\Bin",

    [Parameter(Mandatory=$false)]
    [ValidateSet("Dev", "RunCheckinTests", "RunCheckinTestSamples", "ChangeJournalService")]
    [string]$Deploy,

    [Parameter(Mandatory=$false)]
    [string]$TestMethod = "",

    [Parameter(Mandatory=$false)]
    [string]$TestClass = "",

    [Parameter(Mandatory=$false)]
    [switch]$DeployStandaloneTest = $false,

    # Task 544796 to enable this
    [Parameter(Mandatory=$false)]
    [ValidateSet("Disable", "Consume", "ConsumeAndPublish")]
    [string]$SharedCacheMode = "Disable",

    [Parameter(Mandatory=$false)]
    [string]$DefaultConfig,

    [switch]$Vanilla,

    [switch]$Minimal = $false,

    [switch]$Cache = $false,

    [switch]$CacheNuget = $false,

    [switch]$All = $false,

    [switch]$SkipTests = $false,

    [switch]$Analyze = $false,

    [switch]$LongRunningTest = $false,

    [switch]$DeployDev = $false,

    [switch]$PatchDev = $false,

    [switch]$DoNotUseDefaultCacheConfigFilePath = $false,

    [switch]$UseL3Cache = $true,
	
	[Parameter(Mandatory=$false)]
	[switch]$UseDedupStore = $false,
	
    [string]$VsoAccount = "mseng",

    [string]$CacheNamespace = "BuildXLSelfhost",

    [Parameter(Mandatory=$false)]
	[switch]$Vs = $false,

    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$DominoArguments
)

$ErrorActionPreference = "Stop";
Set-StrictMode -Version Latest;

# Drive letter used as a canonical enlistment root.
$NormalizationDrive = "B:";
$NormalizationDriveLetter = "B";

# Since we don't have process-scoped drive letters, we have to have a locking scheme for usage ofr $NormalizationDrive.
# We keep a special file under Out\ that acts as a lock; this script and also OSGTool's bbuild hold on to this file as an indication
# that the $NormalizationDrive is in use by a build and shouldn't be remapped.
$NormalizationLockRelativePath = "Out\.NormalizationLock"

# These are the options added unless -Vanilla is specified.
$NonVanillaOptions = @("/IncrementalScheduling", "/nowarn:909 /nowarn:11318 /nowarn:11319 /unsafe_IgnorePreloadedDlls- /historicMetadataCache+ /cachemiss");
# Add the new-cache options including a unique build session name
$NonVanillaOptions += @(
        '/cacheSessionName:{0:yyyyMMdd_HHmmssff}-{1}@{2}' -f ((Get-Date), [System.Security.Principal.WindowsIdentity]::GetCurrent().Name.Replace('\', '-'), [System.Net.Dns]::GetHostName())
);

if ($SelfhostHelp) {
    Get-Help -Detailed $PSCommandPath;
    return;
}

if ($env:_BUILDXL_INIT_DONE -ne "1") {
    throw "BuildXL environment has not been set up (expected _BUILDXL_INIT_DONE=1 from Init.cmd)";
}

$enlistmentRoot = $env:ENLISTMENTROOT;
if (! (Test-Path -PathType Container $enlistmentRoot)) {
    throw "The BuildXL enlistment root does not exist or wasn't defined by Init.cmd";
}

$lkgDir = $env:BUILDXL_LKG;
if (! (Test-Path -PathType Container $lkgDir)) {
    throw "The BuildXL LKG root does not exist or was not defined by Init.cmd";
}

$cacheDirectory = $env:BUILDXL_CACHE_DIRECTORY;
if (!$cacheDirectory) {
    throw "The BuildXL cache directory was not defined by Init.cmd";
}

if (! (Test-Path -PathType Container $cacheDirectory)) {
    mkdir $cacheDirectory;
}

if ($DominoArguments -eq $null) {
    $DominoArguments = @()
}

# Use Env var to check for microsoftInternal
$isMicrosoftInternal = [Environment]::GetEnvironmentVariable("[Sdk.BuildXL]microsoftInternal") -eq "1"

$disableSharedCache = ($SharedCacheMode -eq "Disable" -or (-not $isMicrosoftInternal));
$publishToSharedCache = ($SharedCacheMode -eq "ConsumeAndPublish" -and $isMicrosoftInternal);

if ($PatchDev) {
    # PatchDev is the same as deploy dev except no cleaning of deployment
    # is done so that downstream dependents do not need to be rebuilt
    $DeployDev = $true;
}

$shouldClean = $PatchDev -ne $true

$AdditionalBuildXLArguments = @()

if ($DeployDev) {
    $Deploy = "Dev";
    $Minimal = $true;
}

if ($env:BUILDXL_ADDITIONAL_DEFAULTS)
{
    $AdditionalBuildXLArguments += $env:BUILDXL_ADDITIONAL_DEFAULTS
}
if ($env:BUILDXL_ADDITIONAL_DEFAULTS)
{
    $AdditionalDominoArguments += $env:BUILDXL_ADDITIONAL_DEFAULTS
}

$BuildXLExeName = "bxl.exe";
$BuildXLRunnerExeName = "RunInSubst.exe";

if ($Analyze)
{
    $BuildXLExeName = "bxlanalyzer.exe";
}


if (($DominoArguments -match "/c(onfig)?:.*").Length -eq 0) {
    if ($DefaultConfig) {
        $AdditionalBuildXLArguments += "/config:$DefaultConfig";
    }
}

if (! $Vanilla) {
    $AdditionalBuildXLArguments += $NonVanillaOptions;
}

if ($TestMethod -ne "") {
    $AdditionalBuildXLArguments += "/p:[UnitTest]Filter.testMethod=$TestMethod";
}

if ($TestClass -ne "") {
    $AdditionalBuildXLArguments += "/p:[UnitTest]Filter.testClass=$TestClass";
}

if ($DeployStandaloneTest) {
    $AdditionalBuildXLArguments += "/p:[Sdk.BuildXL]DeployStandaloneTest=true";
}

if ($Vs) {
    $AdditionalBuildXLArguments += "/p:[Sdk.BuildXL]GenerateVSSolution=true /q:DebugNet472 /vs";
}

# WARNING: CloudBuild selfhost builds do NOT use this script file. When adding a new argument below, we should add the argument to selfhost queues in CloudBuild. Please contact bxl team. 
$AdditionalBuildXLArguments += @("/remotetelemetry", "/reuseOutputsOnDisk+", "/scrubDirectory:${enlistmentRoot}\out\objects", "/storeFingerprints", "/cacheMiss");
$AdditionalBuildXLArguments += @("/p:[Sdk.BuildXL]useQTest=true");

if (($DominoArguments -match "logsDirectory:.*").Length -eq 0 -and ($DominoArguments -match "logPrefix:.*").Length -eq 0) {
    $AdditionalBuildXLArguments += "/logsToRetain:20";
}

if ($Deploy -eq "LKG") {
    throw "The LKG deployment is special since it comes from a published NuGet package. It cannot be re-deployed in this selfhost wrapper.";
}

function New-Deployment {
    param([string]$Root, [string]$Name, [string]$Description, [string]$TelemetryEnvironment, [string]$dir = $null, [bool]$enableServerMode = $false, [string]$DeploymentRoot);

    $serverDeploymentDir = Join-Path $Root "Out\Selfhost\$name.ServerDeployment"

    if (! $dir) {
        $dir = Join-Path $Root "Out\Selfhost\$name";
    }

    $buildRelativeDir = [io.path]::combine($DeploymentRoot, $DeployConfig, $DeployRuntime)

    return @{
        description = $Description;
        dir = $dir;
        domino = Join-Path $dir $BuildXLExeName;
        dominoRunner = Join-Path $dir $BuildXLRunnerExeName;
        buildDir = Join-Path $Root $buildRelativeDir;
        enableServerMode = $enableServerMode;
        telemetryEnvironment = $TelemetryEnvironment;
        serverDeploymentDir = $serverDeploymentDir;
    };
}

function Write-CacheConfigJson {
    param([string]$ConfigPath, [bool]$UseSharedCache, [bool]$PublishToSharedCache, [bool]$UseL3Cache, [string]$VsoAccount, [string]$CacheNamespace);

    $configOptions = Get-CacheConfig -UseSharedCache $UseSharedCache -PublishToSharedCache $PublishToSharedCache -UseL3Cache $UseL3Cache -VsoAccount $VsoAccount -CacheNamespace $CacheNamespace;
    Set-Content -Path $configPath -Value (ConvertTo-Json $configOptions)
}

function Get-CacheConfig {
    param([bool]$UseSharedCache, [bool]$PublishToSharedCache, [bool]$UseL3Cache, [string]$VsoAccount, [string]$CacheNamespace);
    
    $localCache = @{
         Assembly = "BuildXL.Cache.MemoizationStoreAdapter";
         Type = "BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory";
         CacheId = "SelfhostCS2L1";
         MaxCacheSizeInMB = 20240;
         CacheRootPath = $cacheDirectory;
         CacheLogPath = "[BuildXLSelectedLogPath]";
         UseStreamCAS = $true;
         <# TODO: Enable elasticity when new lkg is published. #>
         <# EnableElasticity = $true; #>
    };

    if (! $UseSharedCache) {
        return $localCache;
    }

    if ($UseL3Cache) {
        $remoteCache = @{
            Assembly = "BuildXL.Cache.BuildCacheAdapter";
            Type = "BuildXL.Cache.BuildCacheAdapter.BuildCacheFactory";
            CacheId = "L3Cache";
            CacheLogPath = "[BuildXLSelectedLogPath].new";
            CacheServiceFingerprintEndpoint = "https://$VsoAccount.artifacts.visualstudio.com/DefaultCollection";
            CacheServiceContentEndpoint = "https://$VsoAccount.vsblob.visualstudio.com/DefaultCollection";
            UseBlobContentHashLists = $true;
            CacheNamespace = $CacheNamespace;
        };
		
		<# TODO: After unifying flags, remove if statement and hard-code dummy value into remoteCache #>
		if ($UseDedupStore) {
			$remoteCache.Add("UseDedupStore", $true);
		}
    } else {
        $remoteCache = @{
            Assembly = "BuildXL.Cache.BasicFilesystem";
            Type = "BuildXL.Cache.BasicFilesystem.BasicFilesystemCacheFactory";
            CacheId = "SelfhostBasicFileSystemL2";
            CacheRootPath = $SharedCachePath;
            StrictMetadataCasCoupling = $true;
        };
    }

    return @{
        Assembly = "BuildXL.Cache.VerticalAggregator";
        Type = "BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory";
        RemoteIsReadOnly = !($PublishToSharedCache);
        LocalCache = $localCache;
        RemoteCache = $remoteCache;
    };
}

function Call-Subst {
    $s = subst @args;
    if ($LastExitCode -ne 0) {
        throw "Subst $args failed with exit code $LastExitCode : $s";
    }

    if ($s -eq $null) {
        return @("");
    }

    return @($s);
}

function Run-WithNormalizedPath {
    param([scriptblock]$normalizedPathAction);

    # The locking protocol:
    # - First, acquire our own lock (e.g. S:\BuildXL\Out\.NormalizationLock). A conflict here amounts to concurrent builds with the same enlistment.
    # - Then, try to point $NormalizationDrive (e.g. B:) to this enlistment. If this succeeds, then e.g. B:\Out\.NormalizationLock is locked (by prior point!)
    #   * If $NormalizationDrive already points here, we're done (B:\Out\.NormalizationLock is held by us)
    #   * If it points somewhere else, we must *acquire the lock for that current mapping*, and then re-point it.
    #   * If it points nowhere, just create it.
    $selfLockPath = Join-Path $enlistmentRoot $NormalizationLockRelativePath
    # Should pass canonicalized paths to subst.
    $mappingPath = (Resolve-Path $enlistmentRoot).Path;

    return &$normalizedPathAction;
}

function Remap-PathToNormalizedDrive {
    param([string[]]$paths);

    return $paths -replace ([Regex]::Escape($enlistmentRoot.TrimEnd("\")) + "(\\|\z)"), ($NormalizationDrive + "\");
}

function Run-ProcessWithNormalizedPath {
    param([string]$executableRunner, [string]$executable, [string[]]$processArgs);
    return Run-WithNormalizedPath {
        [string]$remappedExecutableRunner = $executableRunner;
        [string]$remappedExecutable = @(Remap-PathToNormalizedDrive $executable);
        $enlistmentrootTrimmed = $enlistmentRoot.TrimEnd('\')
        [string[]]$remappedArgs = "$NormalizationDriveLetter=$enlistmentrootTrimmed";
        $remappedArgs += "$remappedExecutable";
        $remappedArgs += @(Remap-PathToNormalizedDrive $processArgs);
        $remappedArgs += "/substTarget:$NormalizationDrive\ /substSource:$enlistmentrootTrimmed\"

        $remappedArgs += " /logProcessDetouringStatus+ /logProcessData+ /logProcesses+";
        Write-Host -ForegroundColor Green $remappedExecutableRunner @remappedArgs;
        $p = Start-Process -FilePath $remappedExecutableRunner -ArgumentList $remappedArgs -WorkingDirectory (pwd).Path -NoNewWindow -PassThru;
        Wait-Process -InputObject $p;
        return $p.ExitCode;
    };
}

function Log {
    param([switch]$NoNewline, [string]$message)

    Write-Host -NoNewline:$NoNewline -BackgroundColor Black -ForegroundColor Cyan $message;
}

function Log-Emphasis {
    param([switch]$NoNewline, [string]$message)

    Write-Host -NoNewline:$NoNewline -BackgroundColor Black -ForegroundColor Green $message;
}

function Log-Error {
    param([string]$message)

    Write-Host -BackgroundColor Black -ForegroundColor Red $message;
}

function Mirror-Directory {
    param([string]$src, [string]$dst)

    robocopy /MT /MIR /NJH /NP /NDL /NFL /NC /NS $src $dst;
    if ($LastExitCode -ge 8) {
        throw "Robocopy failed with exit code $LastExitCode";
    }
}

function Get-Deployment {
    param([string]$name)

    $d = $deployments[$name];
    if ($d -eq $null) {
        throw "Missing deployment spec for $name";
    }
    return $d;
}

# Note that we only enable server mode for the LKG deployment; it is in a version-named directory and so we don't have any trouble with the persistent
# server keeping the binaries locked (new versions will get a new directory).
$deployments = @{
    LKG = New-Deployment -Root $enlistmentRoot -Name "LKG" -Description "LKG (published NuGet package)" -TelemetryEnvironment "SelfhostLKG" -Dir $lkgDir -EnableServerMode $true -DeploymentRoot $DominoDeploymentRoot;
    Dev = New-Deployment -Root $enlistmentRoot -Name "Dev" -Description "dev (locally-built)" -TelemetryEnvironment "SelfHostPrivateBuild" -EnableServerMode $true -DeploymentRoot $DominoDeploymentRoot;
    RunCheckinTests = New-Deployment -Root $enlistmentRoot -Name "RunCheckinTests" -Description "checkin-validation"  -TelemetryEnvironment "SelfHostPrivateBuild" -EnableServerMode $true -DeploymentRoot $DominoDeploymentRoot;
    RunCheckinTestSamples = New-Deployment -Root $enlistmentRoot -Name "RunCheckinTestSamples" -Description "checkin-validation-samples"  -TelemetryEnvironment "SelfHostPrivateBuild" -DeploymentRoot $DominoDeploymentRoot;
    ChangeJournalService = New-Deployment -Root $enlistmentRoot -Name "ChangeJournalService" -Description "change journal service"  -TelemetryEnvironment "SelfHostPrivateBuild" -DeploymentRoot $DominoDeploymentRoot;
};

$shouldDeploy = $Deploy -ne $null -and $Deploy -ne "";

$useDeployment = Get-Deployment $use;
Log "> BuildXL Selfhost wrapper - run 'domino -SelfhostHelp' for more info";
Log -NoNewline "Building using the ";
Log-Emphasis -NoNewline $($useDeployment.description)
Log " version of BuildXL.";

$AdditionalBuildXLArguments += "/environment:$($useDeployment.telemetryEnvironment)";

if (! $DoNotUseDefaultCacheConfigFilePath) {

    $cacheConfigPath = (Join-Path $cacheDirectory CacheCore.json);
    Write-CacheConfigJson -ConfigPath $cacheConfigPath -UseSharedCache (!$disableSharedCache) -PublishToSharedCache $publishToSharedCache -UseL3Cache $UseL3Cache -VsoAccount $VsoAccount -CacheNamespace $CacheNamespace;

    $AdditionalBuildXLArguments += "/cacheConfigFilePath:" + $cacheConfigPath;
}

if ($useDeployment.EnableServerMode) {
    Log "Server mode enabled."
    $AdditionalBuildXLArguments += "/server"
    $AdditionalBuildXLArguments += "/serverDeploymentDir:$($useDeployment.serverDeploymentDir)"
}

if ($shouldDeploy) {
    $deployDeployment = Get-Deployment $Deploy;
    Log "The newly-built BuildXL will be deployed as the $($deployDeployment.description) version.";
}

if ($Minimal) {
    Log "The newly-built BuildXL will not be fully validated because you are running with -Minimal.";
}

if (! (Test-Path -PathType Leaf $useDeployment.domino)) {
    throw "The BuildXL executable was not found at $($useDeployment.domino). Maybe you need to build and deploy with -Deploy $Use first?";
}

# It's important that when neither -DeployConfig nor -DeployRuntime is explicitly provided
# (i.e., the default values are used) we don't add any additional qualifiers here.  The
# reason is because we don't want to add extra qualifiers when the user explicitly 
# specifies which qualifiers to build (using the /q switch).
#
# This basically means that the default values for -DeployConfig and -DeployRuntime
# must correspond to default qualifier in config.dsc.
if ($DeployConfig -eq "Release") {
    if ($DeployRuntime -eq "net472") {
        $AdditionalBuildXLArguments += "/q:ReleaseNet472"
    }
    elseif ($DeployRuntime -eq "osx-x64") {
        $AdditionalBuildXLArguments += "/q:ReleaseDotNetCoreMac"
    }
    else {
        $AdditionalBuildXLArguments += "/q:Release"
    }
} else {
    if ($DeployRuntime -eq "net472") {
        $AdditionalBuildXLArguments += "/q:DebugNet472"
    }
    elseif ($DeployRuntime -eq "osx-x64") {
        $AdditionalBuildXLArguments += "/q:DebugDotNetCoreMac"
    }
}

if ($shouldDeploy -and $shouldClean) {
    Log "Cleaning output directory (needed to prevent extra files from being deployed)";
    # Note that we are deleting the temporary deployment location (not the one we would execute); otherwise
    # a deployment couldn't replace itself.

    if (Test-Path -PathType Container $deployDeployment.buildDir) {
        rmdir -Recurse $deployDeployment.buildDir;
    }
}

# let any freeform filter arguments take precedence over the default filter
$skipFilter = $false;
for($i = 0; $i -lt $DominoArguments.Count; $i++){
    if (!$DominoArguments[$i].StartsWith('-') -and !$DominoArguments[$i].StartsWith('/')){
        $skipFilter = $true;
    }
}

if (!$skipFilter){

    $AllCacheProjectsFilter = "(spec='Public\Src\Cache\ContentStore\*')or(spec='Public\Src\Cache\MemoizationStore\*')or(spec='Public\Src\Cache\DistributedCache.Host\*')or(spec='Public\Src\Deployment\cache.dsc')";
    $CacheNugetFilter = "spec='Public\Src\Deployment\cache.nugetpackages.dsc'";
    $CacheOutputFilter = "output='out\bin\$DeployConfig\cache\*'";
    $CacheLongRunningFilter = "tag='LongRunningTest'";
    $PrivateNugetFilter = "spec='Public\src\Deployment\privatePackages.dsc'";
    $IdeFilter = "spec='Public\src\Deployment\ide.dsc'";
    $TestDeploymentFilter = "spec='Public\src\Deployment\tests.dsc'";
    $PrivateWdgFilter = "dpt(spec='private\Guests\WDG\*')";

    if ($Minimal) {
        # filtering by core deployment.
        $AdditionalBuildXLArguments +=  "/f:(output='$($useDeployment.buildDir)\*'or(output='out\bin\$DeployConfig\Sdk\*')or($CacheOutputFilter))and~($CacheLongRunningFilter)"
    }

    if ($Cache) {
        # Only build Cache code.
        if ($LongRunningTest) {
            $AdditionalBuildXLArguments += "/f:$AllCacheProjectsFilter"
        }
        elseif ($CacheNuget) {
            $AdditionalBuildXLArguments += "/f:($CacheNugetFilter)"
        }
        else {
            $AdditionalBuildXLArguments += "/f:($AllCacheProjectsFilter)and~($CacheLongRunningFilter)and(($CacheOutputFilter)or(tag='test'))"
        }
    }
    else {
        if ($LongRunningTest) {
            $AdditionalBuildXLArguments += "/f:($CacheLongRunningFilter)and~($CacheNugetFilter)"
        }

        if ($All) {
            $AdditionalBuildXLArguments += "/f:~($CacheLongRunningFilter)"
        }
    }

    if (!$All -and !$Minimal -and !$Cache -and !$LongRunningTest) {
        #Request the same output files from minimal above to make sure that deployment is fully specificed
        #Then request excludes guests\wdg and all downstream projects.
        #The filter does't have spaces because the dominow wrapper doesn't play well with them
        $AdditionalBuildXLArguments += "/f:~($PrivateWdgFilter)and~($AllCacheProjectsFilter)and~($CacheLongRunningFilter)and~($CacheNugetFilter)and~($PrivateNugetFilter)and~($IdeFilter)and~($TestDeploymentFilter)"
    }

    if ($SkipTests) {
        $AdditionalBuildXLArguments +=  "/f:~($CacheLongRunningFilter)and~($CacheNugetFilter)"
    }
}

if ($Analyze) {
    $AdditionalBuildXLArguments = @()
    $DominoArguments.RemoveAt(0);
}

[string[]]$DominoArguments = @($DominoArguments |% { $_.Replace("#singlequote#", "'").Replace("#openparens#", "(").Replace("#closeparens#", ")"); })
[string[]]$DominoArguments = $AdditionalBuildXLArguments + $DominoArguments;


$bxlExitCode = Run-ProcessWithNormalizedPath $useDeployment.dominoRunner $useDeployment.domino $DominoArguments;
$bxlSuccess = ($bxlExitCode -eq 0);

if ($bxlSuccess) {
    Log "Done.";
} else {
    Log-Error "The BuildXL build failed with exit code $bxlExitCode";
}

# BuildXL has now exited. If deploying, we can now copy the new binaries such that this script may find them.
if ($shouldDeploy) {
    if ($bxlSuccess) {
        Log "Deploying $Deploy from $($deployDeployment.buildDir)";

        $builtBuildXL = Join-Path $deployDeployment.buildDir $BuildXLExeName;
        if (! (Test-Path -PathType Leaf $builtBuildXL)) {
            throw "The BuildXL executable was not found at $builtBuildXL. This file should have been built (the build appears to have succeeded)";
        }

        Mirror-Directory $deployDeployment.buildDir $deployDeployment.dir;

        Log "Done. You can use the binaries with 'bxl -Use $Deploy'";
    } else {
        Log-Error "Deployment cancelled since the build failed; see above."
    }
}

if (! $bxlSuccess) {
    $host.SetShouldExit($bxlExitCode);
}
