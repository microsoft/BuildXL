$downloadToDirPath = $pwd
$tempExtractedDir = [System.IO.Path]::Combine($env:TEMP, [System.IO.Path]::GetRandomFileName());
$tempZipFilePath = "$env:TEMP\CredentialProviderBundle.zip"

$url = "https://dev.azure.com/mseng/_apis/public/nuget/client/CredentialProviderBundle.zip"

$webClient = New-Object System.Net.WebClient;
$webClient.DownloadFile($url, $tempZipFilePath);

[Reflection.Assembly]::LoadWithPartialName("System.IO.Compression.FileSystem");
[System.IO.Compression.ZipFile]::ExtractToDirectory($tempZipFilePath, $tempExtractedDir);

$copyItemsPattern = [System.IO.Path]::Combine($tempExtractedDir, "*.exe");
Copy-Item $copyItemsPattern $downloadToDirPath

# Nuget 3.5 has a 'feature' that causes project.lock.json to have specific paths in it.
# Pin CloudStore to nuget 3.4.4 until we know we have a fix.
$nugetUrl = "https://dist.nuget.org/win-x86-commandline/v3.4.4/NuGet.exe"
$nugetLocalPath = "$downloadToDirPath\\nuget.exe"

if (Test-Path $nugetLocalPath)
{
    Remove-Item -Force $nugetLocalPath 
}

$webClient.DownloadFile($nugetUrl, $nugetLocalPath)