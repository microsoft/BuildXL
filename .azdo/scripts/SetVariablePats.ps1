<#
.SYNOPSIS
Sets secret variables with PATs for use in subsequent tasks in ADO
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
 [String]$VstsCredProviderPath
)

# Sanity check: we should be running in an Azure Pipeline (let's avoid printing the values if not) 
if (-not (Test-Path env:TF_BUILD)) {
    throw "This script should only be run in an Azure Pipeline"
}

function Set-Variable($variableName, $variableValue) {
    Write-Host "Setting $variableName"
    [Environment]::SetEnvironmentVariable($variableName, $variableValue, "Process")     # This script is still used by RunBuildXLWithPAT so also make this available in the current environment
    Write-Host "##vso[task.setvariable variable=$variableName;issecret=true]$variableValue"
}

Set-Variable "1ESSHAREDASSETS_BUILDXL_FEED_PAT" $OneEsPat
Set-Variable "CLOUDBUILD_BUILDXL_SELFHOST_FEED_PAT" $CbPat
Set-Variable "MSENG_GIT_PAT" $MsEngGitPat
Set-Variable "VSTSPERSONALACCESSTOKEN" $VstsPat
Set-Variable "ARTIFACT_CREDENTIALPROVIDERS_PATH" $VstsCredProviderPath

# NPM authentication requires the PAT to be base64 encoded first
$cbPatBytes = [System.Text.Encoding]::UTF8.GetBytes($CbPat)
$b64CloudbuildPat = [Convert]::ToBase64String($cbPatBytes)

# CODESYNC: Keep this variable name in sync with Public/Src/FrontEnd/UnitTests/Rush/IntegrationTests/RushIntegrationTestBase.cs
Set-Variable "CLOUDBUILD_BUILDXL_SELFHOST_FEED_PAT_B64" $b64CloudbuildPat

if ($NcPath)
{
    Set-Variable "NUGET_CREDENTIALPROVIDERS_PATH" $NcPath
}

$vssEndpoints = "{`"endpointCredentials`": [{`"endpoint`":`"https://pkgs.dev.azure.com/1essharedassets/_packaging/BuildXL/nuget/v3/index.json`", `"password`":`"$OneEsPat`"}, {`"endpoint`":`"https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json`", `"password`":`"$CbPat`"}]}"
Set-Variable "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS" $vssEndpoints
