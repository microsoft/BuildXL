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

@allowed([
  'Standard'
  'AzureDnsZone'
])
param dns string = 'Standard'

// This has to be done this way because we need to scope the deployment of the `BlobL3Module` below to the resource
// group `resourceGroup`, which we also have to create. Therefore, the deployment consists of creating all the resource
// groups and then creating the resources for each resource group separately.
targetScope = 'subscription'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = [for locidx in range(0, length(locations)): {
  // REMARK: the number of shards isn't part of the unique name that we generate because we might want to reshard 
  // later, and then it'd be a hassle for it to be part of the name because all accounts would wind up with a different
  // name.
  name: 'blobl3-${purpose}-${locations[locidx]}-${uniqueString(locations[locidx], sku, kind, purpose, gcStrategy)}'
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
    dns: dns
  }
}]
