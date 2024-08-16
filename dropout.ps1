param (
    [string]$FeatureName,
    [string]$AccountName = "mseng",
    [string]$DropContentDir,
    [string]$UseFeatureName
)

if (-not $FeatureName) {
    Write-Output ""
    Write-Output "Must specify feature name as first argument"
    Write-Output ""
    exit 1
}

if (-not $DropContentDir) {
    $DropContentDir = "$PSScriptRoot\out\bin"
}

if (-not [System.IO.Path]::IsPathRooted($DropContentDir)) {
    $DropContentDir = Join-Path $PSScriptRoot $DropContentDir
}

if ($UseFeatureName) {
    $DropName = $FeatureName
} else {
    $DropName = "$env:USERNAME/$FeatureName"
}

Write-Output "Creating drop $DropName"
Write-Output "https://$AccountName.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/$DropName"

Invoke-Expression "& `"$PSScriptRoot\drop.cmd`" create -a -s https://$AccountName.artifacts.visualstudio.com/DefaultCollection -n `"$DropName`""
Invoke-Expression "& `"$PSScriptRoot\drop.cmd`" publish -a -s https://$AccountName.artifacts.visualstudio.com/DefaultCollection -n `"$DropName`" -d `"$DropContentDir`""
Invoke-Expression "& `"$PSScriptRoot\drop.cmd`" finalize -a -s https://$AccountName.artifacts.visualstudio.com/DefaultCollection -n `"$DropName`""

Write-Output "Created drop $DropName"
Write-Output "https://$AccountName.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/$DropName"