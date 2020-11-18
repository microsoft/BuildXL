
$outputBasePath = "D:\checkpoints"

$secrets = (Get-Content "D:\secrets.txt")
$multitool = "D:\BXL2\Out\Bin\debug\cache\netcoreapp3.1\win-x64\MultiTool\App\multitool.exe"

foreach ($account in $secrets) {
    $components = $account -Split ";"
    $name = ($components[2] -Split "=")[1]
    Invoke-Expression "$multitool DownloadCheckpoint /outputPath:$outputBasePath\$name /storageConnectionString:`"$account`""
}
