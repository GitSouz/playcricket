// Scheduled Container Apps Job running the monthly fixture report pipeline.
// Deploy (after creating the ACR image and Key Vault secrets):
//   az deployment group create -g <resource-group> -f infra/containerapp-job.bicep \
//     -p acrName=<acr> environmentId=<managed-environment-resource-id> \
//        keyVaultName=<vault> sqlConnectionString='...'
//
// Secrets are wired as Container Apps secrets here for simplicity; move them
// to Key Vault references (secretRef with keyVaultUrl + identity) once the
// vault is provisioned.

param location string = resourceGroup().location
param jobName string = 'playcricket-fixture-reports'
param acrName string
param environmentId string
param image string = '${acrName}.azurecr.io/playcricket-fixture-reports:latest'

@description('Cron: 06:00 UTC on the 1st of every month (reports on the previous month).')
param cronExpression string = '0 6 1 * *'

@secure()
param sqlConnectionString string
@secure()
param archiveStorageConnection string
@secure()
param dotdigitalApiUser string
@secure()
param dotdigitalApiPassword string
param dotdigitalParentFolderId string = ''

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: jobName
  location: location
  identity: {
    type: 'SystemAssigned' // grant this identity AcrPull + Azure SQL access
  }
  properties: {
    environmentId: environmentId
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: {
        cronExpression: cronExpression
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: 7200 // 2h ceiling for a full render + upload run
      replicaRetryLimit: 1
      registries: [
        {
          server: '${acrName}.azurecr.io'
          identity: 'system'
        }
      ]
      secrets: [
        { name: 'sql-connection', value: sqlConnectionString }
        { name: 'archive-storage-connection', value: archiveStorageConnection }
        { name: 'dotdigital-api-user', value: dotdigitalApiUser }
        { name: 'dotdigital-api-password', value: dotdigitalApiPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'fixture-reports'
          image: image
          args: ['--upload']
          resources: {
            cpu: json('2')
            memory: '4Gi'
          }
          env: [
            { name: 'PLAYCRICKET_SQL_CONNECTION', secretRef: 'sql-connection' }
            { name: 'ARCHIVE_STORAGE_CONNECTION', secretRef: 'archive-storage-connection' }
            { name: 'DOTDIGITAL_API_USER', secretRef: 'dotdigital-api-user' }
            { name: 'DOTDIGITAL_API_PASSWORD', secretRef: 'dotdigital-api-password' }
            { name: 'DOTDIGITAL_PARENT_FOLDER_ID', value: dotdigitalParentFolderId }
            { name: 'ARCHIVE_SHARE', value: 'development' }
            { name: 'ARCHIVE_BASE_PATH', value: 'PlayCricket' }
          ]
        }
      ]
    }
  }
}

output jobPrincipalId string = job.identity.principalId
