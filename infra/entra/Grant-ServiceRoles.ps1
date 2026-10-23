<#
.SYNOPSIS
  Assigns the app roles of the NewWorld APIs to the managed identities of the calling services (and grants BC access).
.DESCRIPTION
  NOT executed by the project. For the tenant admin: run with -WhatIf first, then check with Test-NewWorldEntra.ps1.
  Needs Microsoft Graph PowerShell (AppRoleAssignment.ReadWrite.All, Application.Read.All).
  The managed identities are those of the five services (system-assigned; the name is the name of the hosting resource).
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $AccessIdentity,
    [Parameter(Mandatory)] [string] $BcIntegrationIdentity,
    [Parameter(Mandatory)] [string] $NotificationsIdentity,
    [Parameter(Mandatory)] [string] $WorkforceIdentity,
    [Parameter(Mandatory)] [string] $PortalIdentity
)

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.Graph.Applications
Connect-MgGraph -Scopes 'AppRoleAssignment.ReadWrite.All', 'Application.Read.All' -NoWelcome

# caller (managed identity) -> API app -> role. Each line is justified in docs/entra-setup.md.
$assignments = @(
    @{ Caller = $AccessIdentity;        Api = 'NewWorld BC Integration API'; Role = 'BcIntegration.Pin.Verify' }
    @{ Caller = $WorkforceIdentity;     Api = 'NewWorld Access API';         Role = 'Access.Catalog.Register' }
    @{ Caller = $NotificationsIdentity; Api = 'NewWorld Access API';         Role = 'Access.Catalog.Register' }
    @{ Caller = $WorkforceIdentity;     Api = 'NewWorld Access API';         Role = 'Access.Permissions.Read' }
    @{ Caller = $NotificationsIdentity; Api = 'NewWorld Access API';         Role = 'Access.Permissions.Read' }
    @{ Caller = $PortalIdentity;        Api = 'NewWorld Access API';         Role = 'Access.Permissions.Read' }
)
# Not assigned yet: Workforce.AbsenceDetails.Read (first caller will be the HR API), BcIntegration.EmployeeErasure.Request (operators).

foreach ($a in $assignments) {
    $caller = Get-MgServicePrincipal -Filter "displayName eq '$($a.Caller)'" | Select-Object -First 1
    $api = Get-MgServicePrincipal -Filter "displayName eq '$($a.Api)'" | Select-Object -First 1
    if (-not $caller -or -not $api) { throw "Not found: $($a.Caller) or $($a.Api) (run New-NewWorldApps.ps1 first)." }
    $role = $api.AppRoles | Where-Object Value -eq $a.Role
    $existing = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $caller.Id | Where-Object { $_.AppRoleId -eq $role.Id -and $_.ResourceId -eq $api.Id }
    if ($existing) { continue }
    if ($PSCmdlet.ShouldProcess("$($a.Caller) -> $($a.Api)", "Assign role $($a.Role)")) {
        New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $caller.Id -PrincipalId $caller.Id -ResourceId $api.Id -AppRoleId $role.Id | Out-Null
    }
}

# Business Central: BC Integration needs API.ReadWrite.All of "Dynamics 365 Business Central" (admin consent).
$bcAppId = '996def3d-b36c-4153-8607-a6fd3c01b89f'
$bc = Get-MgServicePrincipal -Filter "appId eq '$bcAppId'" | Select-Object -First 1
$bcCaller = Get-MgServicePrincipal -Filter "displayName eq '$BcIntegrationIdentity'" | Select-Object -First 1
$bcRole = $bc.AppRoles | Where-Object Value -eq 'API.ReadWrite.All'
$bcExisting = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $bcCaller.Id | Where-Object { $_.AppRoleId -eq $bcRole.Id -and $_.ResourceId -eq $bc.Id }
if (-not $bcExisting -and $PSCmdlet.ShouldProcess($BcIntegrationIdentity, 'Grant Business Central API.ReadWrite.All')) {
    New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $bcCaller.Id -PrincipalId $bcCaller.Id -ResourceId $bc.Id -AppRoleId $bcRole.Id | Out-Null
}
# Afterwards register the identity in Business Central (page "Microsoft Entra Applications") with the permission sets for the API pages.
