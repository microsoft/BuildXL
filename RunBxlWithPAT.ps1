<#
.SYNOPSIS

Script for BuildXL self-hosting with specified PATs. This script is used to perform BuildXL self-hosting in Azure pipeline.

#>

[CmdletBinding(PositionalBinding=$false)]
Param(
 [Parameter(mandatory=$true)]
 [String]$OneEsPat,
 [Parameter(mandatory=$true)]
 [String]$CbPat,
 [Parameter(mandatory=$true)]
 [String]$NcPath,
 [Parameter(mandatory=$true)]
 [String]$MsEngGitPat,
 [Parameter(mandatory=$false)]
 [String]$VstsPat,
 [Parameter(mandatory=$false)]
 [String]$VstsCredProviderPath,

 [ValidateSet("LKG", "Dev", "RunCheckinTests", "RunCheckinTestSamples", "ChangeJournalService")]
 [string]$Use = "LKG",
 [ValidateSet("Release", "Debug")]
 [string]$DeployConfig = "Debug",
 [ValidateSet("net472", "net5.0", "net6.0", "win-x64", "osx-x64")]
 [string]$DeployRuntime = "win-x64",
 [Parameter(Mandatory=$false)]
 [ValidateSet("Dev", "RunCheckinTests", "RunCheckinTestSamples", "ChangeJournalService")]
 [string]$Deploy,
 [switch]$Minimal = $false,

 [Parameter(Mandatory=$false)]
 [switch]$EnableProcessRemoting = $false,
 [Parameter(Mandatory=$false)]
 [string]$AnyBuildClientDir,

 [Parameter(Mandatory=$false)]
 [ValidateSet("Disable", "Consume", "ConsumeAndPublish")]
 [string]$SharedCacheMode = "Disable",

 [Parameter(Mandatory=$false)]
 [string]$CacheNamespace,

 [Parameter(mandatory=$false, ValueFromRemainingArguments=$true)]
 [string[]]$BxlArgs
)

[Environment]::SetEnvironmentVariable("1ESSHAREDASSETS_BUILDXL_FEED_PAT", $OneEsPat, "Process")
[Environment]::SetEnvironmentVariable("CLOUDBUILD_BUILDXL_SELFHOST_FEED_PAT", $CbPat, "Process")
[Environment]::SetEnvironmentVariable("MSENG_GIT_PAT", $MsEngGitPat, "Process")
[Environment]::SetEnvironmentVariable("NUGET_CREDENTIALPROVIDERS_PATH", $NcPath, "Process")
[Environment]::SetEnvironmentVariable("VSTSPERSONALACCESSTOKEN", $VstsPat, "Process")
[Environment]::SetEnvironmentVariable("ARTIFACT_CREDENTIALPROVIDERS_PATH", $VstsCredProviderPath, "Process")

[Environment]::SetEnvironmentVariable("VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", "
{
    'endpointCredentials': [
        {'endpoint':'https://pkgs.dev.azure.com/1essharedassets/_packaging/BuildXL/nuget/v3/index.json', 'password':'$OneEsPat'}, 
        {'endpoint':'https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json', 'password':'$CbPat'}
    ]
}", "Process")

$BxlCmdArgs = @(
    "-Use", $Use,
    "-DeployConfig", $DeployConfig,
    "-DeployRuntime", $DeployRuntime
    "-SharedCacheMode", $SharedCacheMode
)

if (-not [string]::IsNullOrEmpty($Deploy))
{
    $BxlCmdArgs += @("-Deploy", $Deploy)
}

if ($Minimal)
{
    $BxlCmdArgs += "-Minimal"
}

if ($EnableProcessRemoting)
{
    $BxlCmdArgs += "-EnableProcessRemoting"
}

if (-not [string]::IsNullOrEmpty($AnyBuildClientDir))
{
    $BxlCmdArgs += @("-AnyBuildClientDir", "$AnyBuildClientDir")
}

if (-not [string]::IsNullOrEmpty($CacheNamespace))
{
    $BxlCmdArgs += @("-CacheNamespace", "$CacheNamespace")
}

$BxlCmdArgs += $BxlArgs

Write-Host "Call bxl.cmd $BxlCmdArgs"

.\bxl.cmd $BxlCmdArgs