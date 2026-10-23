<#
.SYNOPSIS
  Creates the app registrations of the NewWorld APIs with their app roles (service-to-service only).
.DESCRIPTION
  NOT executed by the project. For the tenant admin: read the script, run it with -WhatIf first.
  Needs the Microsoft Graph PowerShell module (Application.ReadWrite.All). Idempotent: existing apps are only completed.
  Users do not get app roles: people sign in through Access (own token, decision 13); Entra is only used between services.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.Graph.Applications
Connect-MgGraph -Scopes 'Application.ReadWrite.All' -NoWelcome

# Stable role ids (uuid v5), so that every run and every environment uses the same ids.
$roles = @{
    'Access.Catalog.Register' = @{ Id = 'ec52196c-e474-51cf-b8a3-652f54af3583'; Description = 'Registers the privilege catalogue of an API with Access (at start).' }
    'Access.Permissions.Read' = @{ Id = 'ff08a310-dc53-591b-81f7-1cdf9d470a45'; Description = 'Rebuilds the permission/responsibility copy of an API (after an outage or a new installation).' }
    'BcIntegration.Pin.Verify' = @{ Id = '94aa150b-a47a-5751-a2b2-d6713ae75437'; Description = 'Login: checks personnel number and PIN against Business Central (called by Access only).' }
    'BcIntegration.EmployeeErasure.Request' = @{ Id = 'a86431f5-97a9-5b81-b301-a862fece4184'; Description = 'Releases the erasure of an employee in all services (data protection, triggered by an operator).' }
    'Workforce.AbsenceDetails.Read' = @{ Id = '0ae66bc4-9da5-5bed-af40-b424f9fc487f'; Description = 'Reads the details of an absence including the type (for the later HR API).' }
}

# App registration -> its roles. One app per API; a calling service gets a role assigned (Grant-ServiceRoles.ps1).
$apps = @{
    'NewWorld Access API' = @('Access.Catalog.Register', 'Access.Permissions.Read')
    'NewWorld BC Integration API' = @('BcIntegration.Pin.Verify', 'BcIntegration.EmployeeErasure.Request')
    'NewWorld Workforce API' = @('Workforce.AbsenceDetails.Read')
}

foreach ($name in $apps.Keys) {
    $app = Get-MgApplication -Filter "displayName eq '$name'" | Select-Object -First 1
    $appRoles = @($apps[$name] | ForEach-Object {
        @{
            Id                 = $roles[$_].Id
            Value              = $_
            DisplayName        = $_
            Description        = $roles[$_].Description
            AllowedMemberTypes = @('Application')
            IsEnabled          = $true
        }
    })
    if (-not $app) {
        if ($PSCmdlet.ShouldProcess($name, 'Create app registration with app roles')) {
            $app = New-MgApplication -DisplayName $name -SignInAudience 'AzureADMyOrg' -AppRoles $appRoles
            New-MgServicePrincipal -AppId $app.AppId | Out-Null
            Update-MgApplication -ApplicationId $app.Id -IdentifierUris @("api://$($app.AppId)")
        }
    }
    elseif ($PSCmdlet.ShouldProcess($name, 'Complete app roles')) {
        Update-MgApplication -ApplicationId $app.Id -AppRoles $appRoles
    }
}

