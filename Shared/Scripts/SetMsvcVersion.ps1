## Sets the MSVC Version to be used the VisualCpp SDK in ADO

# Define possible paths for Visual Studio installations.
$vsPaths = @(
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Tools\MSVC',
    'C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\VC\Tools\MSVC'
)

# We always prefer to use the 2022 version of Visual Studio since we need it for resolving few violations raised by the BinSkim tool.
$selectedPath = $null

foreach ($path in $vsPaths) {
    if (Test-Path $path) {
        $selectedPath = $path
        break
    }
}

$PIPELINE_MSVC_VER = $null

if ($selectedPath -ne $null) {
    $InstalledMsvcVersions = Get-ChildItem -Path $selectedPath -Directory -Name

    if ($InstalledMsvcVersions -is [system.array]) {
        # If multiple versions are found, then set to the last one
        $PIPELINE_MSVC_VER = $InstalledMsvcVersions[$InstalledMsvcVersions.Length - 1]
    } 
    else {
        $PIPELINE_MSVC_VER = $InstalledMsvcVersions
    }
    # Set the pipeline variable to the found MSVC version
    Write-Host "##vso[task.setvariable variable=MSVC_VERSION;]$PIPELINE_MSVC_VER"
} 
else {
    Write-Error "No valid Visual Studio installations found in the specified paths."
    exit -1
}

Write-Host "Setting MSVC Version to: $PIPELINE_MSVC_VER"