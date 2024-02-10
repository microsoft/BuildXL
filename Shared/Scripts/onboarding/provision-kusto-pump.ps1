<#
.SYNOPSIS

Provisions a BuildXL log Kusto ingestion

.DESCRIPTION

This script creates a blob storage account under the provided subscription to be the target of bxl logs. It assumes the existence of a Kusto cluster where logs will be sent to, 
and creates a database and tables (one for the main bxl logs and one for the cache bxl logs). Event hubs/grids/data connections are also created and configured.
Assumes scripts uploaded to https://bxlscripts.blob.core.windows.net/provisioning.
This script should be run in a session that is already logged into Azure, with a user that has permissions to create a blob storage account under the specified subscription.

.PARAMETER resourceGroup

The Azure resource group the created resouces will belong to. The resource group is created if it does not exist.

.PARAMETER subscription

The Azure subscription the created resources will belong to. The subscription should exist already.

.PARAMETER azureRegion

The Azure location (geo-region) where to create the resources.

.PARAMETER blobAccountName

The name of the blob storage account to be created as the endpoint for the logs. The storage account name is what BuildXL will need to know where to send logs to.

.PARAMETER clusterName

The name of the existing Kusto cluster name where corresponding tables will be created.

.PARAMETER mode

'create' (default). The script will create the specified blob account.
'what-if'. The what-if operation doesn't make any changes to existing resources. Instead, it predicts the changes if the specified template is deployed.

.EXAMPLE

az login
iex "& { $(irm  https://bxlscripts.blob.core.windows.net/provisioning/provision-l3cache.ps1) } -resourceGroup mygroup -subscription 7d3b156b-1072-4060-97f6-42057dc19952 -azureRegion eastus -blobAccountName blobl3test -clusterName myCluster"


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
  [string]$clusterName,

  [Parameter(Mandatory = $true)]
  [string]$blobAccountName,

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
        "clusterName": {
            "value": "$clusterName"
        },
        "blobAccountName": {
            "value": "$blobAccountName"
        },
    }
}
"@;
    Set-Content -Path parameters.json -Value $Parameters
    
    # Download the bicep and kusto scripts
    Invoke-WebRequest "https://bxlscripts.blob.core.windows.net/provisioning/provision-kusto-pump.bicep" -OutFile provision-kusto-pump.bicep
    Invoke-WebRequest "https://bxlscripts.blob.core.windows.net/provisioning/provision-kusto-tables.kql" -OutFile provision-kusto-tables.kql

    $command = "az deployment group $mode --subscription $subscription --resource-group $resourcegroup --template-file provision-kusto-pump.bicep --parameters `"@parameters.json`" --mode incremental"
    Write-Warning "Running command: $command"
    Invoke-Expression $command
}
finally {
    Remove-Item parameters.json -Force -ErrorAction SilentlyContinue
    Remove-Item provision-kusto-pump.bicep -Force -ErrorAction SilentlyContinue
    Remove-Item provision-kusto-tables.kql -Force -ErrorAction SilentlyContinue
}