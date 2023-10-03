param
(
  [Parameter(Mandatory = $true)]
  [ValidateSet('prod', 'test', 'ci', 'rm')]
  $environment,

  [Parameter(Mandatory = $true)]
  [ValidateSet('prepare', 'create', 'what-if', 'delete')]
  $mode,

  [string[]]$locations,

  [ValidateSet('Standard_LRS', 'Premium_LRS')]
  [string]$sku = 'Premium_LRS',

  [ValidateSet('StorageV2', 'BlockBlobStorage')]
  [string]$kind = 'BlockBlobStorage',

  [ValidateRange(1, 99998)]
  [int]$shards,

  [ValidateLength(1, 9)]
  [string]$purpose,

  [ValidateSet('service', 'engine')]
  [string]$gcStrategy = 'service',

  [ValidateSet('Standard', 'AzureDnsZone')]
  [string]$dns = 'Standard'
)

$subscriptions = @{
  prod = "7965fc55-7602-4cf6-abe4-e081cf119567"
  test = "bf933bbb-8131-491c-81d9-26d7b6f327fa"
  ci   = "bf933bbb-8131-491c-81d9-26d7b6f327fa"
  rm   = "2ab82c67-48f1-42ca-a817-67f4013eca86"
}

$subscription = $subscriptions[$environment]

az account set --subscription $subscription

Write-Host "Running $mode deployment to $environment environment (Subscription ID: $subscription)"
if ($mode -eq 'prepare') {
  az feature unregister --subscription $subscription --namespace Microsoft.Storage --name PartitionedDns
  az feature register --subscription $subscription --namespace Microsoft.Storage --name PartitionedDnsPublicPreview

  while ((az provider register -n Microsoft.Storage | Out-String).Trim().Length -ne 0) {
    Write-Host "Waiting for Microsoft.Storage provider to be registered..."
    Start-Sleep -Seconds 30
  }
}
elseif ($mode -eq 'delete') {
  if ($purpose -notmatch '^[a-z0-9]+$') {
    throw "Purpose must be all lowercase, digits, and non-empty"
  }

  foreach ($location in $locations) {
    $resourcegroup = "blobl3-$purpose-$location"
    az group delete --yes --no-wait --name $resourcegroup
  }
}
elseif ($mode -eq 'create' -or $mode -eq 'what-if') {
  if ($purpose -notmatch '^[a-z0-9]+$') {
    throw "Purpose must be all lowercase, digits, and non-empty"
  }

  if ($shards -lt 1 -or $shards -gt 99998) {
    throw "Shards must be between 1 and 99998"
  }

  foreach ($location in $locations) {
    $resourcegroup = "blobl3-$purpose-$location"
    az group create --name $resourcegroup --location $location
  
    try {
      $Parameters = @"
{
  "`$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "location": {
      "value": "$location"
    },
    "sku": {
      "value": "$sku"
    },
    "kind": {
      "value": "$kind"
    },
    "shards": {
      "value": $shards
    },
    "purpose": {
      "value": "$purpose"
    },
    "gcStrategy": {
      "value": "$gcStrategy"
    },
    "dns": {
      "value": "$dns"
    }
  }
}
"@;
      Set-Content -Path parameters.json -Value $Parameters

      $command = "az deployment group $mode --subscription $subscription --resource-group $resourcegroup --template-file location.bicep --parameters `"@parameters.json`" --mode complete"
      Write-Warning "Running command: $command"
      Invoke-Expression $command
    }
    finally {
      Remove-Item parameters.json -Force -ErrorAction SilentlyContinue
    }
  }
}
