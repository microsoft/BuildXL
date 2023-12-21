$accounts = az storage account list  --query "[].{name:name}" --output tsv | Select-String "cbcacheprodstorage"

foreach ($account in $accounts) {
    $connString = $(az storage account show-connection-string -n $account --output tsv)
    Write-Host ($account, $connString) -Separator "`t"
}