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

@minValue(0)
@maxValue(99999)
param shard int

@minLength(1)
@maxLength(9)
param purpose string

// See: https://docs.microsoft.com/en-us/azure/storage/blobs/storage-feature-support-in-storage-accounts
// SKU:
//   'Premium_LRS'
//   'Premium_ZRS'
//   'Standard_GRS'
//   'Standard_GZRS'
//   'Standard_LRS'
//   'Standard_RAGRS'
//   'Standard_RAGZRS'
//   'Standard_ZRS'
// Kind:
//   'BlobStorage'
//   'BlockBlobStorage'
//   'FileStorage'
//   'Storage'
//   'StorageV2'

// Please note, the unique portion takes into consideration all variables in order to ensure we can always provision
var unique = uniqueString(resourceGroup().id, purpose, location, string(shard), sku, kind)
// Must be between 3 and 24 characters, numbers and lowercase letters only
// Azure storage collocates storage accounts based on their names, so it matters that each storage account in the system
// gets a unique prefix, as it ensures that they wind up in different servers.
// Naming convention is: {10 chars - deterministic unique}{5 chars - shard number}{9 chars - purpose}
var accountName = '${substring(unique, 0, 10)}${padLeft(shard, 5, '0')}${substring(purpose, 0, min(length(purpose), 9))}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2021-09-01' = {
  name: accountName
  location: location
  tags: {
    location: location
    system: 'BlobL3'
    purpose: purpose
    shard: string(shard)
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
  }

  resource blobService 'blobServices@2021-09-01' = {
    name: 'default'
    properties: {
      changeFeed: {
        enabled: true
        retentionInDays: 7
      }
      lastAccessTimeTrackingPolicy: {
        enable: true
      }
      isVersioningEnabled: false
    }
  }

  resource lifecycleManagement 'managementPolicies@2021-09-01' = {
    name: 'default'
    dependsOn: [
      blobService
    ]
    properties: {
      policy: {
        rules: [
          {
            definition: {
              actions: {
                baseBlob: {
                  delete: {
                    // NOTE: this must always be at least one day greater than metadata, to give some leeway for builds
                    // that get cache hits of metadata and take some time to access the actual content
                    daysAfterLastAccessTimeGreaterThan: 3
                  }
                }
              }
              filters: {
                blobTypes: [
                  'blockBlob'
                ]
                prefixMatch: [
                  'content'
                ]
              }
            }
            enabled: true
            name: 'content-gc'
            type: 'Lifecycle'
          }
          {
            definition: {
              actions: {
                baseBlob: {
                  delete: {
                    daysAfterLastAccessTimeGreaterThan: 2
                  }
                }
              }
              filters: {
                blobTypes: [
                  'blockBlob'
                ]
                prefixMatch: [
                  'metadata'
                ]
              }
            }
            enabled: true
            name: 'metadata-gc'
            type: 'Lifecycle'
          }
        ]
      }
    }
  }
}

output storageInformation object = {
  id: storageAccount.id
}
