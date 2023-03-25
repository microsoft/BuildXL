param
(
  [Parameter(Mandatory = $true)]
  [ValidateSet('prod', 'test', 'ci')]
  $environment,

  [Parameter(Mandatory = $true)]
  [ValidateSet('create', 'whatif')]
  $mode
)

$subscriptions = @{
  prod = "7965fc55-7602-4cf6-abe4-e081cf119567"
  test = "bf933bbb-8131-491c-81d9-26d7b6f327fa"
  ci   = "bf933bbb-8131-491c-81d9-26d7b6f327fa"
}

$subscription = $subscriptions[$environment]

az account set --subscription $subscription
Write-Host "Running $mode deployment to $environment environment (Subscription ID: $subscription)"
if ($mode -eq 'create') {
  # The location here is the location that coordinates the Azure deployment, it has nothing to do with the location
  # where the resources are actually deployed to.
  az deployment sub create --subscription $subscription --location 'West US' --template-file main.bicep --parameters "@parameters.json"
}
elseif ($mode -eq 'whatif') {
  # The location here is the location that coordinates the Azure deployment, it has nothing to do with the location
  # where the resources are actually deployed to.
  az deployment sub what-if --subscription $subscription --location 'West US' --template-file main.bicep --parameters "@parameters.json"
}
