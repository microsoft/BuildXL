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


.\bxl -DeployDev -DeployConfig "release" -DeployRuntime "net6.0"

Uses the LKG deployment to update the Dev deployment with net6.0 release binaries 

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

    [Parameter(Mandatory=$false)]
    [string]$DevRoot,

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
    [ValidateSet("Disable", "Consume", "ConsumeAndPublish")]
    [string]$SharedCacheMode = "Consume",

    [Parameter(Mandatory=$false)]
    [switch]$DevCache = $false,

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

    [switch]$Bvfs = $false,

    [switch]$PatchDev = $false,

    [switch]$DisableInteractive = $false,

    [switch]$DoNotUseDefaultCacheConfigFilePath = $false,

    [Parameter(Mandatory=$false)]
    [switch]$UseDedupStore = $false,

    [Parameter(Mandatory=$false)]
    [switch]$UseVfs = $false,

    [Parameter(Mandatory=$false)]
    [switch]$UseBlobL3 = $false,

    [string]$VsoAccount = "mseng",

    [string]$CacheNamespace = "BuildXLSelfhost",

    [Parameter(Mandatory=$false)]
    [switch]$Vs = $false,

    [Parameter(Mandatory=$false)]
    [switch]$VsAll = $false,

    [Parameter(Mandatory=$false)]
    [switch]$UseManagedSharedCompilation = $true,

    [Parameter(Mandatory=$false)]
    [switch]$NoQTest = $false,

    [switch]$NoSubst = $false,

    [Parameter(Mandatory=$false)]
    [switch]$EnableProcessRemoting = $false,

    [Parameter(Mandatory=$false)]
    [string]$AnyBuildClientDir,

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
        '/cacheSessionName:{0:yyyyMMdd_HHmmssff}-{1}@{2}' -f ((Get-Date), [System.Security.Principal.WindowsIdentity]::GetCurrent().Name.Replace(' ', '-').Replace('\', '-'), [System.Net.Dns]::GetHostName())
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

# TF_BUILD is an environment variable which is always present when running on ADO
$tfBuild = [Environment]::GetEnvironmentVariable("TF_BUILD")
[bool] $isRunningOnADO = If ($tfBuild -eq $null) { $false } Else { $tfBuild }

# Even if managed shared compilation was requested to be on, we turn it off when:
# - /ado option is present, so AzDevOps scenarios are kept unchanged. 
# - this is not considered an internal build
# We might decide to relax this once shared compilation gets enough mileage.
# TODO: Enable shared compilation for -EnableProcessRemoting.
#       Currently some builds failed to write outputs. Need more investigation.
if ($UseManagedSharedCompilation -and 
        ($isRunningOnADO -or (-not $isMicrosoftInternal) -or $EnableProcessRemoting)) {
    $UseManagedSharedCompilation = $false
}

if ($UseManagedSharedCompilation) {
    [Environment]::SetEnvironmentVariable("[Sdk.BuildXL]useManagedSharedCompilation", "1")
}

# Dev cache adds 5-10s to TTFP due to cache initialization. Since we want tight inner loop (code-test-debug) 
# to be crisp, we disable dev cache when TestMethod or TestClass is specified, i.e., when testing
# a single unit test method or a single unit test class. 
# This behavior can still be overriden by specifying explicitly the SharedCacheMode.
if ($TestMethod -ne "" -or $TestClass -ne "") {
    $DevCache = $false;
}

if ($DevCache) {
    if ($SharedCacheMode -eq "Disable") {
        $SharedCacheMode = "Consume";
    }
}

$useSharedCache = (($SharedCacheMode -eq "Consume" -or $SharedCacheMode -eq "ConsumeAndPublish") -and $isMicrosoftInternal);
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

$BuildXLExeName = "bxl.exe";
$BuildXLRunnerExeName = "RunInSubst.exe";
$NugetDownloaderName = "NugetDownloader.exe";

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

if ($Vs -or $VsAll) {
    $AdditionalBuildXLArguments += "/p:[Sdk.BuildXL]GenerateVSSolution=true /vs /vsnew";
    if ($VsAll) {
        # -vsAll builds both .NET Core and Net472 and doesn't specify any /vsTargetFramework filters 
        $AdditionalBuildXLArguments += "/q:Debug /q:DebugNet472";
    } else {
        # by default (-vs) we build only .NET Core and only projects targeting one of the .NET Core frameworks
        $AdditionalBuildXLArguments += "/q:Debug /vsTargetFramework:netstandard2.0 /vsTargetFramework:netstandard2.1 /vsTargetFramework:net6.0";
    }
}

# Various tools consume language pack files under this path if they are installed. Untrack them to prevent DFAs in local builds
$AdditionalBuildXLArguments +=@("/unsafe_GlobalUntrackedScopes:""C:\Program Files\WindowsApps""");

# WARNING: CloudBuild selfhost builds do NOT use this script file. When adding a new argument below, we should add the argument to selfhost queues in CloudBuild. Please contact bxl team. 
$AdditionalBuildXLArguments += @("/remotetelemetry", "/reuseOutputsOnDisk+", "/enableEvaluationThrottling");

# Lazy shared opaque deletion is an experimental feature. We want to turn it on only for internal builds and when this script is not 
# running under ADO (so we keep the feature out of PR validations for now).
if (-not $isRunningOnADO -and $isMicrosoftInternal) {
    $AdditionalBuildXLArguments += @("/exp:LazySODeletion");
}

if ($NoQTest) {
    $AdditionalBuildXLArguments += "/p:[Sdk.BuildXL]useQTest=false";
}
else {
    $AdditionalBuildXLArguments += "/p:[Sdk.BuildXL]useQTest=true";
}

if (($DominoArguments -match "logsDirectory:.*").Length -eq 0 -and ($DominoArguments -match "logPrefix:.*").Length -eq 0) {
    $AdditionalBuildXLArguments += "/logsToRetain:20";
}

if ($EnableProcessRemoting) {
    # Unit tests are not supported for remoting because change journal is not enabled on agents
    # and all volumes in agents have the same serial.
    $AdditionalBuildXLArguments += @(
        "/enableProcessRemoting+",
        "/processCanRunRemoteTags:compile;cacheTest");

    if (-not [string]::IsNullOrEmpty($AnyBuildClientDir)) {
        $AdditionalBuildXLArguments += " /p:BUILDXL_ANYBUILD_CLIENT_INSTALL_DIR=`"$AnyBuildClientDir`""
    }
}

if ($Deploy -eq "LKG") {
    throw "The LKG deployment is special since it comes from a published NuGet package. It cannot be re-deployed in this selfhost wrapper.";
}

function Get-CacheMissArgs {
    # Adds arguments to reference fingerprintstores corresponding to the last 3 commits.
    # Argument is of the form: /cachemiss:[commit123456:commit0abcdef:commit044839]
    # This ideally allows retrieval of the fingerprint store for the most recent close build to the current
    # state of the repo.
    $cacheMissArgs = "";
    $output = git log --first-parent -n 3 --pretty=format:%H
    
    $cacheMissArgs += "/CacheMiss:[";
    foreach ($item in $output.Split(" "))
    {
        $cacheMissArgs += "commit";
        $cacheMissArgs += $item;
        $cacheMissArgs += ":";
    }

    $cacheMissArgs = $cacheMissArgs.TrimEnd(":");
    $cacheMissArgs += "]";

    return $cacheMissArgs;
}

function New-Deployment {
    param([string]$Root, [string]$Name, [string]$Description, [string]$TelemetryEnvironment, [string]$dir = $null, [bool]$enableServerMode = $false, [string]$DeploymentRoot);

    $serverDeploymentDir = Join-Path $Root "Out\Selfhost\$name.ServerDeployment"

    if (! $dir) {
        $dir = Join-Path $Root "Out\Selfhost\$name";
    }

    $buildRelativeDir = [io.path]::combine($DeploymentRoot, $DeployConfig, $DeployRuntime)
    if ($DeployRuntime -ne "win-x64") {
        # Handling .net 5 differently, because the old scheme is not suitable for having dev deployments with different qualifiers.
        $framework = $DeployRuntime;
        $DeployRuntime = "win-x64";
        $buildRelativeDir = [io.path]::combine($DeploymentRoot, $DeployConfig, $framework, $DeployRuntime)
    }
    
    return @{
        description = $Description;
        dir = $dir;
        domino = Join-Path $dir $BuildXLExeName;
        dominoRunner = Join-Path $dir $BuildXLRunnerExeName;
        nugetDownloader = Join-Path $dir $NugetDownloaderName
        buildDir = Join-Path $Root $buildRelativeDir;
        enableServerMode = $enableServerMode;
        telemetryEnvironment = $TelemetryEnvironment;
        serverDeploymentDir = $serverDeploymentDir;
    };
}

function Write-CacheConfigJson {
    param([string]$ConfigPath, [bool]$UseSharedCache, [bool]$PublishToSharedCache, [string]$VsoAccount, [string]$CacheNamespace);

    $configOptions = Get-CacheConfig -UseSharedCache $UseSharedCache -PublishToSharedCache $PublishToSharedCache -VsoAccount $VsoAccount -CacheNamespace $CacheNamespace;
    Set-Content -Path $configPath -Value (ConvertTo-Json $configOptions)
}

function Get-CacheConfig {
    param([bool]$UseSharedCache, [bool]$PublishToSharedCache, [string]$VsoAccount, [string]$CacheNamespace);
    
    $localCache = @{
         Assembly = "BuildXL.Cache.MemoizationStoreAdapter";
         Type = "BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory";
         CacheId = "SelfhostCS2L1";
         MaxCacheSizeInMB = 20240;
         CacheRootPath = $cacheDirectory;
         CacheLogPath = "[BuildXLSelectedLogPath]";
         UseStreamCAS = $true;
         UseRocksDbMemoizationStore = $true;
    };

    if ($UseVfs) {
        $localCache.Add("VfsCasRoot", "[VfsCasRoot]");
    }

    if (! $UseSharedCache) {
        return $localCache;
    }

    $remoteCache = @{
        Assembly = "BuildXL.Cache.BuildCacheAdapter";
        Type = "BuildXL.Cache.BuildCacheAdapter.BuildCacheFactory";
        CacheId = "L3Cache";
        CacheLogPath = "[BuildXLSelectedLogPath].Remote.log";
        CacheServiceFingerprintEndpoint = "https://$VsoAccount.artifacts.visualstudio.com";
        CacheServiceContentEndpoint = "https://$VsoAccount.vsblob.visualstudio.com";
        UseBlobContentHashLists = $true;
        CacheNamespace = $CacheNamespace;
        DownloadBlobsUsingHttpClient = $true;
        RequiredContentKeepUntilHours = 1;
    };

    if ($env:BUILDXL_VSTS_REMOTE_FINGERPRINT_ENDPOINT) {
        $remoteCache.CacheServiceFingerprintEndpoint = $env:BUILDXL_VSTS_REMOTE_FINGERPRINT_ENDPOINT;
        Write-Host "Using " $remoteCache.CacheServiceFingerprintEndpoint
    }

    if ($env:BUILDXL_VSTS_REMOTE_CONTENT_ENDPOINT) {
        $remoteCache.CacheServiceContentEndpoint = $env:BUILDXL_VSTS_REMOTE_CONTENT_ENDPOINT;
        Write-Host "Using " $remoteCache.CacheServiceContentEndpoint
    }
    
    <# TODO: After unifying flags, remove if statement and hard-code dummy value into remoteCache #>
    if ($UseDedupStore) {
        $remoteCache.Add("UseDedupStore", $true);
    }

    if ($UseBlobL3) {
        $remoteCache = @{
            Assembly = "BuildXL.Cache.MemoizationStoreAdapter";
            Type = "BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory";
            CacheId = "L3Cache";
            CacheLogPath = "[BuildXLSelectedLogPath].Remote.log";
            ContainerName = $CacheNamespace;
        };
    }

    $resultCache = @{
        Assembly = "BuildXL.Cache.VerticalAggregator";
        Type = "BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory";
        RemoteIsReadOnly = !($PublishToSharedCache);
        RemoteContentIsReadOnly = !($PublishToSharedCache);
        WriteThroughCasData = $PublishToSharedCache;
        LocalCache = $localCache;
        RemoteCache = $remoteCache;
        RemoteConstructionTimeoutMilliseconds = 36000;
        SkipDeterminismRecovery = $true;
        FailIfRemoteFails = $true;
    };

    return $resultCache;
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

function Run-ProcessWithoutNormalizedPath {
    param([string]$executable, [string[]]$processArgs);
    Write-Host -ForegroundColor Green $executable $processArgs;
    $p = Start-Process -FilePath $executable -ArgumentList $processArgs -WorkingDirectory (pwd).Path -NoNewWindow -PassThru;
    Wait-Process -InputObject $p;
    return $p.ExitCode;
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

    robocopy /MT /MIR /NJH /NP /NDL /NFL /NC /NS $src $dst /xd BuildXLServerDeploymentCache;
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
    Dev = New-Deployment -Root $enlistmentRoot -Name "Dev" -Description "dev (locally-built)" -TelemetryEnvironment "SelfHostPrivateBuild" -Dir $DevRoot -EnableServerMode $false -DeploymentRoot $DominoDeploymentRoot;
    RunCheckinTests = New-Deployment -Root $enlistmentRoot -Name "RunCheckinTests" -Description "checkin-validation"  -TelemetryEnvironment "SelfHostPrivateBuild" -EnableServerMode $true -DeploymentRoot $DominoDeploymentRoot;
    RunCheckinTestSamples = New-Deployment -Root $enlistmentRoot -Name "RunCheckinTestSamples" -Description "checkin-validation-samples"  -TelemetryEnvironment "SelfHostPrivateBuild" -DeploymentRoot $DominoDeploymentRoot;
    ChangeJournalService = New-Deployment -Root $enlistmentRoot -Name "ChangeJournalService" -Description "change journal service"  -TelemetryEnvironment "SelfHostPrivateBuild" -DeploymentRoot $DominoDeploymentRoot;
};

$shouldDeploy = $Deploy -ne $null -and $Deploy -ne "";


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
    elseif ($DeployRuntime -eq "linux-x64") {
        $AdditionalBuildXLArguments += "/q:ReleaseLinux"
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
    elseif ($DeployRuntime -eq "linux-x64") {
        $AdditionalBuildXLArguments += "/q:DebugLinux"
    }
}

$useDeployment = Get-Deployment $use;
Log "> BuildXL Selfhost wrapper - run 'domino -SelfhostHelp' for more info";
Log -NoNewline "Building using the ";
Log-Emphasis -NoNewline $($useDeployment.description)
Log " version of BuildXL.";

$Nuget_CredentialProviders_Path = [Environment]::GetEnvironmentVariable("NUGET_CREDENTIALPROVIDERS_PATH").TrimEnd('\\'); 

$AdditionalBuildXLArguments += "/environment:$($useDeployment.telemetryEnvironment) /unsafe_GlobalUntrackedScopes:""$Nuget_CredentialProviders_Path"" /unsafe_GlobalPassthroughEnvVars:NUGET_CREDENTIALPROVIDERS_PATH /remoteCacheCutoff+ /remoteCacheCutoffLength:2";

$GenerateCgManifestFilePath = "$enlistmentRoot\cg\nuget\cgmanifest.json";
$AdditionalBuildXLArguments += "/generateCgManifestForNugets:$GenerateCgManifestFilePath";
$AdditionalBuildXLArguments += Get-CacheMissArgs;

if (! $DoNotUseDefaultCacheConfigFilePath) {

    $cacheConfigPath = (Join-Path $cacheDirectory CacheCore.json);
    Write-CacheConfigJson -ConfigPath $cacheConfigPath -UseSharedCache $useSharedCache -PublishToSharedCache $publishToSharedCache -VsoAccount $VsoAccount -CacheNamespace $CacheNamespace;

    $AdditionalBuildXLArguments += "/cacheConfigFilePath:" + $cacheConfigPath;
}

if ($UseVfs) {
    $AdditionalBuildXLArguments += "/vfsCasRoot:" + (Join-Path $cacheDirectory vfs);
}

if ($useDeployment.EnableServerMode) {
    Log "Server mode enabled."
    $AdditionalBuildXLArguments += "/server"
    $AdditionalBuildXLArguments += "/serverDeploymentDir:$($useDeployment.serverDeploymentDir)"
}

if ($shouldDeploy) {
    $deployDeployment = Get-Deployment $Deploy;
    Log "The newly-built BuildXL will be deployed as the $($deployDeployment.description) version at $($deployDeployment.buildDir).";
}

if ($Minimal) {
    Log "The newly-built BuildXL will not be fully validated because you are running with -Minimal.";
}

if (! (Test-Path -PathType Leaf $useDeployment.domino)) {
    throw "The BuildXL executable was not found at $($useDeployment.domino). Maybe you need to build and deploy with -Deploy $Use first?";
}

if ($shouldDeploy -and $shouldClean) {
    Log "Cleaning output directory (needed to prevent extra files from being deployed)";
    # Note that we are deleting the temporary deployment location (not the one we would execute); otherwise
    # a deployment couldn't replace itself.

    if (Test-Path -PathType Container $deployDeployment.buildDir) {
        rmdir -Recurse $deployDeployment.buildDir;
    }
}

if (-not $DisableInteractive) {
    $AdditionalBuildXLArguments += "/Interactive+"
}

# let any freeform filter arguments take precedence over the default filter
$skipFilter = $false;
for($i = 0; $i -lt $DominoArguments.Count; $i++){
    if (!$DominoArguments[$i].StartsWith('-') -and !$DominoArguments[$i].StartsWith('/')){
        $skipFilter = $true;
    }
}

if (!$skipFilter) {

    $AllCacheProjectsFilter = "(spec='Public\Src\Cache\*')";
    $CacheNugetFilter = "spec='Public\Src\Deployment\cache.nugetpackages.dsc'";
    $CacheOutputFilter = "output='out\bin\$DeployConfig\cache\*'";
    $CacheLongRunningFilter = "tag='LongRunningTest'";
    $PrivateNugetFilter = "spec='Public\src\Deployment\privatePackages.dsc'";
    $IdeFilter = "spec='Public\src\Deployment\ide.dsc'";
    $TestDeploymentFilter = "spec='Public\src\Deployment\tests.dsc'";
    $PrivateWdgFilter = "dpt(spec='private\Guests\WDG\*')";

    if ($Minimal) {
        # filtering by core deployment.
        $AdditionalBuildXLArguments += "/f:(output='$($useDeployment.buildDir)\*'or(output='out\bin\$DeployConfig\Sdk\*')or($CacheOutputFilter))and~($CacheLongRunningFilter)"
    }

    if ($Cache) {
        # Only build Cache code.
        if ($LongRunningTest -or $Vs) {
            $AdditionalBuildXLArguments += "/f:$AllCacheProjectsFilter"
        }
        elseif ($CacheNuget) {
            $AdditionalBuildXLArguments += "/f:($CacheNugetFilter)"
        }
        elseif (!$Minimal) {
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

    if ($Bvfs) {
        $AdditionalBuildXLArguments += "/q:DebugNet472 /f:output='out/bin/debug/tools/bvfs/*'"
    }
}

if ($Analyze) {
    $AdditionalBuildXLArguments = @()
    $DominoArguments.RemoveAt(0);
}

if ($env:BUILDXL_ADDITIONAL_DEFAULTS)
{
    $AdditionalBuildXLArguments += $env:BUILDXL_ADDITIONAL_DEFAULTS
}

if ($isRunningOnADO)
{
    # On ADO, let's make sure we scrub stale files to avoid CG issues on unused packages
    # Nuget packages go under the Object directory (and nuspec files downloaded as part of the inpection process under the frontend\Nuget folder).
    # The download resolver places the downloads under frontend/Download.
    # Observe that frontend/Nuget only contains .nuspecs (and hash.txt files), so no need to scrub anything there.
    $AdditionalBuildXLArguments += "/scrub:Out\Objects /scrub:Out\frontend\Download /scrub:Out\frontend\Nuget\pkgs";
}

[string[]]$DominoArguments = @($DominoArguments |% { $_.Replace("#singlequote#", "'").Replace("#openparens#", "(").Replace("#closeparens#", ")"); })
[string[]]$DominoArguments = $AdditionalBuildXLArguments + $DominoArguments;

# The MS internal build needs authentication. When not running on ADO use the configured cred provider
# to prompt for credentials as a way to guarantee the auth token will be cached for the subsequent build.
# This may prompt an interactive pop-up/console. ADO pipelines already configure the corresponding env vars 
# so there is no need to do this on that case. Once the token is cached, launching the provider shouldn't need
# any user interaction
if ($isMicrosoftInternal -and (-not $isRunningOnADO)) {
    # Search for the provider executable under ther specified directory
    $credProvider = Get-ChildItem $Nuget_CredentialProviders_Path\* -File -Include CredentialProvider*.exe | Select-Object -First 1

    # CODESYNC: config.dsc. The URI needs to match the (single) feed used for the internal build
    $internalFeed = "https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json"

    Log "Launching the credential provider with (maybe required) user interaction."
    # Launch the provider making sure we allow for UI (C option) and that the token gets redacted from the console (R option)
    $credProviderArguments = "-U $internalFeed -V Information -C -R"
    $p = Start-Process -FilePath $credProvider -NoNewWindow -Wait -PassThru -ArgumentList $credProviderArguments;

    if (-not ($p.ExitCode -eq 0))
    {
        Log-Error "Failed authentication using the specified credential provider.";
        $host.SetShouldExit($p.ExitCode);
        return $p.ExitCode;
    }

    # Now validate the cached token is good. Since we called the provider without -IsRetry (with the hope that an auth token is already cached), the provider
    # might succeed but return an expired token. Use the nuget downloader with the option /onlyAuthenticate to verify this
    Log "Validating credential provider execution."
    $p = Start-Process $useDeployment.nugetDownloader -NoNewWindow -Wait -PassThru -ArgumentList "/repositories:BuildXL=$internalFeed /onlyAuthenticate" -RedirectStandardOutput "Out/Logs/interactive-auth.log"

    if (-not ($p.ExitCode -eq 0))
    {
        Log "Authentication failed. The credential provider may have returned an invalid auth token. Calling it again with -IsRetry to bypass caching";
        $p = Start-Process -FilePath $credProvider -NoNewWindow -Wait -PassThru -ArgumentList "$credProviderArguments -IsRetry";
        
        if (-not ($p.ExitCode -eq 0))
        {
            Log-Error "Failed authentication using the specified credential provider.";
            $host.SetShouldExit($p.ExitCode);
            return $p.ExitCode;
        }
    }

    Log "Authentication was successful."
}

if ($NoSubst) {
    $bxlExitCode = Run-ProcessWithoutNormalizedPath $useDeployment.domino $DominoArguments;
} else {
    $bxlExitCode = Run-ProcessWithNormalizedPath $useDeployment.dominoRunner $useDeployment.domino $DominoArguments;
}
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
