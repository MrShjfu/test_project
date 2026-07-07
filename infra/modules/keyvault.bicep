// ADR-003: secrets (Postgres/Service Bus/Avalara/DocuSign) accessed via Managed Identity, never config files.
// Deploy wiring (per-module role assignments, secret provisioning, rotation) is infra-phase work.

param name string
param location string

param skuName string = 'standard'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  properties: {
    sku: {
      family: 'A'
      name: skuName
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
