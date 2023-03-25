$rgName = "blobl3-jubayard-southcentralus"

$storageAccounts = az storage account list --resource-group $rgName | ConvertFrom-Json

$storageAccounts | ForEach-Object -Parallel {
  $keys = az storage account keys list --account-name $_.name | ConvertFrom-Json
  $key = $keys[0].value
  $connectionString = "DefaultEndpointsProtocol=https;AccountName=$($_.name);AccountKey=$key" 
  Write-Output $connectionString
}