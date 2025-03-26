# Mostly Copilot generated. Excuse the quirks..
# 
# This script queries the .NET 8.0 and 9.0 download pages to determine the latest runtime version,
# updates the constants in config.nuget.aspNetCore.dsc,
# and updates download URLs in config.dsc by navigating to the thank-you pages and extracting Direct download links.

function Get-LatestDotNetVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )
    Write-Host "Querying $Url ..."
    $response = Invoke-WebRequest $Url -UseBasicParsing
    $content = $response.Content

    # S scrape the version from a marker like ".NET Runtime x.y.z".
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
        [string]$Os  # expected values: "windows-x64", "macos-x64", "linux-x64"
    )
    # Construct the thank-you URL. Example for Windows:
    # https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-8.0.14-windows-x64-binaries
    $thankYouUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-$Version-$Os-binaries"
    Write-Host "Navigating to thank-you URL: $thankYouUrl ..."
    $response = Invoke-WebRequest $thankYouUrl -UseBasicParsing
    $content = $response.Content

    # Adjusted regex: match "Direct link" (case-insensitive) then capture an HTTP(S) URL.
    $regex = [regex]::new('(https:\/\/download\.visualstudio\.microsoft\.com\/[^\s"<>]+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $match = $regex.Match($content)
    if ($match.Success) {
        $directUrl = $match.Groups[1].Value.Trim()
        Write-Host "Found Direct download URL: $directUrl"
        return $directUrl
    }
    else {
        Write-Warning "Could not extract Direct download URL from $thankYouUrl"
        return $null
    }
}

# URLs for .NET 8.0 and 9.0 downloads pages
$dotnet8Url = "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
$dotnet9Url = "https://dotnet.microsoft.com/en-us/download/dotnet/9.0"

$latestDotnet8 = Get-LatestDotNetVersion -Url $dotnet8Url
$latestDotnet9 = Get-LatestDotNetVersion -Url $dotnet9Url

if (-not $latestDotnet8 -or -not $latestDotnet9) {
    Write-Error "Could not determine latest version(s). Exiting."
    exit 1
}

# Files to update.
$ConfigFile = Join-Path $PSScriptRoot "..\..\config.dsc"
$NugetFile = Join-Path $PSScriptRoot "..\..\config.nuget.aspNetCore.dsc"

Write-Host "Updating NuGet config file: $NugetFile"

# (Existing logic to update version constants in the NuGet config remains unchanged)
(Get-Content $NugetFile) |
    ForEach-Object {
        $_ -replace 'const\s+asp8RefVersion\s*=\s*".*?"', "const asp8RefVersion = `"$latestDotnet8`"" `
           -replace 'const\s+asp8RuntimeVersion\s*=\s*".*?"', "const asp8RuntimeVersion = `"$latestDotnet8`"" `
           -replace 'const\s+asp9RefVersion\s*=\s*".*?"', "const asp9RefVersion = `"$latestDotnet9`"" `
           -replace 'const\s+asp9RuntimeVersion\s*=\s*".*?"', "const asp9RuntimeVersion = `"$latestDotnet9`""
    } | Set-Content $NugetFile

Write-Host "Updated NuGet file: $NugetFile"

Write-Host "Updating config file: $NugetFile"

$osList = @("windows-x64", "macos-x64", "linux-x64")

foreach ($latestDotnet in @($latestDotnet9, $latestDotnet8))
{
    foreach ($os in $osList)
    {
        # For the download config file, we now update the entry for the dotnet version and OS
        # Fetch the new direct download URL.
        $newUrl = Get-DirectDownloadUrl -Version $latestDotnet -Os $os
        if (-not $newUrl) {
            Write-Warning "Failed to get new direct download URL for .NET $latesDotnet ($os)."
        }

        # The URLs and package names don't exactly match up. Switch them around
        $packageOs = $os -replace "macos", "osx" -replace "windows", "win"

        # extract the first number before the dot in $latestDotNet
        $majorVersion = $latestDotnet.Split(".")[0]

        # Read the file and use a regex to update the URL for the DotNet-Runtime.win-x64.8.0 entry.
        # This regex looks for the moduleName "DotNet-Runtime.win-x64.8.0", then the url field.
        $pattern = '(?ms)(moduleName:\s*"DotNet-Runtime\.' + $packageOs + '\.' + $majorVersion + '\.0".*?url:\s*")[^"]+(")'

        $updatedContent = (Get-Content $ConfigFile) -join "`n" -replace $pattern, "`$1$newUrl`$2"

        Set-Content $ConfigFile $updatedContent
    }
}

Write-Host "Updated config file: $ConfigFile"

Write-Host "Update Step 1 complete."

Write-Warning "You now must do a bxl build and update $ConfigFile with the expected content hashes. Run: bxl.cmd ""/f:tag='extract'"""
$runBxl = Read-Host "Do you want to do it now? (y/yes to proceed)"
if ($runBxl -match '^(?i:y(?:es)?)$') {
    $repoRoot = Join-Path $PSScriptRoot "..\.."
    Push-Location $repoRoot
    Write-Host "Executing bxl.cmd /f:tag='extract'..."
    & ".\bxl.cmd" "/f:tag='extract'"
    Pop-Location
}