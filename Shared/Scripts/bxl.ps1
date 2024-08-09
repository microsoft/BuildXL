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


.\bxl -DeployDev -DeployConfig "release" -DeployRuntime "net8.0"

Uses the LKG deployment to update the Dev deployment with net8.0 release binaries 

.EXAMPLE

bxl -Deploy Dev -DeployConfig Debug -Minimal

Uses the LKG deployment to update the Dev deployment with Debug binaries, skipping unittest and other tools that are not part of the core deployment.
.EXAMPLE

bxl -Use Dev -Deploy Dev

Uses the Dev deployment to update the Dev deployment

#>

[CmdletBinding(PositionalBinding = $false)]
param(
    [switch]$SelfhostHelp,

    [ValidateSet("LKG", "Dev", "RunCheckinTests", "RunCheckinTestSamples", "ChangeJournalService")]
    [string]$Use = "LKG",

    [Parameter(Mandatory = $false)]
    [string]$DevRoot,

    [ValidateSet("Release", "Debug")]
    [string]$DeployConfig = "Debug", # must match defaultQualifier.configuration in config.dsc 

    [ValidateSet("net472", "net6.0", "net8.0", "win-x64", "osx-x64")]
    [string]$DeployRuntime = "win-x64", # must correspond to defaultQualifier.targetFramework in config.dsc 

    [Parameter(Mandatory = $false)]
    [string]$DominoDeploymentRoot = "Out\Bin",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Dev", "RunCheckinTests", "RunCheckinTestSamples", "ChangeJournalService")]
    [string]$Deploy,

    [Parameter(Mandatory = $false)]
    [string]$TestMethod = "",

    [Parameter(Mandatory = $false)]
    [string]$TestClass = "",

    [Parameter(Mandatory = $false)]
    [ValidateSet("Disable", "Consume", "ConsumeAndPublish")]
    [string]$SharedCacheMode = "Consume",

    [Parameter(Mandatory = $false)]
    [switch]$DevCache = $false,

    [Parameter(Mandatory = $false)]
    [string]$DefaultConfig,

    [Parameter(Mandatory = $false)]
    [switch]$UseAdoBuildRunner = $false,

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

    [switch]$DisableInteractive = $false,

    [switch]$DoNotUseDefaultCacheConfigFilePath = $false,

    [Parameter(Mandatory = $false)]
    [switch]$UseDedupStore = $false,

    [ValidateSet("Disabled", "Build", "Datacenter")]
    [string]$UseEphemeralCache = "Disabled",

    [Parameter(Mandatory = $false)]
    [switch]$UseBlobL3 = $false,

    [string]$VsoAccount = "mseng",

    [string]$CacheNamespace = "buildxlselfhost",

    [Parameter(Mandatory = $false)]
    [switch]$Vs = $false,

    [Parameter(Mandatory = $false)]
    [switch]$VsAll = $false,

    [Parameter(Mandatory = $false)]
    [switch]$UseManagedSharedCompilation = $true,

    [Parameter(Mandatory = $false)]
    [switch]$NoQTest = $false,

    [switch]$NoSubst = $false,

    [Parameter(Mandatory = $false)]
    [switch]$EnableProcessRemoting = $false,

    [Parameter(Mandatory = $false)]
    [string]$AnyBuildClientDir,

    [Parameter(Mandatory = $false)]
    [string]$GenerateFlagsMd = $true,

    [Parameter(Mandatory = $false)]
    [string]$ProduceResponseFile,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DominoArguments
)

$ErrorActionPreference = "Stop";
Set-StrictMode -Version Latest;

if ($GenerateFlagsMd) {
    & "$(Get-Location)/Shared/Scripts/HelpTextToMarkdown.ps1" -ResxFile "$(Get-Location)/Public/Src/App/Bxl/Strings.resx" -Output "$(Get-Location)/Documentation/Wiki/Flags.md"
}

# Drive letter used as a canonical enlistment root.
$NormalizationDrive = "B:";
$NormalizationDriveLetter = "B";

# Since we don't have process-scoped drive letters, we have to have a locking scheme for usage ofr $NormalizationDrive.
# We keep a special file under Out\ that acts as a lock; this script and also OSGTool's bbuild hold on to this file as an indication
# that the $NormalizationDrive is in use by a build and shouldn't be remapped.
$NormalizationLockRelativePath = "Out\.NormalizationLock"

# TF_BUILD is an environment variable which is always present when running on ADO
$tfBuild = [Environment]::GetEnvironmentVariable("TF_BUILD")
[bool] $isRunningOnADO = If ($tfBuild -eq $null) { $false } Else { $tfBuild }

# These are the options added unless -Vanilla is specified.
$NonVanillaOptions = @("/IncrementalScheduling", "/nowarn:909 /nowarn:11318 /nowarn:11319 /unsafe_IgnorePreloadedDlls- /historicMetadataCache+");
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

if ($Deploy -and $ProduceResponseFile) {
    throw "-ProduceResponseFile is incompatible with -Deploy"
}

$BuildXLExeName = "bxl.exe";
$BuildXLRunnerExeName = "RunInSubst.exe";
$AdoBuildRunnerExeName = "AdoBuildRunner.exe";
$NugetDownloaderName = "NugetDownloader.exe";

if ($Analyze) {
    $BuildXLExeName = "bxlanalyzer.exe";
}


if (($DominoArguments -match "/c(onfig)?:.*").Length -eq 0) {
    if ($DefaultConfig) {
        $AdditionalBuildXLArguments += "/config:$DefaultConfig";
    }
}

if (($DominoArguments -match "/p:BUILDXL_FINGERPRINT_SALT.*").Length -eq 0) {
    # A casing related PR polluted the cache, so let's force a salt. This could be removed after the poisoned content gets evicted.
    $AdditionalBuildXLArguments += "/p:BUILDXL_FINGERPRINT_SALT=casingPR";
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
    }
    else {
        # by default (-vs) we build only .NET Core and only projects targeting one of the .NET Core frameworks
        $AdditionalBuildXLArguments += "/q:Debug /vsTargetFramework:netstandard2.0 /vsTargetFramework:netstandard2.1 /vsTargetFramework:net6.0 /vsTargetFramework:net8.0";
    }
}

# Various tools consume language pack files under this path if they are installed. Untrack them to prevent DFAs in local builds
$AdditionalBuildXLArguments += @("/unsafe_GlobalUntrackedScopes:""C:\Program Files\WindowsApps""");

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

function New-Deployment {
    param([string]$Root, [string]$Name, [string]$Description, [string]$TelemetryEnvironment, [string]$dir = $null, [bool]$enableServerMode = $false, [string]$DeploymentRoot);

    $serverDeploymentDir = Join-Path $Root "Out\Selfhost\$name.ServerDeployment"

    if (! $dir) {
        $dir = Join-Path $Root "Out\Selfhost\$name";
    }

    $buildRelativeDir = [io.path]::combine($DeploymentRoot, $DeployConfig, $DeployRuntime)
    if ($DeployRuntime -ne "win-x64") {
        # If it's not a default runtime, we handle it differently because the old scheme is not suitable for having dev deployments with different qualifiers.
        $framework = $DeployRuntime;
        $DeployRuntime = "win-x64";
        $buildRelativeDir = [io.path]::combine($DeploymentRoot, $DeployConfig, $framework, $DeployRuntime)
    }
    
    return @{
        description          = $Description;
        dir                  = $dir;
        domino               = Join-Path $dir $BuildXLExeName;
        adoBuildRunner       = Join-Path $dir $AdoBuildRunnerExeName;
        nugetDownloader      = Join-Path $dir $NugetDownloaderName
        buildDir             = Join-Path $Root $buildRelativeDir;
        enableServerMode     = $enableServerMode;
        telemetryEnvironment = $TelemetryEnvironment;
        serverDeploymentDir  = $serverDeploymentDir;
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
        Assembly                   = "BuildXL.Cache.MemoizationStoreAdapter";
        Type                       = "BuildXL.Cache.MemoizationStoreAdapter.MemoizationStoreCacheFactory";
        CacheId                    = "SelfhostCS2L1";
        MaxCacheSizeInMB           = 40480;
        CacheRootPath              = $cacheDirectory;
        CacheLogPath               = "[BuildXLSelectedLogPath]";
        UseStreamCAS               = $true;
        UseRocksDbMemoizationStore = $true;
    };

    if (!$UseSharedCache) {
        return $localCache;
    }

    $ephemeralCache = @{
        Assembly              = "BuildXL.Cache.MemoizationStoreAdapter";
        CacheLogPath          = "[BuildXLSelectedLogPath]";
        Type                  = "BuildXL.Cache.MemoizationStoreAdapter.EphemeralCacheFactory";
        CacheId               = "L3Cache";
        Universe              = $CacheNamespace;
        RetentionPolicyInDays = 1;
        CacheRootPath         = "[BuildXLSelectedRootPath]";
        LeaderMachineName     = "[BuildXLSelectedLeader]";
        CacheSizeMb           = 20240;
        DatacenterWide        = $UseEphemeralCache -eq "Datacenter";
    };
    if ($UseEphemeralCache -ne "Disabled") {
        return $ephemeralCache;
    }

    if ($UseBlobL3) {
        $remoteCache = @{
            Assembly              = "BuildXL.Cache.MemoizationStoreAdapter";
            Type                  = "BuildXL.Cache.MemoizationStoreAdapter.BlobCacheFactory";
            CacheId               = "L3Cache";
            CacheLogPath          = "[BuildXLSelectedLogPath].Remote.log";
            Universe              = $CacheNamespace;
            RetentionPolicyInDays = 1;
        };
    }
    else {
        $remoteCache = @{
            Assembly                        = "BuildXL.Cache.BuildCacheAdapter";
            Type                            = "BuildXL.Cache.BuildCacheAdapter.BuildCacheFactory";
            CacheId                         = "L3Cache";
            CacheLogPath                    = "[BuildXLSelectedLogPath].Remote.log";
            CacheServiceFingerprintEndpoint = "https://$VsoAccount.artifacts.visualstudio.com";
            CacheServiceContentEndpoint     = "https://$VsoAccount.vsblob.visualstudio.com";
            UseBlobContentHashLists         = $true;
            CacheNamespace                  = $CacheNamespace;
            DownloadBlobsUsingHttpClient    = $true;
            RequiredContentKeepUntilHours   = 1;
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
    }

    $resultCache = @{
        Assembly                              = "BuildXL.Cache.VerticalAggregator";
        Type                                  = "BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory";
        RemoteIsReadOnly                      = !($PublishToSharedCache);
        RemoteContentIsReadOnly               = !($PublishToSharedCache);
        WriteThroughCasData                   = $PublishToSharedCache;
        LocalCache                            = $localCache;
        RemoteCache                           = $remoteCache;
        RemoteConstructionTimeoutMilliseconds = 36000;
        SkipDeterminismRecovery               = $true;
        FailIfRemoteFails                     = $true;
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

function Remap-PathToNormalizedDrive {
    param([string[]]$paths);

    return $paths -replace ([Regex]::Escape($enlistmentRoot.TrimEnd("\")) + "(\\|\z)"), ($NormalizationDrive + "\");
}


function Get-SubstArguments {
    param([string[]]$processArgs);
    $enlistmentrootTrimmed = $enlistmentRoot.TrimEnd('\')
    [string[]]$remappedArgs = @("/runInSubst");
    $remappedArgs += @(Remap-PathToNormalizedDrive $processArgs);
    $remappedArgs += "/substTarget:$NormalizationDrive /substSource:$enlistmentrootTrimmed"
    $remappedArgs += " /logProcessDetouringStatus+ /logProcessData+ /logProcesses+";
    if ($ProduceResponseFile) {
        return $remappedArgs
    }
    else {
        return $remappedArgs.Replace("""", "\""")
    }
}

function Run-Process {
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
    LKG                   = New-Deployment -Root $enlistmentRoot -Name "LKG" -Description "LKG (published NuGet package)" -TelemetryEnvironment "SelfhostLKG" -Dir $lkgDir -EnableServerMode $true -DeploymentRoot $DominoDeploymentRoot;
    Dev                   = New-Deployment -Root $enlistmentRoot -Name "Dev" -Description "dev (locally-built)" -TelemetryEnvironment "SelfHostPrivateBuild" -Dir $DevRoot -EnableServerMode $false -DeploymentRoot $DominoDeploymentRoot;
    RunCheckinTests       = New-Deployment -Root $enlistmentRoot -Name "RunCheckinTests" -Description "checkin-validation"  -TelemetryEnvironment "SelfHostPrivateBuild" -EnableServerMode $true -DeploymentRoot $DominoDeploymentRoot;
    RunCheckinTestSamples = New-Deployment -Root $enlistmentRoot -Name "RunCheckinTestSamples" -Description "checkin-validation-samples"  -TelemetryEnvironment "SelfHostPrivateBuild" -DeploymentRoot $DominoDeploymentRoot;
    ChangeJournalService  = New-Deployment -Root $enlistmentRoot -Name "ChangeJournalService" -Description "change journal service"  -TelemetryEnvironment "SelfHostPrivateBuild" -DeploymentRoot $DominoDeploymentRoot;
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
    elseif ($DeployRuntime -eq "net6.0") {
        $AdditionalBuildXLArguments += "/q:ReleaseDotNet6"
    }
    elseif ($DeployRuntime -eq "net8.0") {
        $AdditionalBuildXLArguments += "/q:ReleaseNet8"
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
}
else {
    if ($DeployRuntime -eq "net472") {
        $AdditionalBuildXLArguments += "/q:DebugNet472"
    }
    elseif ($DeployRuntime -eq "net6.0") {
        $AdditionalBuildXLArguments += "/q:DebugDotNet6"
    }
    elseif ($DeployRuntime -eq "net8.0") {
        $AdditionalBuildXLArguments += "/q:DebugNet8"
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

if (! $UseAdoBuildRunner) # AdoBuildRunner itself enables cache miss with a specific key
{
    $AdditionalBuildXLArguments += "/cacheMiss+";
}

if (! $DoNotUseDefaultCacheConfigFilePath) {

    $cacheConfigPath = (Join-Path $cacheDirectory CacheCore.json);
    Write-CacheConfigJson -ConfigPath $cacheConfigPath -UseSharedCache $useSharedCache -PublishToSharedCache $publishToSharedCache -VsoAccount $VsoAccount -CacheNamespace $CacheNamespace;

    $AdditionalBuildXLArguments += "/cacheConfigFilePath:" + $cacheConfigPath;
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
for ($i = 0; $i -lt $DominoArguments.Count; $i++) {
    if (!$DominoArguments[$i].StartsWith('-') -and !$DominoArguments[$i].StartsWith('/')) {
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
        $AdditionalBuildXLArguments += "/f:~($CacheLongRunningFilter)and~($CacheNugetFilter)"
    }
}

if ($Analyze) {
    $AdditionalBuildXLArguments = @()
    $DominoArguments.RemoveAt(0);
}

if ($env:BUILDXL_ADDITIONAL_DEFAULTS) {
    $AdditionalBuildXLArguments += $env:BUILDXL_ADDITIONAL_DEFAULTS
}

if ($isRunningOnADO) {
    # On ADO, let's make sure we scrub stale files to avoid CG issues on unused packages
    # Nuget packages go under the Object directory (and nuspec files downloaded as part of the inpection process under the frontend\Nuget folder).
    # The download resolver places the downloads under frontend/Download.
    # Observe that frontend/Nuget only contains .nuspecs (and hash.txt files), so no need to scrub anything there.
    $AdditionalBuildXLArguments += "/scrubDirectory:Out\Objects /scrubDirectory:Out\frontend\Download /scrubDirectory:Out\frontend\Nuget\pkgs";
}

[string[]]$DominoArguments = @($DominoArguments | % { $_.Replace("#singlequote#", "'").Replace("#openparens#", "(").Replace("#closeparens#", ")"); })
[string[]]$DominoArguments = $AdditionalBuildXLArguments + $DominoArguments;

function getTokenFromCredentialProvider() {
    [OutputType([string])]
    Param (
        [parameter(Mandatory=$true)]
        [string]
        $credProvider,

        [parameter(Mandatory=$true)]
        [string]
        $internalFeed,

        [parameter(Mandatory=$true)]
        [boolean]
        $isRetry
    )

    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = $credProvider
    $processInfo.RedirectStandardError = $true
    $processInfo.RedirectStandardOutput = $true
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $processInfo.Arguments = "-U $internalFeed -V Information -C -F Json"
    if ($isRetry) {
        $processInfo.Arguments += " -I"
    }
    # tells the artifacts cred provider to generate a PAT instead of a self-describing token
    if ($processInfo.EnvironmentVariables.ContainsKey("NUGET_CREDENTIALPROVIDER_VSTS_TOKENTYPE")) {
        $processInfo.EnvironmentVariables["NUGET_CREDENTIALPROVIDER_VSTS_TOKENTYPE"] = "Compact"
    }
    else {
        $processInfo.EnvironmentVariables.Add("NUGET_CREDENTIALPROVIDER_VSTS_TOKENTYPE", "Compact")
    }
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $processInfo
    $p.Start() | Out-Null
    $p.WaitForExit()
    $stdout = $p.StandardOutput.ReadToEnd()

    # parse token from output
    $tokenMatches = $stdout | Select-String -Pattern '.*\{"Username":"[a-zA-Z0-9]*","Password":"(.*)"\}.*'
    return $tokenMatches.Matches.Groups[1].Value
}

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

    if (-not ($p.ExitCode -eq 0)) {
        Log-Error "Failed authentication using the specified credential provider.";
        $host.SetShouldExit($p.ExitCode);
        return $p.ExitCode;
    }

    # Now validate the cached token is good. Since we called the provider without -IsRetry (with the hope that an auth token is already cached), the provider
    # might succeed but return an expired token. Use the nuget downloader with the option /onlyAuthenticate to verify this
    Log "Validating credential provider execution."
    $p = Start-Process $useDeployment.nugetDownloader -NoNewWindow -Wait -PassThru -ArgumentList "/repositories:BuildXL=$internalFeed /onlyAuthenticate" -RedirectStandardOutput "Out/Logs/interactive-auth.log"

    if (-not ($p.ExitCode -eq 0)) {
        Log "Authentication failed. The credential provider may have returned an invalid auth token. Calling it again with -IsRetry to bypass caching";
        $p = Start-Process -FilePath $credProvider -NoNewWindow -Wait -PassThru -ArgumentList "$credProviderArguments -IsRetry";
        
        if (-not ($p.ExitCode -eq 0)) {
            Log-Error "Failed authentication using the specified credential provider.";
            $host.SetShouldExit($p.ExitCode);
            return $p.ExitCode;
        }
    }

    Log "Authentication was successful."

    # Set the cached credential into the user npmrc
    # we don't run vsts-npm-auth here because it requires us to have npm installed first
    # the code below will essentially duplicate what vsts-npm-auth performs
    $token = getTokenFromCredentialProvider $credProvider $internalFeed $false;

    # verify whether the provided PAT is valid
    $auth = "username" + ':' + $token
    $encoded = [System.Text.Encoding]::UTF8.GetBytes($auth)
    $authorizationInfo = [System.Convert]::ToBase64String($encoded)
    $headers = @{"Authorization"="Basic $($authorizationInfo)"}
    $statusCode = 0;

    try {
        $response = Invoke-WebRequest -Uri "https://feeds.dev.azure.com/cloudbuild/CloudBuild/_apis/packaging/feeds?api-version=7.1-preview.1" -Method GET -Headers $headers
        $statusCode = $response.StatusCode
    }
    catch {
        $statusCode = 401
    }

    # a 200 response indicates that the PAT is valid
    # if we didn't get this, we need to re-run the credential provider with the -I arg
    if ($statusCode -ne 200) {
        $token = getTokenFromCredentialProvider $credProvider $internalFeed $true;
    }

    # npmrc files can contain multiple sources, so we'll create a buildxl specific one here
    if (![System.IO.File]::Exists("$env:USERPROFILE/.npmrc")) {
        New-Item -Path "$env:USERPROFILE/.npmrc" -ItemType File
    }
    else {
        Set-Content -Path "$env:USERPROFILE/.npmrc" -Value (Get-Content "$env:USERPROFILE/.npmrc" | Select-String -Pattern '.*\/\/cloudbuild\.pkgs\.visualstudio\.com\/_packaging\/BuildXL\.Selfhost\/npm\/registry.*' -NotMatch)
    }

    # base64 encode the token
    $b64token = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($token))

    # add new lines with token to npmrc
    Add-Content -Path "$env:USERPROFILE/.npmrc" -Value "//cloudbuild.pkgs.visualstudio.com/_packaging/BuildXL.Selfhost/npm/registry/:username=VssSessionToken" -Encoding UTF8
    Add-Content -Path "$env:USERPROFILE/.npmrc" -Value "//cloudbuild.pkgs.visualstudio.com/_packaging/BuildXL.Selfhost/npm/registry/:_password=$b64token" -Encoding UTF8
    Add-Content -Path "$env:USERPROFILE/.npmrc" -Value "//cloudbuild.pkgs.visualstudio.com/_packaging/BuildXL.Selfhost/npm/registry/:email=not-used@example.com" -Encoding UTF8
}

$executable = $useDeployment.domino;
if ($NoSubst) {
    $arguments = $DominoArguments
}
else {
    # Wrap the invocation with RunInSubst
    $arguments = Get-SubstArguments $DominoArguments
}

if ($UseAdoBuildRunner) {
    # Wrap the invocation with the AdoBuildRunner
    $arguments = $arguments.Replace("\""", "\\\""")
    $executable = $useDeployment.adoBuildRunner
}

if ($ProduceResponseFile) {
    # Don't run BuildXL - instead, produce a file with the arguments
    Log "Creating response file at $ProduceResponseFile"
    # A response file has to have one argument per line, make sure
    # that we're doing that correctly by splitting the flat arguments
    # that we constructed above by '/' and reconstructing them separately
    $sanitizedRspArgs = @()
    foreach ($arg in $arguments) {
      $maybeMultipleArgs = $arg -split "/"
      foreach ($individualArg in $maybeMultipleArgs) 
      {
        $individualArg = $individualArg.Trim();
        if ($individualArg -ne ""){
           $sanitizedRspArgs += "/$individualArg"
        }
      }
    }

    New-Item -ItemType File -Path $ProduceResponseFile -Force | Out-Null
    Set-Content -Path $ProduceResponseFile -Value $sanitizedRspArgs
    Log "Contents:"
    # Print contents indented with 4 spaces
    (Get-Content -Path $ProduceResponseFile -Raw) -replace '(?m)^', '    '
    Log "Created response file with the selected arguments at $ProduceResponseFile"
}
else {
    # Run BuildXL
    $bxlExitCode = Run-Process $executable $arguments;
    $bxlSuccess = ($bxlExitCode -eq 0);
    if ($bxlSuccess) {
        Log "Done.";
    }
    else {
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
        }
        else {
            Log-Error "Deployment cancelled since the build failed; see above."
        }
    }
    
    if (! $bxlSuccess) {
        $host.SetShouldExit($bxlExitCode);
    }    
}
