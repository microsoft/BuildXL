$rgName = "blobl3-juguzman-centralus"
$subscription = "bf933bbb-8131-491c-81d9-26d7b6f327fa"

az account set --subscription $subscription

$storageAccounts = az storage account list --resource-group $rgName | ConvertFrom-Json

$storageAccounts | ForEach-Object {
  $keys = az storage account keys list --account-name $_.name | ConvertFrom-Json
  $key = $keys[0].value
  $connectionString = "DefaultEndpointsProtocol=https;AccountName=$($_.name);AccountKey=$key" 
  Write-Output $connectionString
}