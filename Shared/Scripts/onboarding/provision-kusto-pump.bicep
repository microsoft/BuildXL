param clusterName string
param azureRegion string
param blobAccountName string

// The Kusto instance is assumed to be already created
resource cluster 'Microsoft.Kusto/clusters@2022-02-01' existing = {
    name: clusterName
}

// Create the database to hold the logs
resource bxldatabase 'Microsoft.Kusto/clusters/databases@2023-08-15' = {
  name: 'BuildXLLogs'
  kind: 'ReadWrite'
  location: azureRegion
  parent: cluster
}

// Create tables for both the main log and the cache log, together with ingestion mappings
resource provisiontables 'Microsoft.Kusto/clusters/databases/scripts@2023-08-15' = {
  name: 'provision-tables'
  parent: bxldatabase
  properties: {
    continueOnErrors: false
    scriptContent: loadTextContent('provision-kusto-tables.kql')
  }
}

// Create the managed identity that will have access to the storage account
resource kustoWriter 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'bxl_kusto_writer'
  location: azureRegion
}

// Create the blob account that will act as the endpoint for receiving the log messages
resource managementStorageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: blobAccountName
  location: azureRegion
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    dnsEndpointType: 'Standard'
  }
}

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#storage-blob-data-contributor
resource storageBlobDataContributor 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: managementStorageAccount
  name: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

// Assign the managed identity to have 'storage blob data contributor' access to the blob
resource managedIdentityAsWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, kustoWriter.id, managementStorageAccount.id)
  scope: managementStorageAccount
  properties: {
    roleDefinitionId: storageBlobDataContributor.id
    principalId: kustoWriter.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Assign the cluster to have 'storage blob data contributor' access to the blob
resource clusterAsWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, cluster.id, managementStorageAccount.id)
  scope: managementStorageAccount
  properties: {
    roleDefinitionId: storageBlobDataContributor.id
    principalId: cluster.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Set a lifetime management policy so log blobs don't remain forever (since they will be ingested into kusto)
resource logremoval 'Microsoft.Storage/storageAccounts/managementPolicies@2022-09-01' = {
    name: 'default'
    parent: managementStorageAccount
    properties: {
      policy: {
        rules: [
          {
            definition: {
              actions: {
                baseBlob: {
                  delete: {
                    daysAfterCreationGreaterThan: 2
                  }
                }
              }
              filters: {
                blobTypes: [
                  'blockBlob'
                ]
              }
            }
            enabled: true
            name: 'logs-garbage-collection'
            type: 'Lifecycle'
          }
        ]
      }
    }
}

// Create an event hub to host the instances in charge of the ingestion
resource eventhublogsnamespace 'Microsoft.EventHub/namespaces@2022-10-01-preview' = {
  name: 'EventHub${guid(resourceGroup().id, bxldatabase.id)}'
  location: azureRegion
  sku: {
    capacity: 1
    name: 'Standard'
    tier: 'Standard'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // This disables SAS tokens as an auth mechanism.
    // Managed identities is the recommended path
    disableLocalAuth: true
  }
}

// Create an event hub instance in charge of the main log ingestion
resource bxlLogsHubsInstance 'Microsoft.EventHub/namespaces/eventhubs@2022-10-01-preview' = {
  name: 'EventHubMainLogs${guid(resourceGroup().id, bxldatabase.id)}'
  parent: eventhublogsnamespace
  properties: {
    messageRetentionInDays: 7
    partitionCount: 8
    retentionDescription: {
      cleanupPolicy: 'Delete'
      retentionTimeInHours: 168
    }
    status: 'Active'
  }
}

// Create an event hub instance in charge of the cache log ingestion
resource bxlCacheLogsHubsInstance 'Microsoft.EventHub/namespaces/eventhubs@2022-10-01-preview' = {
  name: 'EventHubCacheLogs${guid(resourceGroup().id, bxldatabase.id)}'
  parent: eventhublogsnamespace
  properties: {
    messageRetentionInDays: 7
    partitionCount: 8
    retentionDescription: {
      cleanupPolicy: 'Delete'
      retentionTimeInHours: 168
    }
    status: 'Active'
  }
}

// There needs to be a single system topics at the subscription level
resource logeventgrid 'Microsoft.EventGrid/systemTopics@2023-12-15-preview' = {
  name: 'LogEventTopic${guid(resourceGroup().id, bxldatabase.id)}'
  location: azureRegion

  properties: {
    source: managementStorageAccount.id
    topicType: 'Microsoft.Storage.StorageAccounts'
  }
  // It is important to turn on identities for the 
  // log event grid so it can auth with it against the event hub
  identity: {
    type: 'SystemAssigned'
  }
}

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/analytics#azure-event-hubs-data-sender
resource cacheLogsDataSender 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: bxlCacheLogsHubsInstance
  name: '2b629674-e913-4c01-ae53-ef4638d8f975'
}

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles/analytics#azure-event-hubs-data-sender
resource mainLogsDataSender 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: bxlLogsHubsInstance
  name: '2b629674-e913-4c01-ae53-ef4638d8f975'
}

// Allow the event grid to send main log messages to the main log hub
resource mainLogsSenderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, cluster.id, bxlMainLogsConnections.id, 'sender')
  scope: bxlLogsHubsInstance
  properties: {
    roleDefinitionId: mainLogsDataSender.id
    principalId: logeventgrid.identity.principalId
  }
}

// Allow the event grid to send cache log messages to the cache log hub
resource cacheLogsSenderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, cluster.id, bxlCacheLogsConnections.id, 'sender')
  scope: bxlCacheLogsHubsInstance
  properties: {
    roleDefinitionId: cacheLogsDataSender.id
    principalId: logeventgrid.identity.principalId
  }
}

// Grid subscription for the main logs
resource logEventMainLogSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2023-12-15-preview' = {
  parent: logeventgrid
  name: 'LogEventSubscription${guid(resourceGroup().id, bxldatabase.id, bxlLogsHubsInstance.id)}'
  properties: {
    deliveryWithResourceIdentity:{
      destination: {
        endpointType: 'EventHub'
        properties: {
          resourceId: bxlLogsHubsInstance.id
        }
      }
      identity: {
        type: 'SystemAssigned'
      }
    }
    filter: {
      includedEventTypes: [
        'Microsoft.Storage.BlobCreated'
      ]
      // This should correspond to the container 'logs' where main logs should be written to
      subjectBeginsWith: '/blobServices/default/containers/logs'
    }
    eventDeliverySchema: 'EventGridSchema'
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
}

// Grid subscription for the cache logs
resource logEventCacheLogSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2023-12-15-preview' = {
  parent: logeventgrid
  name: 'LogCacheEventSubscription${guid(resourceGroup().id, bxldatabase.id, bxlLogsHubsInstance.id)}'
  properties: {
    deliveryWithResourceIdentity:{
      destination: {
        endpointType: 'EventHub'
        properties: {
          resourceId: bxlLogsHubsInstance.id
        }
      }
      identity: {
        type: 'SystemAssigned'
      }
    }
    filter: {
      includedEventTypes: [
        'Microsoft.Storage.BlobCreated'
      ]
      // This should correspond to the container 'logscache' where cache logs should be written to.
      // By convention the container where cache logs go is the result of adding the suffix 'cache' to the main log container
      subjectBeginsWith: '/blobServices/default/containers/logscache'
    }
    eventDeliverySchema: 'EventGridSchema'
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
}

// Data connection for the main logs
resource bxlMainLogsConnections 'Microsoft.Kusto/clusters/databases/dataConnections@2023-08-15' = {
  name: 'bxllogsconnection'
  parent: bxldatabase
  location: azureRegion
  kind: 'EventGrid'
  properties: {
    blobStorageEventType: 'Microsoft.Storage.BlobCreated'
    consumerGroup: '$Default'
    databaseRouting: 'Multi'
    dataFormat: 'PSV'
    eventGridResourceId: logeventgrid.id
    eventHubResourceId: bxlLogsHubsInstance.id
    managedIdentityResourceId: cluster.id
    mappingRuleName: 'BuildXLIngestion' 
    storageAccountResourceId: managementStorageAccount.id
    tableName: 'BuildXLLogs' 
    ignoreFirstRecord: false
  }
}

// Data connection for the cache logs
resource bxlCacheLogsConnections 'Microsoft.Kusto/clusters/databases/dataConnections@2023-08-15' = {
  name: 'bxlcachelogsconnection'
  parent: bxldatabase
  location: azureRegion
  kind: 'EventGrid'
  properties: {
    blobStorageEventType: 'Microsoft.Storage.BlobCreated'
    consumerGroup: '$Default'
    databaseRouting: 'Multi'
    dataFormat: 'CSV'
    eventGridResourceId: logeventgrid.id
    eventHubResourceId: bxlCacheLogsHubsInstance.id
    managedIdentityResourceId: cluster.id
    mappingRuleName: 'BuildXLCacheIngestion' 
    storageAccountResourceId: managementStorageAccount.id
    tableName: 'BuildXLCacheLogs' 
    ignoreFirstRecord: false
  }
}

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#azure-event-hubs-data-receiver
resource mainLogsDataReceiver 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: bxlLogsHubsInstance
  name: 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
}

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#azure-event-hubs-data-receiver
resource cacheLogsDataReceiver 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: bxlCacheLogsHubsInstance
  name: 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
}

// Assign the system assign identity of the cluster to have 'event hubs data receiver' access to the main logs hub instance
resource mainLogsReceiveRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, cluster.id, bxlMainLogsConnections.id)
  scope: bxlLogsHubsInstance
  properties: {
    roleDefinitionId: mainLogsDataReceiver.id
    principalId: cluster.identity.principalId
  }
}

// Assign the system assigned identity of the cluster to have 'event hubs data receiver' access to the cache logs hub instance
resource cacheLogsReceiveRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, cluster.id, bxlCacheLogsConnections.id)
  scope: bxlCacheLogsHubsInstance
  properties: {
    roleDefinitionId: cacheLogsDataReceiver.id
    principalId: cluster.identity.principalId
  }
}
