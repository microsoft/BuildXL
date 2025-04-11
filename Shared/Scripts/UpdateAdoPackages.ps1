# This script is used to update Artifacts/ADO packages.
# Currently, we have to run this script manually every time we update those packages because we can't upstream their NuGet source (juguzman)

$destination = 'https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json'
$source = 'https://pkgs.dev.azure.com/mseng/_packaging/VSOnline-Internal/nuget/v3/index.json'

$tempDirectory = Join-Path $env:TEMP "adoUpdateTemp"
New-Item -Path $tempDirectory -ItemType Directory -Force

$artifactVersion = '19.254.35907-buildid29691232'
$adoVersion = '19.254.0-internal202503071'

$adoPackages = @(
    'Microsoft.VisualStudio.Services.Client'
    'Microsoft.VisualStudio.Services.InteractiveClient'

    # The following packages are needed by the CB repo. Let's include them.
    'Microsoft.TeamFoundation.DistributedTask.Common.Contracts'
    'Microsoft.TeamFoundation.DistributedTask.WebApi'
    'Microsoft.TeamFoundationServer.Client'
    'Microsoft.TeamFoundation.PublishTestResults'
    'Microsoft.VisualStudio.Services.Feed.WebApi'
    'Microsoft.VisualStudio.Services.Packaging.Client'
    'Microsoft.VisualStudio.Services.ServiceEndpoints.WebApi'
)

$artifactPackages = @(
    'Microsoft.VisualStudio.Services.ArtifactServices.Shared'
    'Microsoft.VisualStudio.Services.BlobStore.Client'
    'ArtifactServices.App.Shared'
    'ArtifactServices.App.Shared.Cache'
    'Drop.App.Core'
    'Drop.Client'
    'ItemStore.Shared'
    'Microsoft.VisualStudio.Services.BlobStore.Client.Cache'
    'Symbol.App.Core'
    'Symbol.Client'
    
    # The following packages are needed by the CB repo. Let's include them.
    'Drop.App'
)

Foreach ($package in $adoPackages) {
    $version = $adoVersion
    Write-Host ""
    Write-Host "-- Installing $package version $version"
    Invoke-Expression "$nugetPath install $package -Version $version -DependencyVersion Ignore -DirectDownload -Source $source -OutputDirectory $tempDirectory"

    $packageDirectory = "$package.$version"
    $packageFile = "$packageDirectory.nupkg"
    Write-Host ""
    Write-Host "-- Pushing $packageFile to $destination"
    Invoke-Expression "$nugetPath push '$tempDirectory\$packageDirectory\$packageFile' -ApiKey ado -Source $destination -Timeout 3600 -SkipDuplicate"
}

Foreach ($package in $artifactPackages) {
    $version = $artifactVersion
    Write-Host ""
    Write-Host "-- Installing $package version $version"
    Invoke-Expression "$nugetPath install $package -Version $version -DependencyVersion Ignore -DirectDownload -Source $source -OutputDirectory $tempDirectory"

    $packageDirectory = "$package.$version"
    $packageFile = "$packageDirectory.nupkg"
    Write-Host ""
    Write-Host "-- Pushing $packageFile to $destination"
    Invoke-Expression "$nugetPath push '$tempDirectory\$packageDirectory\$packageFile' -ApiKey ado -Source $destination -Timeout 3600 -SkipDuplicate"
}

Remove-Item $tempDirectory -Recurse