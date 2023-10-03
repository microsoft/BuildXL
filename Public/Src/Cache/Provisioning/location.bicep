param location string

@allowed([
  'Standard_LRS'
  'Premium_LRS'
])
param sku string

@allowed([
  'StorageV2'
  'BlockBlobStorage'
])
param kind string

@minValue(1)
@maxValue(99998)
param shards int

@minLength(1)
@maxLength(9)
param purpose string

@allowed([
  'service'
  'engine'
])
param gcStrategy string

@allowed([
  'Standard'
  'AzureDnsZone'
])
param dns string = 'Standard'

// The following creates one storage account per shard that's going to be used. This is where fingerprints and content
// are stored, and what both the datacenter and dev cache will access to obtain cache hits.
module shard 'shard.bicep' = [for shard in range(0, shards): {
  name: 'DeployStorage${purpose}At${location}Shard${shard}'
  params: {
    location: location
    sku: sku
    kind: kind
    shard: shard
    purpose: purpose
    gcStrategy: gcStrategy
    dns: dns
  }
}]

// The management storage account is the following reserved name. The name follows the naming convention in shard.bicep.
// It is used to store metadata about the account, such as files required for garbage collection, configuration, etc.
var unique = uniqueString(resourceGroup().id, location, sku, kind, 'mgmt', purpose, gcStrategy)
var accountName = 'mgmt${substring(unique, 0, 6)}99999${substring(purpose, 0, min(length(purpose), 9))}'

resource managementStorageAccount 'Microsoft.Storage/storageAccounts@2021-09-01' = {
  name: accountName
  location: location
  tags: {
    location: location
    system: 'BlobL3'
    purpose: purpose
    shard: 'management'
    storageSku: sku
    storageKind: kind
  }
  sku: {
    name: sku
  }
  kind: kind

  properties: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    dnsEndpointType: dns
  }

  resource managementBlobService 'blobServices@2021-09-01' = {
    name: 'default'
    properties: {
      lastAccessTimeTrackingPolicy: {
        enable: true
      }
    }
  }

  resource managementLifecycleManagement 'managementPolicies@2021-09-01' = {
    name: 'default'
    dependsOn: [
      managementBlobService
    ]
    properties: {
      policy: {
        rules: [
          {
            definition: {
              actions: {
                baseBlob: {
                  delete: {
                    daysAfterLastAccessTimeGreaterThan: 30
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
            name: 'unused-gc'
            type: 'Lifecycle'
          }
        ]
      }
    }
  }
}
