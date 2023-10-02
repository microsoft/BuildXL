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
@maxValue(99999)
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
