// Service Bus topology of the NewWorld landscape (decision 8: Azure.Messaging.ServiceBus, no shared access keys).
// The local emulator uses the same topology: infra/servicebus-emulator.json (a test compares both and the message catalogue).
// Access: managed identities only. Senders may send to their own topics/queues, receivers read only their own subscriptions.
// Dead-letter queues belong to the subscription/queue and are therefore reachable only by the same receiver identity
// (same restriction as the business data).
// Generated structure: topics -> subscriptions (with filters and purpose) -> role assignments.

@description('Name of the Service Bus namespace.')
param namespaceName string

@description('Location of the namespace.')
param location string = resourceGroup().location

@description('Principal ID of the managed identity of BC Integration.')
param bcIntegrationPrincipalId string

@description('Principal ID of the managed identity of Access.')
param accessPrincipalId string

@description('Principal ID of the managed identity of Notifications.')
param notificationsPrincipalId string

@description('Principal ID of the managed identity of Workforce.')
param workforcePrincipalId string

@description('Principal ID of the managed identity of Portal.')
param portalPrincipalId string

var senderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39' // Azure Service Bus Data Sender
var receiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0' // Azure Service Bus Data Receiver

resource senderRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  scope: subscription()
  name: senderRoleId
}

resource receiverRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  scope: subscription()
  name: receiverRoleId
}

resource sbNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    disableLocalAuth: true // no shared access keys
    minimumTlsVersion: '1.2'
  }
}

// Topic employee-changed (event). Publisher: BC Integration.
resource topic_employee_changed 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'employee-changed'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_employee_changed 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_employee_changed
  name: guid(topic_employee_changed.id, bcIntegrationPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-changed/workforce. Purpose: Copy of employees (name, home store, exit date) for absences.
resource sub_employee_changed_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_changed
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_changed_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_changed_workforce
  name: guid(sub_employee_changed_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-changed/portal. Purpose: Copy of employees for the Portal API (event distribution to a third consumer).
resource sub_employee_changed_portal 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_changed
  name: 'portal'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_changed_portal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_changed_portal
  name: guid(sub_employee_changed_portal.id, portalPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: portalPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-changed/access. Purpose: Copy of employees for derived permissions and the login.
resource sub_employee_changed_access 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_changed
  name: 'access'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_changed_access 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_changed_access
  name: guid(sub_employee_changed_access.id, accessPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic employee-hired (event). Publisher: BC Integration.
resource topic_employee_hired 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'employee-hired'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_employee_hired 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_employee_hired
  name: guid(topic_employee_hired.id, bcIntegrationPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-hired/access. Purpose: Basic role "Public User" for new employees.
resource sub_employee_hired_access 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_hired
  name: 'access'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_hired_access 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_hired_access
  name: guid(sub_employee_hired_access.id, accessPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic employee-left (event). Publisher: BC Integration.
resource topic_employee_left 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'employee-left'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_employee_left 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_employee_left
  name: guid(topic_employee_left.id, bcIntegrationPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-left/workforce. Purpose: Ends open sick notes and cancels later absences at the exit date.
resource sub_employee_left_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_left
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_left_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_left_workforce
  name: guid(sub_employee_left_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-left/access. Purpose: Withdraws permissions at the exit date.
resource sub_employee_left_access 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_left
  name: 'access'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_left_access 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_left_access
  name: guid(sub_employee_left_access.id, accessPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic employee-data-erasure-requested (event). Publisher: BC Integration.
resource topic_employee_data_erasure_requested 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'employee-data-erasure-requested'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_employee_data_erasure_requested 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_employee_data_erasure_requested
  name: guid(topic_employee_data_erasure_requested.id, bcIntegrationPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-data-erasure-requested/workforce. Purpose: Erasure of personal data (data protection).
resource sub_employee_data_erasure_requested_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_data_erasure_requested
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_data_erasure_requested_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_data_erasure_requested_workforce
  name: guid(sub_employee_data_erasure_requested_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-data-erasure-requested/portal. Purpose: Erasure of personal data (data protection).
resource sub_employee_data_erasure_requested_portal 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_data_erasure_requested
  name: 'portal'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_data_erasure_requested_portal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_data_erasure_requested_portal
  name: guid(sub_employee_data_erasure_requested_portal.id, portalPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: portalPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-data-erasure-requested/access. Purpose: Erasure of personal data (data protection).
resource sub_employee_data_erasure_requested_access 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_data_erasure_requested
  name: 'access'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_data_erasure_requested_access 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_data_erasure_requested_access
  name: guid(sub_employee_data_erasure_requested_access.id, accessPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-data-erasure-requested/notifications. Purpose: Erasure of personal data (data protection).
resource sub_employee_data_erasure_requested_notifications 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_data_erasure_requested
  name: 'notifications'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_data_erasure_requested_notifications 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_data_erasure_requested_notifications
  name: guid(sub_employee_data_erasure_requested_notifications.id, notificationsPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: notificationsPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic store-changed (event). Publisher: BC Integration.
resource topic_store_changed 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'store-changed'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_store_changed 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_store_changed
  name: guid(topic_store_changed.id, bcIntegrationPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription store-changed/workforce. Purpose: Copy of stores for absences.
resource sub_store_changed_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_store_changed
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_store_changed_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_store_changed_workforce
  name: guid(sub_store_changed_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription store-changed/portal. Purpose: Copy of stores for the Portal API.
resource sub_store_changed_portal 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_store_changed
  name: 'portal'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_store_changed_portal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_store_changed_portal
  name: guid(sub_store_changed_portal.id, portalPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: portalPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription store-changed/access. Purpose: Copy of stores for scopes.
resource sub_store_changed_access 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_store_changed
  name: 'access'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_store_changed_access 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_store_changed_access
  name: guid(sub_store_changed_access.id, accessPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic bc-sync-succeeded (event). Publisher: BC Integration.
resource topic_bc_sync_succeeded 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'bc-sync-succeeded'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_bc_sync_succeeded 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_bc_sync_succeeded
  name: guid(topic_bc_sync_succeeded.id, bcIntegrationPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription bc-sync-succeeded/workforce. Purpose: BC sync status of absences; filtered: only answers to requests of Workforce.
resource sub_bc_sync_succeeded_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_bc_sync_succeeded
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource rule_bc_sync_succeeded_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: sub_bc_sync_succeeded_workforce
  name: 'source-service'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'SourceService = \'Workforce\''
    }
  }
}

resource read_bc_sync_succeeded_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_bc_sync_succeeded_workforce
  name: guid(sub_bc_sync_succeeded_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic bc-sync-failed (event). Publisher: BC Integration.
resource topic_bc_sync_failed 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'bc-sync-failed'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_bc_sync_failed 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_bc_sync_failed
  name: guid(topic_bc_sync_failed.id, bcIntegrationPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription bc-sync-failed/workforce. Purpose: BC sync status of absences; filtered: only answers to requests of Workforce.
resource sub_bc_sync_failed_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_bc_sync_failed
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource rule_bc_sync_failed_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2024-01-01' = {
  parent: sub_bc_sync_failed_workforce
  name: 'source-service'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'SourceService = \'Workforce\''
    }
  }
}

resource read_bc_sync_failed_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_bc_sync_failed_workforce
  name: guid(sub_bc_sync_failed_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic permissions-changed (event). Publisher: Access.
resource topic_permissions_changed 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'permissions-changed'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_permissions_changed 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_permissions_changed
  name: guid(topic_permissions_changed.id, accessPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription permissions-changed/workforce. Purpose: Local permission copy; checks without calling Access.
resource sub_permissions_changed_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_permissions_changed
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_permissions_changed_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_permissions_changed_workforce
  name: guid(sub_permissions_changed_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription permissions-changed/portal. Purpose: Local permission copy; checks without calling Access.
resource sub_permissions_changed_portal 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_permissions_changed
  name: 'portal'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_permissions_changed_portal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_permissions_changed_portal
  name: guid(sub_permissions_changed_portal.id, portalPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: portalPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription permissions-changed/notifications. Purpose: Local permission copy for the in-app inbox endpoints.
resource sub_permissions_changed_notifications 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_permissions_changed
  name: 'notifications'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_permissions_changed_notifications 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_permissions_changed_notifications
  name: guid(sub_permissions_changed_notifications.id, notificationsPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: notificationsPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic responsibilities-changed (event). Publisher: Access.
resource topic_responsibilities_changed 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'responsibilities-changed'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_responsibilities_changed 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_responsibilities_changed
  name: guid(topic_responsibilities_changed.id, accessPrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription responsibilities-changed/workforce. Purpose: Copy of who approves absences (topic absence.approval).
resource sub_responsibilities_changed_workforce 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_responsibilities_changed
  name: 'workforce'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_responsibilities_changed_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_responsibilities_changed_workforce
  name: guid(sub_responsibilities_changed_workforce.id, workforcePrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Topic employee-availability-changed (event). Publisher: Workforce.
resource topic_employee_availability_changed 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: sbNamespace
  name: 'employee-availability-changed'
  properties: {
    defaultMessageTimeToLive: 'P1D'
  }
}

resource send_employee_availability_changed 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: topic_employee_availability_changed
  name: guid(topic_employee_availability_changed.id, workforcePrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Subscription employee-availability-changed/portal. Purpose: Prepared: availability for the Portal (no business function yet); the handler only acknowledges.
resource sub_employee_availability_changed_portal 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic_employee_availability_changed
  name: 'portal'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_employee_availability_changed_portal 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sub_employee_availability_changed_portal
  name: guid(sub_employee_availability_changed_portal.id, portalPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: portalPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Queue sync-absence-to-bc (command). Receiver: BC Integration.
resource queue_sync_absence_to_bc 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: sbNamespace
  name: 'sync-absence-to-bc'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_sync_absence_to_bc 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue_sync_absence_to_bc
  name: guid(queue_sync_absence_to_bc.id, bcIntegrationPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: bcIntegrationPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource send_sync_absence_to_bc_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue_sync_absence_to_bc
  name: guid(queue_sync_absence_to_bc.id, workforcePrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Queue send-notification (command). Receiver: Notifications.
resource queue_send_notification 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: sbNamespace
  name: 'send-notification'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P1D'
  }
}

resource read_send_notification 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue_send_notification
  name: guid(queue_send_notification.id, notificationsPrincipalId, receiverRoleId)
  properties: {
    roleDefinitionId: receiverRole.id
    principalId: notificationsPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource send_send_notification_workforce 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: queue_send_notification
  name: guid(queue_send_notification.id, workforcePrincipalId, senderRoleId)
  properties: {
    roleDefinitionId: senderRole.id
    principalId: workforcePrincipalId
    principalType: 'ServicePrincipal'
  }
}
