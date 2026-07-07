// Root composition — deploys the five infra modules for one environment.
// Deploy wiring (pipelines, parameter files per env, approvals) is infra-phase work.

targetScope = 'resourceGroup'

@allowed(['dev', 'staging', 'prod'])
param env string

param location string = resourceGroup().location

@description('Container image for Helm.Host, e.g. myregistry.azurecr.io/helm-host:latest')
param helmHostImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@minValue(0)
param containerMinReplicas int = 1

@minValue(1)
param containerMaxReplicas int = 3

@secure()
param postgresAdminPassword string

param postgresSkuName string = 'Standard_B1ms'

param staticWebAppsSkuName string = 'Free'

var namePrefix = 'helm'

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault-${env}'
  params: {
    name: '${namePrefix}-kv-${env}'
    location: location
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres-${env}'
  params: {
    serverName: '${namePrefix}-psql-${env}'
    location: location
    env: env
    administratorLoginPassword: postgresAdminPassword
    skuName: postgresSkuName
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-${env}'
  params: {
    namespaceName: '${namePrefix}-sb-${env}'
    location: location
  }
}

module containerApps 'modules/container-apps.bicep' = {
  name: 'container-apps-${env}'
  params: {
    envName: '${namePrefix}-cae-${env}'
    appName: '${namePrefix}-host-${env}'
    location: location
    image: helmHostImage
    minReplicas: containerMinReplicas
    maxReplicas: containerMaxReplicas
  }
}

module staticWebApps 'modules/static-web-apps.bicep' = {
  name: 'static-web-apps-${env}'
  params: {
    namePrefix: namePrefix
    env: env
    location: location
    skuName: staticWebAppsSkuName
  }
}

output keyVaultName string = keyVault.outputs.name
output postgresServerName string = postgres.outputs.name
output serviceBusNamespaceName string = serviceBus.outputs.name
output containerAppFqdn string = containerApps.outputs.fqdn
output staticWebAppNames array = staticWebApps.outputs.names
