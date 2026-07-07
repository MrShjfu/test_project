// ADR-004: Azure Service Bus is the recommended IEventBus transport for staging/prod
// (RabbitMQ locally, in-memory in tests — swapping transports is config-only, no module code changes).
// Deploy wiring (per-module subscriptions, DLQ alerts, dedup window tuning) is infra-phase work.

param namespaceName string
param location string

param skuName string = 'Standard'

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
}

resource helmEventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: namespace
  name: 'helm-events'
  properties: {
    defaultMessageTimeToLive: 'P14D'
  }
}

output name string = namespace.name
output topicName string = helmEventsTopic.name
