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

// --- App Service Plan (Consumption Plan / Serverless Linux) ---
resource hostingPlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: 'bussin-plan-${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
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

// --- Function App ---
resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0' // Self-contained AOT runs natively under this Isolated Host
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower(functionAppName)
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
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
    httpsOnly: true
  }
}

// --- Cosmos DB SQL Role Assignment for Function App Managed Identity ---
resource sqlRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-09-15' = {
  parent: cosmosDbAccount
  name: guid(functionApp.id, '00000000-0000-0000-0000-000000000002', cosmosDbAccount.id)
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions', cosmosDbAccount.name, '00000000-0000-0000-0000-000000000002')
    scope: cosmosDbAccount.id
  }
}

output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output cosmosDbAccountName string = cosmosDbAccount.name
