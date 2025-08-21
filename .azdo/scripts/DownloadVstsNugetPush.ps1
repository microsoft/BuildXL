param(
    [Parameter(Mandatory = $true)]
    [string]$tempDirectory,
    
    [Parameter(Mandatory = $true)]
    [string]$toolsDirectory
)

function Get-FileWithRetries {
    param (
        [string]$url,
        [string]$outputPath,
        [string]$bearerToken = $null
    )

    $maxAttempts = 3
    $delaySeconds = 5
    $attempt = 0
    $success = $false

    while (-not $success -and $attempt -lt $maxAttempts) {
        try {
            $attempt++
            Write-Host "Attempt $attempt of $maxAttempts. Downloading $url..."

            # Set headers if bearer token is provided
            $headers = @{}
            if ($bearerToken) {
                $headers["Authorization"] = "Bearer $bearerToken"
            }

            # Download file
            Invoke-WebRequest -Uri $url -OutFile $outputPath -Headers $headers -ErrorAction Stop
            Write-Host "Download successful."
            $success = $true
        }
        catch {
            Write-Warning "Exception occurred: $($_.Exception.Message)"

            Write-Host "Download failed. Attempt $attempt of $maxAttempts."
            if ($attempt -lt $maxAttempts) {
                Write-Host "Retrying in $delaySeconds seconds..."
                Start-Sleep -Seconds $delaySeconds
            }
            else {
                Write-Error "Max retries reached. Download failed."
            }
        }
    }

    return $success
}

function Install-FromNuget {
    param (
        [string]$version,
        [string]$outputPath
    )

    $FeedUrl = "https://pkgs.dev.azure.com/1essharedassets/_packaging/Packaging/nuget/v3/index.json"
    $servicesListJsonPath = Join-Path $tempDirectory "servicesList.json"

    if (-Not (Test-Path $tempDirectory)) {
        New-Item -Path $tempDirectory -ItemType Directory -Force | Out-Null
    }
    
    $accessToken = $env:ACCESS_TOKEN
    $downloaded = Get-FileWithRetries -url $FeedUrl -outputPath $servicesListJsonPath -bearerToken $accessToken

    if (!$downloaded) {
        Write-Host "Failed to download JSON after retries."
        exit 1
    }

    try {
        $serviceList = Get-Content -Path $servicesListJsonPath -Raw | ConvertFrom-Json

        # Find the resource with type "PackageBaseAddress/3.0.0"
        # That service's id will be the base URL for downloading the nuget packages from that feed
        $baseUrl = $serviceList.resources | Where-Object { $_.'@type' -eq 'PackageBaseAddress/3.0.0' } | Select-Object -ExpandProperty '@id'

        if (-not $baseUrl) {
            Write-Host "Failed to find base URL for nuget packages."
            exit 1
        }

        Write-Debug "Found base URL: $baseUrl"

        $versionLower = $version.ToLower()
        $packageName = "Microsoft.VisualStudio.Services.Packaging.NuGet.PushTool"

        $packageUrl = "$($baseUrl)$($packageName)/$($versionLower)/$($packageName).$($versionLower).nupkg"
        $tempNugetPath = Join-Path $tempDirectory "installer.zip"

        Write-Debug "Downloading package from '$packageUrl' to '$tempNugetPath'"

        $downloaded = Get-FileWithRetries -url $packageUrl -outputPath $tempNugetPath -bearerToken $accessToken
        if (!$downloaded) {
            Write-Host "Failed to download NuGet package after retries."
            exit 1
        }

        Write-Debug "Extracting package to $outputPath"

        # Extract only the 'tools' folder from the archive
        $tempExtractPath = Join-Path $tempDirectory "tempInstallerOutput"
        Expand-Archive -Path $tempNugetPath -DestinationPath $tempExtractPath -Force

        $toolsFolderPath = Join-Path $tempExtractPath "tools"
        if (Test-Path $toolsFolderPath) {
            Get-ChildItem -Path $toolsFolderPath -Recurse | Move-Item -Destination $outputPath -Force
        } else {
            Write-Host "The 'tools' folder was not found in the archive."
            exit 1
        }

        # Clean up everything from the temp folder
        try {
            Get-ChildItem -Path $tempDirectory -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue | Out-Null
        }
        catch { }
    }
    catch {
        Write-Warning "Exception occurred: $($_.Exception.Message)"
        exit 1
    }
}

$destinationFolder = Join-Path $toolsDirectory "VstsNugetPush"

# Create output folders if they don't exist
if (-Not (Test-Path $destinationFolder)) {
    New-Item -Path $destinationFolder -ItemType Directory -Force | Out-Null
}

Write-Host "Downloading VstsNugetPush from NuGet"
Install-FromNuget -version "0.21.0" -outputPath $destinationFolder
Write-Host "Extracted VstsNugetPush to $destinationFolder"
exit 0
