Import-Module (Join-Path $PSScriptRoot Invoke-CBWebRequest.psm1) -Force -DisableNameChecking;
$requestBody = "
{
    'BuildQueue': 'myBuildQueue',
    'Requester' : 'myalias',
    'Description' : 'Testing low priv build',
    'ToolPaths' : {
      'DominoEngine' : 'https://cloudbuild.artifacts.visualstudio.com/DefaultCollection/_apis/drop/drops/buildxl.dogfood.0.1.0-20200131.5.1?root=release/win-x64'
    }
}
";
$response = Invoke-CBWebRequest -Uri 'https://cloudbuild.microsoft.com/ScheduleBuild/submit' -Body $requestBody
echo $response