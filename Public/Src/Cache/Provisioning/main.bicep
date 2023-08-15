@description('Azure locations in which to deploy identical copies. Must be valid Azure Resource Manager locations')
param locations array = [
  'westus2'
  'centralus'
  'westcentralus'
]

@allowed([
  'Standard_LRS'
  'Premium_LRS'
])
param sku string = 'Premium_LRS'

@allowed([
  'StorageV2'
  'BlockBlobStorage'
])
param kind string = 'BlockBlobStorage'

@minValue(1)
@maxValue(99999)
@description('Number of shards to create')
param shards int

@minLength(1)
@maxLength(9)
@description('String that indicates who or what this instance is for. MUST be all lowercase letters and numbers')
param purpose string

@allowed([
  'service'
  'engine'
])
param gcStrategy string = 'service'

targetScope = 'subscription'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = [for locidx in range(0, length(locations)): {
  name: 'blobl3-${purpose}-${locations[locidx]}'
  location: locations[locidx]
}]

module BlobL3Module 'location.bicep' = [for locidx in range(0, length(locations)): {
  scope: resourceGroup[locidx]
  name: 'DeployStorage${purpose}At${locations[locidx]}'
  params: {
    location: locations[locidx]
    sku: sku
    kind: kind
    shards: shards
    purpose: purpose
    gcStrategy: gcStrategy
  }
}]
