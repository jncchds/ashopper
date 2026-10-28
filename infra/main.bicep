// ashopper on Azure, sized for free tiers:
//   - Container Apps (Consumption): monthly free grant, everything scales to zero
//   - Azure SQL Database free offer: serverless, auto-pauses, 100k vCore-seconds + 32 GB/month free
//   - Log Analytics with a daily cap, so logs can't run up a bill
//
// Deploy: az deployment group create -g <rg> -f infra/main.bicep -p infra/main.parameters.json -p sqlAdminPassword=... jwtSigningKey=...

targetScope = 'resourceGroup'

@description('Short prefix for resource names (lowercase letters/digits).')
@maxLength(12)
param prefix string = 'ashopper'

param location string = resourceGroup().location

@description('Container images, e.g. ghcr.io/<owner>/ashopper-api:<sha>.')
param apiImage string
param uiImage string
param collectorImage string

@description('GHCR credentials. Leave empty if the packages are public.')
param registryUsername string = ''
@secure()
param registryPassword string = ''

param sqlAdminLogin string = 'ashopperadmin'
@secure()
param sqlAdminPassword string

@description('Base64 of at least 32 random bytes: `openssl rand -base64 48`.')
@secure()
param jwtSigningKey string
param jwtKeyId string = 'k1'

@description('Collector schedule (UTC cron). Every 6 hours by default.')
param collectorCron string = '0 */6 * * *'

var apiName = '${prefix}-api'
var uiName = '${prefix}-ui'
var registries = empty(registryUsername) ? [] : [
  { server: 'ghcr.io', username: registryUsername, passwordSecretRef: 'registry-password' }
]
var registrySecrets = empty(registryUsername) ? [] : [
  { name: 'registry-password', value: registryPassword }
]

// ---------- Observability
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.15') } // ~5 GB/month free ingestion
  }
}

// ---------- Database (free offer)
resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql-${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Lets Azure services (Container Apps) connect. Tighten with VNet integration later (not free).
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'ashopper'
  location: location
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose' }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause' // never bill: pause until next month instead
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB
  }
}

var sqlConnection = 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${db.name};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;Connection Timeout=60'

// ---------- Container Apps
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var uiUrl = 'https://${uiName}.${env.properties.defaultDomain}'

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiName
  location: location
  properties: {
    environmentId: env.id
    configuration: {
      // Internal only: reachable from the UI's nginx inside the environment, not from the internet.
      ingress: { external: false, targetPort: 8080, transport: 'http' }
      registries: registries
      secrets: concat(registrySecrets, [
        { name: 'sql', value: sqlConnection }
        { name: 'jwt-key', value: jwtSigningKey }
      ])
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ConnectionStrings__Sql', secretRef: 'sql' }
            { name: 'Auth__PublicUrl', value: uiUrl }
            { name: 'Auth__Jwt__ActiveKeyId', value: jwtKeyId }
            { name: 'Auth__Jwt__SigningKeys__0__Id', value: jwtKeyId }
            { name: 'Auth__Jwt__SigningKeys__0__Key', secretRef: 'jwt-key' }
          ]
          // Liveness only: a DB-touching probe would keep the free database from auto-pausing.
          probes: [
            { type: 'Liveness', httpGet: { path: '/api/health', port: 8080 }, periodSeconds: 30 }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
        rules: [ { name: 'http', http: { metadata: { concurrentRequests: '50' } } } ]
      }
    }
  }
}

resource ui 'Microsoft.App/containerApps@2024-03-01' = {
  name: uiName
  location: location
  properties: {
    environmentId: env.id
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      registries: registries
      secrets: registrySecrets
    }
    template: {
      containers: [
        {
          name: 'ui'
          image: uiImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [ { name: 'API_UPSTREAM', value: 'http://${apiName}' } ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 30 }
          ]
        }
      ]
      scale: {
        minReplicas: 0 // set to 1 to avoid cold starts (costs a little beyond the free grant)
        maxReplicas: 2
        rules: [ { name: 'http', http: { metadata: { concurrentRequests: '100' } } } ]
      }
    }
  }
}

var jobEnv = [ { name: 'ConnectionStrings__Sql', secretRef: 'sql' } ]
var jobSecrets = concat(registrySecrets, [ { name: 'sql', value: sqlConnection } ])

resource collector 'Microsoft.App/jobs@2024-03-01' = {
  name: '${prefix}-collector'
  location: location
  properties: {
    environmentId: env.id
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: { cronExpression: collectorCron, parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 1800
      replicaRetryLimit: 1
      registries: registries
      secrets: jobSecrets
    }
    template: {
      containers: [
        {
          name: 'collector'
          image: collectorImage
          args: [ 'collect' ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: jobEnv
        }
      ]
    }
  }
}

// Started by the deploy workflow before rolling out a new API revision.
resource migrate 'Microsoft.App/jobs@2024-03-01' = {
  name: '${prefix}-migrate'
  location: location
  properties: {
    environmentId: env.id
    configuration: {
      triggerType: 'Manual'
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 600
      replicaRetryLimit: 0
      registries: registries
      secrets: jobSecrets
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: collectorImage
          args: [ 'migrate' ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: jobEnv
        }
      ]
    }
  }
}

output uiUrl string = uiUrl
output apiAppName string = api.name
output uiAppName string = ui.name
output collectorJobName string = collector.name
output migrateJobName string = migrate.name
