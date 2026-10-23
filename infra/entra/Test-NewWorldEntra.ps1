<#
.SYNOPSIS
  Read-only check of the Entra setup: apps, app roles and role assignments exist as documented in docs/entra-setup.md.
.DESCRIPTION
  NOT executed by the project. Changes nothing. Exit code 1 if something is missing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AccessIdentity,
    [Parameter(Mandatory)] [string] $WorkforceIdentity,
    [Parameter(Mandatory)] [string] $NotificationsIdentity,
    [Parameter(Mandatory)] [string] $PortalIdentity
)

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.Graph.Applications
Connect-MgGraph -Scopes 'Application.Read.All' -NoWelcome

$expected = @{
    'NewWorld Access API'         = @('Access.Catalog.Register', 'Access.Permissions.Read')
    'NewWorld BC Integration API' = @('BcIntegration.Pin.Verify', 'BcIntegration.EmployeeErasure.Request')
    'NewWorld Workforce API'      = @('Workforce.AbsenceDetails.Read')
}
$problems = @()
foreach ($name in $expected.Keys) {
    $sp = Get-MgServicePrincipal -Filter "displayName eq '$name'" | Select-Object -First 1
    if (-not $sp) { $problems += "Missing app: $name"; continue }
    foreach ($role in $expected[$name]) {
        if (-not ($sp.AppRoles | Where-Object Value -eq $role)) { $problems += "Missing role $role in $name" }
    }
}

$calls = @(
    @{ Caller = $AccessIdentity;        Api = 'NewWorld BC Integration API'; Role = 'BcIntegration.Pin.Verify' }
    @{ Caller = $WorkforceIdentity;     Api = 'NewWorld Access API';         Role = 'Access.Catalog.Register' }
    @{ Caller = $NotificationsIdentity; Api = 'NewWorld Access API';         Role = 'Access.Catalog.Register' }
    @{ Caller = $WorkforceIdentity;     Api = 'NewWorld Access API';         Role = 'Access.Permissions.Read' }
    @{ Caller = $NotificationsIdentity; Api = 'NewWorld Access API';         Role = 'Access.Permissions.Read' }
    @{ Caller = $PortalIdentity;        Api = 'NewWorld Access API';         Role = 'Access.Permissions.Read' }
)
foreach ($c in $calls) {
    $caller = Get-MgServicePrincipal -Filter "displayName eq '$($c.Caller)'" | Select-Object -First 1
    $api = Get-MgServicePrincipal -Filter "displayName eq '$($c.Api)'" | Select-Object -First 1
    if (-not $caller -or -not $api) { $problems += "Missing identity or app for $($c.Caller) -> $($c.Api)"; continue }
    $role = $api.AppRoles | Where-Object Value -eq $c.Role
    $has = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $caller.Id | Where-Object { $_.AppRoleId -eq $role.Id -and $_.ResourceId -eq $api.Id }
    if (-not $has) { $problems += "Missing assignment: $($c.Caller) -> $($c.Api) / $($c.Role)" }
}

if ($problems) { $problems | ForEach-Object { Write-Error $_ -ErrorAction Continue }; exit 1 }
Write-Output 'Entra setup matches docs/entra-setup.md.'
