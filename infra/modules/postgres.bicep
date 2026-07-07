// ADR-002: PostgreSQL Flexible Server, schema per module. Overview §11: zone-redundant HA is prod-only.
// Deploy wiring (backup policy tuning, connection pooling config, geo-replica per ADR-011) is infra-phase work.

param serverName string
param location string

@allowed(['dev', 'staging', 'prod'])
param env string

param administratorLogin string = 'helmadmin'

@secure()
param administratorLoginPassword string

@description('Burstable SKU by default; prod may move to a higher tier — sizing is infra-phase work.')
param skuName string = 'Standard_B1ms'

param postgresVersion string = '16'

// Zone-redundant HA is prod-only per overview §11; other envs run single-zone to save cost.
var haMode = env == 'prod' ? 'ZoneRedundant' : 'Disabled'

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: serverName
  location: location
  sku: {
    name: skuName
    tier: 'Burstable'
  }
  properties: {
    version: postgresVersion
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: env == 'prod' ? 'Enabled' : 'Disabled'
    }
    highAvailability: {
      mode: haMode
    }
  }
}

resource helmDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  parent: server
  name: 'helm'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

output name string = server.name
output fqdn string = server.properties.fullyQualifiedDomainName
