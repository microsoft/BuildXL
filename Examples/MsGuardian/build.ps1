if (-not (Test-Path env:BUILDXL_BIN))
{
    Write-Output "BUILDXL_BIN environment variable must be set to BuildXL deployment folder."
    exit
}

& $Env:BUILDXL_BIN/bxl /c:config.dsc