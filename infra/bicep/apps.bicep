targetScope = 'resourceGroup'

@description('Short, globally unique prefix for the demo resources.')
@minLength(3)
@maxLength(16)
param environmentName string

@description('Azure region of the existing Container Apps environment.')
param location string

@description('Globally unique lowercase Azure Container Registry name.')
@minLength(5)
@maxLength(50)
param registryName string

@description('Immutable backend image reference, including its digest.')
param apiImage string

@description('Immutable frontend image reference, including its digest.')
param webImage string

@description('Microsoft Entra authority, for example https://login.microsoftonline.com/<tenant-id>/v2.0.')
param authAuthority string

@description('Microsoft Entra API audience configured for the backend.')
param authAudience string

var containerEnvironmentName = '${environmentName}-aca'
var apiIdentityName = '${environmentName}-api-pull'
var webIdentityName = '${environmentName}-web-pull'
var apiHostname = '${environmentName}-api.${containerEnvironment.properties.defaultDomain}'
var webHostname = '${environmentName}-web.${containerEnvironment.properties.defaultDomain}'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource apiPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: apiIdentityName
}

resource webPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: webIdentityName
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerEnvironmentName
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
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
              value: 'https://${webHostname}'
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
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
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
}

output apiUrl string = 'https://${apiHostname}'
output webUrl string = 'https://${webHostname}'
