param
(
  [Parameter(Mandatory = $true)]
  [ValidateSet('prod', 'test', 'ci', 'rm')]
  $environment,

  [Parameter(Mandatory = $true)]
  [ValidateSet('create', 'whatif', 'prepare')]
  $mode
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
elseif ($mode -eq 'create') {
  # The location here is the location that coordinates the Azure deployment, it has nothing to do with the location
  # where the resources are actually deployed to.
  az deployment sub create --subscription $subscription --location 'West US' --template-file main.bicep --parameters "@parameters.json"
}
elseif ($mode -eq 'whatif') {
  # The location here is the location that coordinates the Azure deployment, it has nothing to do with the location
  # where the resources are actually deployed to.
  az deployment sub what-if --subscription $subscription --location 'West US' --template-file main.bicep --parameters "@parameters.json"
}
