<#
.SYNOPSIS

Buddy build requester in CloudBuild

.DESCRIPTION

This script requests a buddy build in CloudBuild with the untracked files and uncommitted changes. 
The untracked files and uncommitted changes will be committed temporarily and published to the remote branch 
(personal/<username>/buddy). After the remote branch is published, the commit will be reverted and you will 
have unstaged changes and untracked files again. 
This script will force the remote branch to update and reflect the snapshot of your codebase. 

By default, the buddy build populates the shared cache in VSTS. When you build locally after the buddy build
completes, your local build is supposed to get 100% cache hits. 

.EXAMPLE

buddy -Help

Prints this help text

.EXAMPLE

buddy

Requests a single-machine build with default qualifier (Debug) and no filtering

.EXAMPLE

buddy -NumBuilders 3 BuildXL.Engine.dll

Requests a distributed build (1 master + 2 workers) with the given filter (BuildXL.Engine.dll)

.EXAMPLE

buddy -Cache Disable 

Requests a clean build which does not populate the VSTS shared cache

#>

[CmdletBinding(PositionalBinding=$false)]
param(
    [switch]$Help,

    [parameter(Mandatory=$false)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$NumBuilders,

    [Parameter(Mandatory=$false)]
    [ValidateSet("Disable", "Consume", "ConsumeAndPublish")]
    [string]$Cache = "ConsumeAndPublish",

    [string]$CacheAccount = "mseng",

    [string]$CacheNamespace = "DominoSelfhost",

    [string]$Queue = "Domino_buddy",

    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$DominoArguments = ""
)

if ($Help) {
    Get-Help -Detailed $PSCommandPath;
    return;
}

 
# ------------------------------------------------------ 
# 1- Git commit, publish to the remote branch, and git reset

$username = $env:USERNAME;
$date = Get-Date -Format g
$remoteBranch = "personal/$username/buddy";
Write-Host ">>> Publishing untracked files and uncommitted changes to $remoteBranch."

$anyStagedChanges = git diff --cached;
if (![string]::IsNullOrEmpty($anyStagedChanges))
{
    $answer = Read-Host -Prompt ">>> WARNING: You have staged changes. Those changes will be unstaged after the script is done. If you'd like to continue, please enter 'Y'"

    if ($answer -ne "Y")
    {
        Write-Host ">>> Exited"
        return;
    }
}

$beforeCommit = git rev-parse HEAD;
git add -A
git commit -m "$username's buddy build on $date" >$null 2>$null
$afterCommit = git rev-parse HEAD;
git push --force origin HEAD:personal/$username/buddy >$null 2>$null

if ($beforeCommit -ne $afterCommit)
{
    # if committed, then revert to the original state.
    git reset HEAD~ >$null 2>$null
}

# -----------------------------------------------------------------
# 2- Generate the cb.exe arguments and send the build to Cloudbuild
Write-Host ">>> Sending the build to CloudBuild"

$cbExe = "$env:PkgCloudBuild_BuildRequester\tools\CB.exe"

# BuildXL version in your repo needs to be used to get cache hits in your local builds.
# That's why, we infer BuildXL version from the branding spec file. If we use the version
# in CloudBuild, you would not be able to get cache hits from the remote cache and it might be
# even incompatible with your sources.
$dominoVersionLine = Get-Content -Path "$PSScriptRoot\..\..\Public\Src\Branding\branding.dsc" | Select-String "explicitVersion" | select-object -First 1
$dominoVersion = $dominoVersionLine.Line.Split("`"")[1];
$dominoDrop = 'https://cloudbuild.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/domino.dogfood.' + $dominoVersion + '?root=release';

$disableCache = ($Cache -eq "Disable");
$consumeCache = ($Cache -eq "Consume");
$publishCache = ($Cache -eq "ConsumeAndPublish");

if ($disableCache) {
    $DominoArguments += " /p:BUILDXL_FINGERPRINT_SALT=* /f:~(tag='LongRunningTest')";
    $contentWriteMode = "WriteNever";
}
elseif ($publishCache) {
    $contentWriteMode = "WriteThrough";
}
elseif ($consumeCache) {
    $contentWriteMode = "WriteNever";
}

$genericRunnerOptions = "CacheVstsAccountName=$CacheAccount;CacheVstsNamespace=$CacheNamespace;DisableStampIsolation=true;ContentWriteMode=$ContentWriteMode"

$buildEngineOptions = "Additionalcommandlineflags=`"$DominoArguments`"";

$cbArgs = "ondemand -batmon batmon.trafficmanager.net -bq $Queue -c $afterCommit -de $dominoDrop -gr $genericRunnerOptions";

if (![string]::IsNullOrEmpty($DominoArguments))
{
    $cbArgs += " -be Additionalcommandlineflags=`"$DominoArguments`"";
}

if ($NumBuilders -gt 0)
{
    $cbArgs += " -MinBuilders $NumBuilders -MaxBuilders $NumBuilders";
}

$cbOutput = Start-Process -FilePath $cbExe -ArgumentList $cbArgs -WorkingDirectory (pwd).Path -NoNewWindow -Wait -PassThru;
if ($cbOutput.ExitCode -ne 0) {
    throw "Cb.exe failed to send the build with exit code $LastExitCode : $cbOutput";
}