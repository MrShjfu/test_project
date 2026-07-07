// ADR-010: Azure Container Apps hosts the Helm.Host modular monolith (no AKS day 1).
// Deploy wiring (CI image push, revisions/blue-green, per-PR ephemeral apps) is infra-phase work.

param envName string
param appName string
param location string

@description('Container image for Helm.Host; replace with the built image reference in the pipeline.')
param image string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@minValue(0)
param minReplicas int = 1

@minValue(1)
param maxReplicas int = 3

@description('Env vars for the container. Values referencing secrets should use Key Vault references — wired in the infra phase (ADR-010, ADR-003).')
param envVars array = []

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${envName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource helmHostApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: appName
  location: location
  properties: {
    managedEnvironmentId: managedEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
    }
    template: {
      containers: [
        {
          name: 'helm-host'
          image: image
          env: envVars
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output fqdn string = helmHostApp.properties.configuration.ingress.fqdn
output name string = helmHostApp.name
