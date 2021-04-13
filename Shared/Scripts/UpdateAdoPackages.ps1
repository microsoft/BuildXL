# Makes sure that when we are updating ADO packages, we don't have to do it manually. This is done because we can't upstream their NuGet source (juguzman)

$destination = 'https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json'
$source = 'https://pkgs.dev.azure.com/mseng/_packaging/VSOnline-Internal/nuget/v3/index.json'

$adoVersion = '16.186.0-internal202104121'
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

$artifactVersion = '18.186.31212-buildid14906928'
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

$tempDirectory = '.\adoUpdateTemp'

Foreach ($package in $adoPackages)
{
    $version = $adoVersion
    nuget.exe install $package -Version $version -DependencyVersion Ignore -DirectDownload -Source $source -OutputDirectory $tempDirectory

    $packageDirectory = "$package.$version"
    $packageFile = "$packageDirectory.nupkg"
    nuget.exe push "$tempDirectory\$packageDirectory\$packageFile" -ApiKey ado -Source $destination
}

Foreach ($package in $artifactPackages)
{
    $version = $artifactVersion
    nuget.exe install $package -Version $version -DependencyVersion Ignore -DirectDownload -Source $source -OutputDirectory $tempDirectory

    $packageDirectory = "$package.$version"
    $packageFile = "$packageDirectory.nupkg"
    nuget.exe push "$tempDirectory\$packageDirectory\$packageFile" -ApiKey ado -Source $destination
}

Remove-Item $tempDirectory -Recurse