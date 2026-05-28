@description('The name of the Azure Function App')
param functionAppName string = 'bussin-backend-${uniqueString(resourceGroup().id)}'

@description('The name of the Cosmos DB Account')
param cosmosDbAccountName string = 'bussin-db-${uniqueString(resourceGroup().id)}'

@description('The Azure region where resources will be deployed')
param location string = resourceGroup().location

// --- Storage Account ---
var storageAccountName = 'bussinstg${uniqueString(resourceGroup().id)}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
  }
}

// --- Log Analytics Workspace (Required for App Insights) ---
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'bussin-logs-${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// --- Application Insights ---
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'bussin-insights-${uniqueString(resourceGroup().id)}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

// --- App Service Plan (Flex Consumption Plan Serverless Linux) ---
resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'bussin-plan-${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true // Linux Plan
  }
}

// --- Cosmos DB Account (Serverless Capacity Mode) ---
resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-09-15' = {
  name: cosmosDbAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless' // Sets Serverless billing mode ($0/month base cost)
      }
    ]
  }
}

// --- Cosmos DB Database ---
resource cosmosDbDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-09-15' = {
  parent: cosmosDbAccount
  name: 'BussinDb'
  properties: {
    resource: {
      id: 'BussinDb'
    }
  }
}

// --- Cosmos DB Container ---
resource cosmosDbContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-09-15' = {
  parent: cosmosDbDatabase
  name: 'Logins'
  properties: {
    resource: {
      id: 'Logins'
      partitionKey: {
        paths: [
          '/userId'
        ]
        kind: 'Hash'
      }
    }
  }
}

// --- Blob Services and Deployment Container ---
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'package'
  properties: {
    publicAccess: 'None'
  }
}

// --- Function App (Flex Consumption) ---
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    reserved: true
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageAccount.properties.primaryEndpoints.blob}package'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccount.name
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'CosmosDbEndpoint'
          value: 'https://${cosmosDbAccount.name}.documents.azure.com:443/'
        }
        {
          name: 'CosmosDbDatabaseName'
          value: 'BussinDb'
        }
        {
          name: 'CosmosDbContainerName'
          value: 'Logins'
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

// --- Storage Managed Identity Role Assignments for Function App ---
// Grants Blob, Queue, and Table permissions to configure 100% passwordless storage access
var storageRoles = [
  'b24988ac-6180-42a0-ab88-20f7382dd24c' // Storage Blob Data Owner
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
  '0a9a22dd-7a55-4345-90a4-0d5d374b8de7' // Storage Table Data Contributor
]

resource storageRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for roleId in storageRoles: {
  name: guid(storageAccount.id, functionAppName, roleId)
  scope: storageAccount
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
    principalType: 'ServicePrincipal'
  }
}]

// --- Cosmos DB SQL Role Assignment for Function App Managed Identity ---
resource sqlRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-09-15' = {
  parent: cosmosDbAccount
  name: guid(functionAppName, '00000000-0000-0000-0000-000000000002', cosmosDbAccount.id)
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: resourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', cosmosDbAccount.name, '00000000-0000-0000-0000-000000000002')
    scope: cosmosDbAccount.id
  }
}

output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output cosmosDbAccountName string = cosmosDbAccount.name
