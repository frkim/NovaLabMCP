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

@description('Deploy the two Container Apps after the registry and environment bootstrap.')
param deployApps bool = false

@description('Immutable backend image reference, including its digest.')
param apiImage string = ''

@description('Immutable frontend image reference, including its digest.')
param webImage string = ''

@description('Exact HTTPS origin of the frontend Container App.')
param frontendOrigin string = 'https://localhost:3000'

@description('Microsoft Entra authority, for example https://login.microsoftonline.com/<tenant-id>/v2.0.')
param authAuthority string = ''

@description('Microsoft Entra API audience configured for the backend.')
param authAudience string = ''

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var logWorkspaceName = '${environmentName}-logs'
var containerEnvironmentName = '${environmentName}-aca'
var apiIdentityName = '${environmentName}-api-pull'
var webIdentityName = '${environmentName}-web-pull'
var apiHostname = '${environmentName}-api.${containerEnvironment.properties.defaultDomain}'
var webHostname = '${environmentName}-web.${containerEnvironment.properties.defaultDomain}'

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logWorkspaceName
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

resource apiPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: apiIdentityName
  location: location
}

resource webPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: webIdentityName
  location: location
}

resource apiAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, apiPullIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: apiPullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource webAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, webPullIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: webPullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerEnvironmentName
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

resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: '${environmentName}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiPullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registry.properties.loginServer
          identity: apiPullIdentity.id
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'NOVALABMCP_DEMO_AUTH_ENABLED'
              value: 'false'
            }
            {
              name: 'NOVALABMCP_AUTHORITY'
              value: authAuthority
            }
            {
              name: 'NOVALABMCP_AUDIENCE'
              value: authAudience
            }
            {
              name: 'NOVALABMCP_FRONTEND_ORIGIN'
              value: frontendOrigin
            }
            {
              name: 'NOVALABMCP_MCP_LOG_CAPACITY'
              value: '2000'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    apiAcrPull
  ]
}

resource web 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: '${environmentName}-web'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webPullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: registry.properties.loginServer
          identity: webPullIdentity.id
        }
      ]
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'API_PROXY_TARGET'
              value: 'https://${apiHostname}'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/'
                port: 3000
                scheme: 'HTTP'
              }
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    webAcrPull
  ]
}

output registryLoginServer string = registry.properties.loginServer
output apiUrl string = deployApps ? 'https://${apiHostname}' : ''
output webUrl string = deployApps ? 'https://${webHostname}' : ''
output apiPullPrincipalId string = apiPullIdentity.properties.principalId
output webPullPrincipalId string = webPullIdentity.properties.principalId
