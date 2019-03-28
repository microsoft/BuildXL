<#
.SYNOPSIS
    Emits the DropLocation of the latest good BuildXL official build to stdout.
#>

$ba = [System.Text.Encoding]::UTF8.GetBytes((":{0}" -f $env:SYSTEM_ACCESSTOKEN))

$h = @{Authorization=("Basic {0}" -f [System.Convert]::ToBase64String($ba));ContentType="application/json-patch+json"} 

$lastSuccessfulBuild = Invoke-RestMethod https://mseng.VisualStudio.com/DefaultCollection/Domino/_apis/build/builds?definitions=2575`&resultFilter=succeeded`&`$top=1`&api-version=2.0 -Method Get -Headers $h

$lastSuccessfulBuildId = $lastSuccessfulBuild.value.id

$artifacts = Invoke-RestMethod https://mseng.VisualStudio.com/DefaultCollection/Domino/_apis/build/builds/$lastSuccessfulBuildId/artifacts?api-version=2.0 -Method Get -Headers $h 

$artifacts.value[0].resource.data