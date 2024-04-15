# The source and destination directories for the scan and extraction of packages.
param(
    [String]$sourceDirectory,
    [String]$destinationDirectory,
    [String]$buildNumber
)

# The destinationDirectory is created if it does not exist and cleaned up if it exists.
if (Test-Path -Path $destinationDirectory) {
    # Clear the contents of the destination directory without deleting the directory itself
    Get-ChildItem -Path $destinationDirectory -Recurse | Remove-Item -Force -Recurse
} else {
    New-Item -ItemType Directory -Force -Path $destinationDirectory
}

# Recursively scan for all the .nupkg files in the source directory
$files = Get-ChildItem -Path $sourceDirectory -Filter *.nupkg -Recurse

foreach ($file in $files) {
    # Guardian baselines and suppressions require a consistent path.
    # Remove the version number from the nuget extracted directory name to have the same path across multiple builds.
    $packageName = $file.BaseName

    # Find the starting index of the buildNumber.
    $index = $packageName.IndexOf($buildNumber)

    # If the buildNumber is found, trim everything from its start position including the extension.
    if ($index -ne -1) {
        $packageName = $packageName.Substring(0, $index - 1)
    }

    # Define the unique destination path for the current file
    $uniqueDestinationPath = Join-Path -Path $destinationDirectory -ChildPath $packageName
   
    # Check if the unique destination path already exists
    if (Test-Path -Path $uniqueDestinationPath) {
        # Clear the contents of the unique destination directory
        Get-ChildItem -Path $uniqueDestinationPath -Recurse | Remove-Item -Force -Recurse
    } else {
        # If the directory does not exist, create it
        New-Item -ItemType Directory -Force -Path $uniqueDestinationPath
    }

    # Extract the contents of the .nupkg file directly to the cleaned unique destination directory
    Expand-Archive -Path $file.FullName -DestinationPath $uniqueDestinationPath -Force
}