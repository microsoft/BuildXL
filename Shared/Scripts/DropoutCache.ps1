<#

.SYNOPSIS
The script builds and deploys a drop for updating the cache service bits via new deployment uploader

.DESCRIPTION

This script builds the BuildXL codebase and produces the drop with the following name: 'aliasDate.Time' like '0.1.0-20201029.1547.seteplia'.

The newly built bits will also have a Bxl version in the same  format: 0.1.0-20201029.1547.seteplia

In order to use the new bits, DeploymentConfiguration.json file (https://cloudbuild.visualstudio.com/CloudBuild/_git/CacheConfig?path=%2FDeploymentConfiguration.json&version=GBmaster&_a=contents) must be updated to look like this:

"Url [Machine:MachineToTest]": "https://cloudbuild.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/0.1.0-20201029.1547.seteplia?root=release/cache/netcoreapp3.1/win-x64/DeployServer",

.EXAMPLE

.\shared\scripts\DropoutCache.ps1

#>

$userName = $env:USERNAME;
$dateTime = (get-date -Format "yyyyMMdd.HHmm");
$version = "0.1.0-$dateTime.$userName"

.\bxl -Minimal -DeployConfig Release -SharedCacheMode Disable /q:Release /q:ReleaseLinux /q:ReleaseDotNet5 /q:ReleaseDotNetCoreMac out\bin\release\cache\* /p:[BuildXL.Branding]SemanticVersion=$version /p:[BuildXL.Branding]SourceIdentification='1'
.\dropout $version cloudbuild true


