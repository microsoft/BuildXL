## Sets the MSVC Version to be used the VisualCpp SDK in ADO

$InstalledMsvcVersions=Get-ChildItem -Path 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\VC\Tools\MSVC' -Directory -Name

$PIPELINE_MSVC_VER=$null

if ( $InstalledMsvcVersions -is [system.array] )
{
    # If multiple versions are found, then set to the last one
    $PIPELINE_MSVC_VER=$InstalledMsvcVersions[$InstalledMsvcVersions.Length-1]
}
else
{
    $PIPELINE_MSVC_VER=$InstalledMsvcVersions
}

Write-Host "##vso[task.setvariable variable=MSVC_VERSION;]$PIPELINE_MSVC_VER"