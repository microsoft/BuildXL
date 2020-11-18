$inputPath = "D:\checkpoints"
$sstDumpPath = "D:\src\rocksdb-tools\sst_dump.exe"
$blockSize = 8192

$checkpoints = Get-ChildItem -Path $inputPath -Directory

foreach ($checkpointPath in $checkpoints) {
    Write-Host "Processing checkpoint $checkpointPath"
    $sstFiles = Get-ChildItem -Path $checkpointPath -Filter "*.sst" | % { $_.FullName }
    $job = $sstFiles | ForEach-Object -Parallel {
        $sst = $_
        $checkpoint = $using:checkpointPath
        $outputFile = "$sst.compression.bs$using:blockSize.txt"
        # Remove-Item -Path $outputFile
        if (Test-Path $outputFile -PathType leaf) {
            Write-Host "[$checkpoint] Skipping file $sst because $outputFile already exists"
        } else {
            Write-Host "[$checkpoint] Processing file $sst into $outputFile"
            if ($using:blockSize -eq 0) {
                Invoke-Expression "$using:sstDumpPath --file=$sst --command=recompress > $outputFile"
            } else {
                Invoke-Expression "$using:sstDumpPath --file=$sst --command=recompress --set_block_size=$using:blockSize > $outputFile"
            }
        }
    } -ThrottleLimit 8

    $job | Receive-Job -Wait
}
