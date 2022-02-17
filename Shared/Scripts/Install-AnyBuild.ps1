Function Install-AnyBuild
{
    param(
        [string]$AnyBuildSource,
        [string]$Ring,
        [bool]$Clean = $false
    );

    $AbDir = "$env:LOCALAPPDATA\Microsoft\AnyBuild"
    $AbCmd = "$AbDir\AnyBuild.cmd"

    if ($Clean)
    {
        Remove-Item -Force -Recurse "$AbDir" -ea SilentlyContinue
    }

    if (Test-Path $AbCmd -PathType Leaf)
    {
        Write-Host "AnyBuild client is already installed"
        return
    }

    Write-Host "Install AnyBuild from $AnyBuildSource ($Ring)"
    
    # Make lowercase as AzStorage cannot handle uppercase in URL.
    $source = $AnyBuildSource.ToLowerInvariant()
    $bootstrapperArgs = @("$source", "$Ring")

    Write-Host "Bootstrapper args: '$bootstrapperArgs'";

    while ($true)
    {
        $script = ((curl.exe -s -S --retry 10 --retry-connrefused "$source/bootstrapper.ps1") | Out-String)

        if ($LASTEXITCODE -eq 0)
        {
            break;
        }

        # We sometimes get a 404 error when downloading the bootstrapper
        # while the bootstrapper script is updating. Needs an eventual fix instead of this workaround.
        Write-Host "ERROR: Failed downloading the bootstrapper script. Trying again."
    }

    Invoke-Command -ScriptBlock ([scriptblock]::Create($script)) -ArgumentList $bootstrapperArgs
}