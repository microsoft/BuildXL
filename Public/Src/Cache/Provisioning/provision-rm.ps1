$locations = @(
  "northcentralus",
  "southcentralus",
  "centralus",
  "westus",
  "westus2",
  "eastus2"
);
.\provision.ps1 -environment rm -mode create -locations $locations -purpose "cbrmprod" -shards 100 -dns AzureDnsZone
.\provision.ps1 -environment rm -mode create -locations $locations -purpose "cbrmtest" -shards 50 -dns AzureDnsZone