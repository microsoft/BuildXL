# The source and destination directories for the scan and extraction of packages.
param(
    [String]$sourceDirectory,
    [String]$destinationDirectory
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
    # Define the unique destination path for the current file
    $uniqueDestinationPath = Join-Path -Path $destinationDirectory -ChildPath ($file.BaseName)
   
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