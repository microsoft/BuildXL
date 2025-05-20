<#
.SYNOPSIS

Script for BuildXL self-hosting with specified PATs. This script is used to perform BuildXL self-hosting in Azure pipeline.

NOTE: This script is deprecated. 
      The way to run BuildXL on ADO is just including .azdo/common/set-variable-pats.yml to set up the environment
      as a pre-build step of the 1ESPT BuildXL workflow (or before running bxl.cmd directly).
#>

[CmdletBinding(PositionalBinding=$false)]
Param(
 [Parameter(mandatory=$true)]
 [String]$OneEsPat,
 [Parameter(mandatory=$true)]
 [String]$CbPat,
 [Parameter(mandatory=$false)]
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
 [ValidateSet("net472", "net8.0", "net9.0", "win-x64", "osx-x64")]
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

Write-Warning "This script is deprecated."
Write-Warning "The way to run BuildXL on ADO is just including .azdo/common/set-variable-pats.yml to set up the environment as a pre-build step of the 1ESPT BuildXL workflow (or before running bxl.cmd directly)."

# 1. Set PATs
$PatArgs = @(
    "-OneEsPat", $OneEsPat,
    "-CbPat", $CbPat,
    "-MsEngGitPat", $MsEngGitPat
)

if (-not [string]::IsNullOrEmpty($NcPath))
{
    $PatArgs += @("-NcPath", $NcPath)
}

if (-not [string]::IsNullOrEmpty($VstsPat))
{
    $PatArgs += @("-VstsPat", $VstsPat)
}

if (-not [string]::IsNullOrEmpty($VstsCredProviderPath))
{
    $PatArgs += @("-VstsCredProviderPath", $VstsCredProviderPath)
}

$PatArgsStr = $PatArgs -Join " "
Invoke-Expression ".azdo/scripts/SetVariablePats.ps1 $PatArgsStr"

Write-Host "Call bxl.cmd $BxlArgs"
.\bxl.cmd $BxlArgs
