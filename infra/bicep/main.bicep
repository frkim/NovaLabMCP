targetScope = 'resourceGroup'

@description('Short, globally unique prefix for the demo resources.')
@minLength(3)
@maxLength(16)
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Globally unique lowercase Azure Container Registry name.')
@minLength(5)
@maxLength(50)
param registryName string

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var components = ['api', 'web']

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${environmentName}-logs'
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource pullIdentities 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = [for component in components: {
  name: '${environmentName}-${component}-pull'
  location: location
}]

resource pullRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for i in range(0, length(components)): {
  name: guid(registry.id, pullIdentities[i].id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: pullIdentities[i].properties.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${environmentName}-aca'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logWorkspace.properties.customerId
        sharedKey: logWorkspace.listKeys('2022-10-01').primarySharedKey
      }
    }
  }
}

output registryLoginServer string = registry.properties.loginServer
output apiUrl string = 'https://${environmentName}-api.${containerEnvironment.properties.defaultDomain}'
output webUrl string = 'https://${environmentName}-web.${containerEnvironment.properties.defaultDomain}'
output apiPullPrincipalId string = pullIdentities[0].properties.principalId
output webPullPrincipalId string = pullIdentities[1].properties.principalId
