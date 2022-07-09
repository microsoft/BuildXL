<#
.SYNOPSIS

Buddy build requester in CloudBuild

.DESCRIPTION

This script requests a buddy build in CloudBuild with the untracked files and uncommitted changes. 
The untracked files and uncommitted changes will be committed temporarily and published to the remote branch 
(personal/<username>/bxl_buddy/zzz_buddy-<datetime>). After the remote branch is published, the commit will be reverted and you will 
have unstaged changes and untracked files again.

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
    [parameter(HelpMessage = "Print help message")]
    [switch]$Help,

    [parameter(HelpMessage = "Number of builders to use")]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$NumBuilders,

    [Parameter(HelpMessage = "Cache feature")]
    [ValidateSet("Disable", "Consume", "ConsumeAndPublish")]
    [string]$Cache = "ConsumeAndPublish",

    [Parameter(HelpMessage = "Cache account")]
    [string]$CacheAccount = "mseng",

    [Parameter(HelpMessage = "Cache namespace")]
    [string]$CacheNamespace = "BuildXLSelfhost",

    [Parameter(HelpMessage = "Build queue")]
    [string]$Queue = "BuildXL_Internal_PR",

    [Parameter(HelpMessage = "Build engine drop")]
    [string]$BxlDrop = "",

    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$BuildXLArguments = ""
)

if ($Help) {
    Get-Help -Detailed $PSCommandPath;
    return;
}

Import-Module (Join-Path $PSScriptRoot Invoke-CBWebRequest.psm1) -Force -DisableNameChecking;

$username = $env:USERNAME;
$date = $([DateTime]::UtcNow.ToLocalTIme());
$branchDateFormat = "yyyy_MM_dd_hh_mm_ss";
$branchPrefix = "zzz_buddy";

# ------------------------------------------------------
# 0- Delete old branches.
Write-Host ">>> Deleting old branches if any."
$remoteBranches = git branch -r;
$remoteBranches | 
    %{$_.Trim()} | 
    %{$_.TrimStart("origin/")} | 
    ?{-not($_ -match "master" )} | 
    ?{$_ -match "dev/$username/bxl_buddy/$branchPrefix-"} |
    ?{
        $oldBranchName = $_.Split("/")[3];
        $oldBranchDate = $oldBranchName.Split("-")[1];
        $oldDate =  [DateTime]::ParseExact($oldBranchDate, $branchDateFormat, $null);
        $diff = New-TimeSpan $oldDate $date;
        # Delete the branch if it is more than 1 day old.
        return $diff -gt (New-TimeSpan -Days 1);
    } |
    % {
        Write-Host "Deleting branch origin $_";
        git push origin --delete $_;
    }

# ------------------------------------------------------ 
# 1- Git commit, publish to the remote branch, and git reset

$formattedDate = $date.ToString($branchDateFormat);
$remoteBranch = "dev/$username/bxl_buddy/$branchPrefix-$formattedDate";
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
git push --force origin HEAD:$remotebranch >$null 2>$null

if ($beforeCommit -ne $afterCommit)
{
    # if committed, then revert to the original state.
    git reset HEAD~ >$null 2>$null
}

# -----------------------------------------------------------------
# 2- Generate the cb.exe arguments and send the build to Cloudbuild
Write-Host ">>> Sending the build to CloudBuild"

if (![string]::IsNullOrEmpty($BxlDrop)) {
    $bxlEngine = $BxlDrop + '?root=release/win-x64';
}
else {
    # BuildXL version in your repo needs to be used to get cache hits in your local builds.
    # To this end, we infer BuildXL version from BuildXLLkgVersion.cmd. If we use the version
    # in CloudBuild, we may not get cache hits from the remote cache.
    $bxlVersionLine = Get-Content -Path "$PSScriptRoot\BuildXLLkgVersion.cmd" | Select-String "BUILDXL_LKG_VERSION" | select-object -First 1
    $bxlVersion = $bxlVersionLine.Line.Split("=")[1];
    $bxlEngine = 'https://cloudbuild.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/buildxl.dogfood.' + $bxlVersion + '?root=release/win-x64';
}

$disableCache = ($Cache -eq "Disable");
$consumeCache = ($Cache -eq "Consume");
$publishCache = ($Cache -eq "ConsumeAndPublish");

if ($disableCache) {
    $BuildXLArguments += " /p:BUILDXL_FINGERPRINT_SALT=* /f:~(tag='LongRunningTest')";
    $contentWriteMode = "WriteNever";
}
elseif ($publishCache) {
    $contentWriteMode = "WriteThrough";
}
elseif ($consumeCache) {
    $contentWriteMode = "WriteNever";
}

$minBuilders = 3;
$maxBuilders = 3;
If ($NumBuilders -gt 0) { 
    $minBuilders = $NumBuilders;
    $maxBuilders = $NumBuilders;
}

$requestBody = "
{
    'BuildQueue': '$Queue',
    'Requester': '$username',
    'ChangeId': '$afterCommit',
    'MinBuilders': '$minBuilders',
    'MaxBuilders': '$maxBuilders',
    'Description': 'BuildXL buddy build $([DateTime]::UtcNow.ToLocalTIme().ToString())',
    'ToolPaths': {
        'DominoEngine': '$bxlEngine'
    },
    'GenericRunnerOptions': {
        'CacheVstsAccountName': '$CacheAccount',
        'CacheVstsNamespace': '$CacheNamespace',
        'ContentWriteMode': '$contentWriteMode'
    },
    'BuildEngineOptions': {
        'Additionalcommandlineflags': '$BuildXLArguments'
    },
    'IsBuddyBuild': 'true'
}
";


$response = Invoke-CBWebRequest -Uri 'https://cloudbuild.microsoft.com/ScheduleBuild/submit' -Body $requestBody

If ($response.StatusCode -ne 200) {
    Write-Host $response;
}
else {
    $content = $response.Content | ConvertFrom-Json;
    If ($content.Succeeded -ne $true) {
        $errorMessage = $content.ErrorMessage
        Write-Host "Error: $errorMessage";
    }
    else {
        $batmonHost = $content.BatmonHostInCorp;
        $sessionId = $content.UniqueSessionId;
        Write-Host "Build: https://$batmonHost/build/$sessionId";
    }
}