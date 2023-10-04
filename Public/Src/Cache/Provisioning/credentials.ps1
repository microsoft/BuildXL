$subscription = "2ab82c67-48f1-42ca-a817-67f4013eca86"

az account set --subscription $subscription

$groups = az group list --subscription $subscription --query "[?starts_with(name, 'blobl3')].name" --output json | ConvertFrom-Json

Write-Host "Found $($groups.Count) resource groups: $($groups -join ', ')"

$regionStampMapping = @{
  "northcentralus" = "CH";
  "southcentralus" = "SN";
  "centralus"      = "DM";
  "westus"         = "CO";
  "westus2"        = "MW";
  "eastus2"        = "BN";
};

$environmentVaultMapping = @{
  "test" = @{
    "name"         = "CBTVault";
    "subscription" = "30c83465-21e5-4a97-9df2-d8dd19881d24"
  };
  "prod" = @{
    "name"         = "CBProdVault";
    "subscription" = "41cf5fb3-558b-467d-b6cd-dd7e6c18945d"
  };
};

foreach ($group in $groups) {
  Write-Host "Processing resource group $group"
  $parts = $group -split '-'
  $region = $parts[-1]

  # Obtain secrets for each storage account in the resource group
  $storageAccounts = az storage account list --subscription $subscription --resource-group $group | ConvertFrom-Json
  $connectionStrings = [System.Collections.Generic.List[string]]::new()
  $storageAccounts | ForEach-Object {
    if ($_.name.StartsWith("mgmt") -and $_.name.Contains("99999")) { return }
  
    $response = az storage account show-connection-string --name $_.name | ConvertFrom-Json
    $connectionString = $response.connectionString
    $connectionStrings.Add($connectionString)
    Write-Host "[$group] Added storage account $($_.name)"
  }

  # Process the secrets into what we want to store in Key Vault
  $pattern = "DefaultEndpointsProtocol=(?<DefaultEndpointsProtocol>[^;]+);EndpointSuffix=(?<EndpointSuffix>[^;]+);AccountName=(?<AccountName>[^;]+);AccountKey=(?<AccountKey>[^;]+);BlobEndpoint=(?<BlobEndpoint>.+)";

  $secrets = [System.Collections.Generic.List[string]]::new();
  foreach ($connectionString in $connectionStrings) {
    if ($connectionString -match $pattern) {
      $DefaultEndpointsProtocol = $matches['DefaultEndpointsProtocol']
      $EndpointSuffix = $matches['EndpointSuffix']
      $AccountName = $matches['AccountName']
      $AccountKey = $matches['AccountKey']
      $BlobEndpoint = $matches['BlobEndpoint']
  
      $secret = "$AccountKey;$BlobEndpoint"
      $secrets.Add($secret)
    }
    else {
      throw "No match found in connection string $connectionString"
    }
  }

  $secretValue = $secrets | ConvertTo-Json
 
  # Determine exactly which keyvault, subscription, etc the secret needs to go, and store it
  $environmentName = $null
  if ($parts[1].Contains('prod')) {
    $environmentName = 'prod'
  }
  elseif ($parts[1].Contains('test')) {
    $environmentName = 'test'
  }
  else {
    throw "Unknown purpose: $($parts[1])"
  }

  $regionName = $regionStampMapping[$region]
  $secretName = "BlobL3ConnectionStrings-$regionName-$environmentName"
  # We use a file instead of a hardcoded string because PowerShell acts weird around quoting and such.
  $secretFileName = "$secretName.json"
  Set-Content -Path $secretFileName -Value $secretValue

  foreach ($environment in $environmentVaultMapping.Keys) {
    $vaultName = $environmentVaultMapping[$environment].name
    $vaultSubscription = $environmentVaultMapping[$environment].subscription
  
    Write-Host "Setting secret $secretName in vault $vaultName (Subscription ID: $vaultSubscription) for $($connectionStrings.Count) storage accounts"
    $command = "az keyvault secret set --subscription `"$vaultSubscription`" --name `"$secretName`" --vault-name `"$vaultName`" --encoding utf-8 --file `"$secretFileName`""
    Invoke-Expression $command
  }
}
