param ($tempPackageRoot, $validatedPackageRoot)

# Enumerate all nuget package files in the input location.
Get-ChildItem $tempPackageRoot -Filter *.nupkg | Foreach-Object {
    $nupkgPath = $_.FullName
    $basename = $_.BaseName
    $directory = $_.Directory
    $packageName = ($basename -split '\.',2)[0]
    $packageVersion = ($basename -split '\.',2)[1]

    # nuget package is just a zip file. Extract files.
    $installDirectory = Join-Path $directory "install"
    $zipPath = Join-Path $directory $basename".zip"
    Copy-Item $nupkgPath $zipPath
    Expand-Archive $zipPath -dest $installDirectory

    # Enumerate all extracted assemblies and make sure their version matches the package version.
    Get-ChildItem $installDirectory\* -Recurse -Include *.dll, *.exe | Foreach-Object {
        # Load some information about the assembly.
        $dllPath = $_.FullName
        $assembly = [System.Reflection.Assembly]::LoadFrom($dllPath)
        $fileVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath)
        $assemblyName = $assembly.FullName
        if ($assemblyName -match "Version=(\d\.\d.\d)") {
            $assemblyVersion = $matches[1]
        }
        $fileVersion = $fileVersionInfo.FileVersion
        $productVersion = $fileVersionInfo.ProductVersion
        $hasMatchingVersion = $True

        Write-Host "Checking assembly $dllPath"
        Write-Host "    Assembly name  : $assemblyName"
        Write-Host "    FileVersion    : $fileVersion"
        Write-Host "    ProductVersion : $productVersion"

        # Verify embedded version number in full assembly name.
        if (-Not $assemblyVersion.startswith($packageVersion)) {
            Write-Host "    Assembly version $assemblyVersion does not match package version $packageVersion" -foregroundcolor red
            $hasMatchingVersion = $False
        }

        # Verify file version.
        if (-Not $fileVersion.startswith($packageVersion)) {
            Write-Host "    FileVersion $fileVersion does not match package version $packageVersion" -foregroundcolor red
            $hasMatchingVersion = $False
        }

        # Verify product version.
        if (-Not $productVersion.startswith($packageVersion)) {
            Write-Host "    ProductVersion $fileVersion does not match package version $packageVersion" -foregroundcolor red
            $hasMatchingVersion = $False
        }

        # Something not matching, so immediately give caller error code.
        if (-Not $hasMatchingVersion) {
            exit 1
        }

        # Assembly version matches, copy it to validated location
        Copy-Item $nupkgPath $validatedPackageRoot
        Write-Host "    Copied to $validatedPackageRoot"
    }
 }
exit 0
