$sharedCachePopulationLogDir = "\\fsu\shares\MsEng\Domino\PopulateSharedCacheLogs"
$analyzerExe = "bxlanalyzer.exe"

#################################
# 1. Figure out what point in time to sync to to match the latest rolling build
#################################

# Query the share where shared cache population logs are. The directory names are in the form:
#        Date    Time        CommitId
#      20180416-081955_321aa82d1abedd3c703a61502f83e4e2c8f8e201
# Sort to get the most recent
$sharedCacheLogs = Get-ChildItem $sharedCachePopulationLogDir -Directory | Sort-Object -Property {$_.Name -as [string]} -Descending
$newestRemoteLogDirectory = $sharedCacheLogs[0]

# extract the commit id
$parsed = [regex]::Match($newestRemoteLogDirectory, '(?<Date>[^-]*)-(?<Time>[^_]*)_(?<CommitId>.*)')
$remoteCommitId = $parsed.Groups['CommitId'].Value

Write-Host Latest commit published to shared cache is: $remoteCommitId

# Check to see if the local repo is at the same commit id.
# This doesn't perform any syncing/resetting so as to not potentially lose any local changes.
# it provides the commands to run but trusts the user to know what they're doing
$gitLog = git log -1
# That will be a line like "commit b65d7cdd77927cb0b946fb35a1764d031c83b06d Author:...."
$gitLogparsed = [regex]::Match($gitLog, 'commit (?<CommitId>[^ ]*)')
$localCommitId = $gitLogparsed.Groups['CommitId'].Value

If ($localCommitId -ne $remoteCommitId)
{
    Write-Host Local commit id of $localCommitId does not match latest remote. For best results, sync to remote
    Write-Host
    Write-Host git pull
    Write-Host git reset $remoteCommitId
    return 1
}

$gitStatus = git status

If (!$gitStatus[-1].Contains("working tree clean"))
{
    Write-Host Local repo has uncommitted changes. For best results, sync to remote
    Write-Host
    Write-Host git pull
    Write-Host git reset $remoteCommitId
    return 1
}

#################################
# 2. Perform build
#################################
cmd.exe /c  "$PSScriptRoot\..\..\bxl.cmd" /incrementalscheduling-


#################################
# 3. Run the shared cache analyzer
#################################

# Get the log directory of the latest build
$localLogs = Get-ChildItem "$PSScriptRoot\..\..\out\logs" -Directory | Sort-Object -Property {$_.Name -as [string]} -Descending
$newestLocalLog = $localLogs[0]

$analyzerFullPath = Join-Path $Env:BUILDXL_LKG $analyzerExe
$analyzerCommand = $analyzerFullPath + " /m:cachemiss" + " /xl:$sharedCachePopulationLogDir\$newestRemoteLogDirectory" + " /xl:out\logs\$newestLocalLog" + " /o:out\logs\$newestLocalLog\CacheMiss"

Write-Host Running execution analyzer to diff against shared cache
Write-Host $analyzerCommand 
cmd.exe /c $analyzerCommand
Write-Host
Write-Host Check the cache miss analysis output if the prior build was not a 100% cache hit
Write-Host (Join-Path $PSScriptRoot "..\..\out\logs\$newestLocalLog\CacheMiss" -Resolve)