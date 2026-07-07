// ADR-008/ADR-010: three frontends (Internal Platform, Customer Portal, Factory Kiosk), each its own
// BFF-backed SWA, no API gateway. Deploy wiring (build pipelines, custom domains, Front Door) is infra-phase work.

param namePrefix string
param location string

@allowed(['dev', 'staging', 'prod'])
param env string

param skuName string = 'Free'

var apps = [
  'web-internal'
  'web-portal'
  'web-kiosk'
]

resource staticSites 'Microsoft.Web/staticSites@2023-01-01' = [
  for app in apps: {
    name: '${namePrefix}-${app}-${env}'
    location: location
    sku: {
      name: skuName
      tier: skuName
    }
    properties: {}
  }
]

output names array = [for i in range(0, length(apps)): staticSites[i].name]
