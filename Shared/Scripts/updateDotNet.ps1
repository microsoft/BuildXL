# Mostly Copilot generated. Excuse the quirks..
#
# This script queries the .NET 8.0, 9.0, and 10.0 download pages to determine the latest runtime version,
# updates the constants in config.nuget.aspNetCore.dsc and config.nuget.dotnetcore.dsc,
# and updates download URLs in config.dsc using the official release metadata.

function Get-LatestDotNetVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )
    Write-Host "Querying $Url ..."
    $response = Invoke-WebRequest $Url -UseBasicParsing -ErrorAction Stop
    $content = $response.Content

    # Scrape the version from a marker like ".NET Runtime x.y.z".
    $regex = [regex] "\.NET\s+Runtime\s+(\d+\.\d+\.\d+)"
    $match = $regex.Match($content)
    if ($match.Success) {
        $version = $match.Groups[1].Value
        Write-Host "Scraped version from .NET Runtime string: $version"
        return $version
    }
}

function Get-DirectDownloadUrl {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Version,
        [Parameter(Mandatory=$true)]
        [string]$Os  # expected values: "windows-x64", "macos-x64", "macos-arm64", "linux-x64"
    )
    # Read the published archive URL instead of constructing a thank-you URL.
    $majorVersion = $Version.Split(".")[0]
    $metadataUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$majorVersion.0/releases.json"
    Write-Host "Querying $metadataUrl for $Version ($Os) ..."
    $metadata = Invoke-RestMethod $metadataUrl -ErrorAction Stop
    $release = $metadata.releases | Where-Object { $_.runtime.version -eq $Version }
    $rid = $Os -replace "macos", "osx" -replace "windows", "win"
    $extension = if ($rid -eq "win-x64") { "zip" } else { "tar.gz" }
    $archive = @($release.runtime.files | Where-Object { $_.rid -eq $rid -and $_.name -eq "dotnet-runtime-$rid.$extension" })
    if ($archive.Count -ne 1 -or -not $archive[0].url) {
        throw "Could not find a direct download URL for .NET $Version ($Os)."
    }
    $directUrl = $archive[0].url
    Write-Host "Found Direct download URL: $directUrl"
    return $directUrl
}

# URLs for .NET 8.0, 9.0, and 10.0 download pages
$dotnet8Url = "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
$dotnet9Url = "https://dotnet.microsoft.com/en-us/download/dotnet/9.0"
$dotnet10Url = "https://dotnet.microsoft.com/en-us/download/dotnet/10.0"

$latestDotnet8 = Get-LatestDotNetVersion -Url $dotnet8Url
$latestDotnet9 = Get-LatestDotNetVersion -Url $dotnet9Url
$latestDotnet10 = Get-LatestDotNetVersion -Url $dotnet10Url

if (-not $latestDotnet8 -or -not $latestDotnet9 -or -not $latestDotnet10) {
    Write-Error "Could not determine latest version(s). Exiting."
    exit 1
}

# Files to update.
$ConfigFile = Join-Path $PSScriptRoot "..\..\config.dsc"
$NugetFile = Join-Path $PSScriptRoot "..\..\config.nuget.aspNetCore.dsc"
$DotNetCoreNugetFile = Join-Path $PSScriptRoot "..\..\config.nuget.dotnetcore.dsc"

Write-Host "Updating NuGet config file: $NugetFile"

$updatedContent = (Get-Content $NugetFile -Raw) |
    ForEach-Object {
        $_ -replace 'const\s+aspVersion\s*=\s*".*?"', "const aspVersion = `"$latestDotnet10`"" `
           -replace 'const\s+asp8RefVersion\s*=\s*".*?"', "const asp8RefVersion = `"$latestDotnet8`"" `
           -replace 'const\s+asp8RuntimeVersion\s*=\s*".*?"', "const asp8RuntimeVersion = `"$latestDotnet8`"" `
           -replace 'const\s+asp9RefVersion\s*=\s*".*?"', "const asp9RefVersion = `"$latestDotnet9`"" `
           -replace 'const\s+asp9RuntimeVersion\s*=\s*".*?"', "const asp9RuntimeVersion = `"$latestDotnet9`"" `
           -replace 'const\s+asp10RefVersion\s*=\s*".*?"', "const asp10RefVersion = `"$latestDotnet10`"" `
           -replace 'const\s+asp10RuntimeVersion\s*=\s*".*?"', "const asp10RuntimeVersion = `"$latestDotnet10`""
    }
[IO.File]::WriteAllText($NugetFile, $updatedContent)

Write-Host "Updated NuGet file: $NugetFile"

Write-Host "Updating NuGet dotnet core config file: $DotNetCoreNugetFile"

$updatedContent = (Get-Content $DotNetCoreNugetFile -Raw) |
    ForEach-Object {
        $_ -replace 'const\s+core80Version\s*=\s*".*?"', "const core80Version = `"$latestDotnet8`"" `
           -replace 'const\s+core90Version\s*=\s*".*?"', "const core90Version = `"$latestDotnet9`"" `
           -replace 'const\s+core100Version\s*=\s*".*?"', "const core100Version = `"$latestDotnet10`""
    }
[IO.File]::WriteAllText($DotNetCoreNugetFile, $updatedContent)

Write-Host "Updated NuGet dotnet core config file: $DotNetCoreNugetFile"

Write-Host "Updating config file: $ConfigFile"

$osList = @("windows-x64", "macos-x64", "macos-arm64", "linux-x64")

foreach ($latestDotnet in @($latestDotnet10, $latestDotnet9, $latestDotnet8))
{
    foreach ($os in $osList)
    {
        # The URLs and package names don't exactly match up. Switch them around
        $packageOs = $os -replace "macos", "osx" -replace "windows", "win"
        $majorVersion = $latestDotnet.Split(".")[0]
        $newUrl = Get-DirectDownloadUrl -Version $latestDotnet -Os $os

        # Match the runtime entry's URL, leaving its hash for the manual update.
        $pattern = '(?ms)(moduleName:\s*"DotNet-Runtime\.' + $packageOs + '\.' + $majorVersion + '\.0".*?url:\s*")[^"]+(")'
        $updatedContent = (Get-Content $ConfigFile -Raw) -replace $pattern, "`$1$newUrl`$2"
        [IO.File]::WriteAllText($ConfigFile, $updatedContent)
    }
}

Write-Host "Updated config file: $ConfigFile"

Write-Host "Update Step 1 complete."
Write-Host "Complete the supporting-package, framework-config, and binding-redirect edits in .github\instructions\update-dotnet-runtime.instructions.md."

Write-Warning "You now must do a bxl build and update $ConfigFile with the expected content hashes. Run: bxl.cmd ""/f:tag='extract'"" /server-"
$runBxl = Read-Host "Do you want to do it now? (y/yes to proceed)"
if ($runBxl -match '^(?i:y(?:es)?)$') {
    $repoRoot = Join-Path $PSScriptRoot "..\.."
    Push-Location $repoRoot
    Write-Host "Executing bxl.cmd /f:tag='extract' /server-..."
    try {
        & $env:ComSpec /c "call .\bxl.cmd /f:tag='extract' /server-"
        $buildExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    exit $buildExitCode
}
