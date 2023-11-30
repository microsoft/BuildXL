param blobAccountName string
param azureRegion string
param retentionPolicy int

resource managementStorageAccount 'Microsoft.Storage/storageAccounts@2021-09-01' = {
  name: blobAccountName
  location: azureRegion
  sku: {
    name: 'Premium_LRS'
  }
  kind: 'BlockBlobStorage'

  properties: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    dnsEndpointType: 'Standard'
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
                    daysAfterLastAccessTimeGreaterThan: retentionPolicy
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
            name: 'metadata-garbage-collection'
            type: 'Lifecycle'
          }
          {
            definition: {
              actions: {
                baseBlob: {
                  delete: {
                    daysAfterLastAccessTimeGreaterThan: retentionPolicy + 1
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
            name: 'content-garbage-collection'
            type: 'Lifecycle'
          }
        ]
      }
    }
  }
}
