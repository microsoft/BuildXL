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

 [Parameter(Mandatory=$false)]
 [switch]$DeployDev = $false,
 [Parameter(Mandatory=$false)]
 [switch]$UseDev = $false,
 [Parameter(Mandatory=$false)]
 [switch]$Minimal = $false,
 [Parameter(Mandatory=$false)]
 [switch]$Release = $false,

 [Parameter(Mandatory=$false)]
 [switch]$EnableProcessRemoting = $false,
 [Parameter(Mandatory=$false)]
 [string]$RemoteServiceUri = "https://westus2.anybuild-test.microsoft.com/clusters/07F427C5-7979-415C-B6D9-01BAD5118191",
 [Parameter(Mandatory=$false)]
 [string]$AnyBuildClientDir,

 [Parameter(mandatory=$false, ValueFromRemainingArguments=$true)]
 [string[]]$BxlArgs
)

[Environment]::SetEnvironmentVariable("1ESSHAREDASSETS_BUILDXL_FEED_PAT", $OneEsPat, "Process")
[Environment]::SetEnvironmentVariable("CLOUDBUILD_BUILDXL_SELFHOST_FEED_PAT", $CbPat, "Process")
[Environment]::SetEnvironmentVariable("MSENG_GIT_PAT", $MsEngGitPat, "Process")
[Environment]::SetEnvironmentVariable("NUGET_CREDENTIALPROVIDERS_PATH", $NcPath, "Process")

[Environment]::SetEnvironmentVariable("VSS_NUGET_EXTERNAL_FEED_ENDPOINTS", "
{
    'endpointCredentials': [
        {'endpoint':'https://pkgs.dev.azure.com/1essharedassets/_packaging/BuildXL/nuget/v3/index.json', 'password':'$OneEsPat'}, 
        {'endpoint':'https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json', 'password':'$CbPat'}
    ]
}", "Process")

$BxlCmdArgs = @()

if ($UseDev)
{
    $BxlCmdArgs += @("-Use", "Dev")
}

if ($DeployDev)
{
    $BxlCmdArgs += "-DeployDev"
}

if ($Release)
{
    $BxlCmdArgs += @("-DeployConfig", "Release")
}

if ($Minimal)
{
    $BxlCmdArgs += "-Minimal"
}

if ($EnableProcessRemoting)
{
    $BxlCmdArgs += "-EnableProcessRemoting"
    $BxlCmdArgs += @("-RemoteServiceUri", $RemoteServiceUri)
}

if (-not [string]::IsNullOrEmpty($AnyBuildClientDir))
{
    $BxlCmdArgs += @("-AnyBuildClientDir", "$AnyBuildClientDir")
}

$BxlCmdArgs += $BxlArgs

Write-Host "Call bxl.cmd $BxlCmdArgs"

.\bxl.cmd $BxlCmdArgs