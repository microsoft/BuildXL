<#
.SYNOPSIS

Provisions a blob account to be used as an L3 cache.

.DESCRIPTION

This script creates a blob storage account under the provided subscription and sets up lifetime management policies to take care of evicting old content.
Assumes scripts uploaded to https://bxlscripts.blob.core.windows.net/provisioning.
This script should be run in a session that is already logged into Azure, with a user that has permissions to create a blob storage account under the specified subscription.

.PARAMETER resourceGroup

The Azure resource group the created blob storage account will belong to. The resource group is created if it does not exist.

.PARAMETER subscription

The Azure subscription the created blob storage account will belong to. The subscription should exist already.

.PARAMETER azureRegion

The Azure location (geo-region) where to create the blob storage account.

.PARAMETER blobAccountName

The name of the blob storage account to be created.

.PARAMETER retentionPolicy

The retention policy in days to be used. The storage account will start evicting content that hasn't been accessed after the specified number of days.
If any numer different than the default is explicitly passed, make sure the corresponding cache config file used by the build engine has a matching retention policy.

.PARAMETER mode

'create' (default). The script will create the specified blob account.
'what-if'. The what-if operation doesn't make any changes to existing resources. Instead, it predicts the changes if the specified template is deployed.

.EXAMPLE

az login
iex "& { $(irm  https://bxlscripts.blob.core.windows.net/provisioning/provision-l3cache.ps1) } -resourceGroup mygroup -subscription 7d3b156b-1072-4060-97f6-42057dc19952 -azureRegion eastus -blobAccountName blobl3test"


#>

param
(
  [Parameter(Mandatory = $true)]
  [string]$resourceGroup,

  [Parameter(Mandatory = $true)]
  [string]$subscription,

  [Parameter(Mandatory = $true)]
  [string]$azureRegion,

  [Parameter(Mandatory = $true)]
  [string]$blobAccountName,

  [Parameter(Mandatory = $false)]
  [int]$retentionPolicy = 6,

  [Parameter(Mandatory = $false)]
  [ValidateSet('create', 'what-if')]
  [string]$mode = 'create'
)

# Set subscription and create resource group
az account set --subscription $subscription
az group create --name $resourcegroup --location $azureRegion

# Run the bicep script
try {
    $Parameters = @"
{
    "`$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
    "contentVersion": "1.0.0.0",
    "parameters": {
        "azureRegion": {
            "value": "$azureRegion"
        },
        "blobAccountName": {
            "value": "$blobAccountName"
        },
        "retentionPolicy": {
            "value": $retentionPolicy
        },
    }
}
"@;
    Set-Content -Path parameters.json -Value $Parameters
    
    # Download the bicep script
    Invoke-WebRequest "https://bxlscripts.blob.core.windows.net/provisioning/provision-l3cache.bicep" -OutFile provision-l3cache.bicep

    $command = "az deployment group $mode --subscription $subscription --resource-group $resourcegroup --template-file provision-l3cache.bicep --parameters `"@parameters.json`" --mode incremental"
    Write-Warning "Running command: $command"
    Invoke-Expression $command
}
finally {
    Remove-Item parameters.json -Force -ErrorAction SilentlyContinue
    Remove-Item provision-l3cache.bicep -Force -ErrorAction SilentlyContinue
}

