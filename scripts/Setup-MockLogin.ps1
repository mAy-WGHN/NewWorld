<#
.SYNOPSIS
  Richtet nach dem Start der lokalen Landschaft die Testrollen ein (siehe mock-login.md im Root).

.DESCRIPTION
  Die lokale Datenbank ist nach jedem Start des AppHost leer. Dieses Skript meldet sich als Anna Admin an und:
    - vergibt der Rolle "Public User" die eigenen Rechte,
    - legt die Rolle "HR" an und weist sie Clara Drossel zu,
    - macht Clara zuständig für die Urlaubsanträge von Erika (Thema absence.approval),
    - trägt Erikas Urlaubsanspruch für das laufende Jahr ein.
  Nur für die lokale Entwicklung mit den Testpersonen aus tests/Fixtures/Bc/e2e-landscape (PIN der Testpersonen steht in mock-login.md).

.PARAMETER AccessUrl
  Adresse von Access (Standard: http://localhost:5402). Die Ports vergibt Aspire, notfalls im Dashboard nachsehen.

.PARAMETER WorkforceUrl
  Adresse von Workforce (Standard: http://localhost:5404).

.PARAMETER Pin
  PIN der Testpersonen (Standard: der Wert aus den BC-Mock-Dateien).
#>
[CmdletBinding()]
param(
  [string]$AccessUrl = 'http://localhost:5402',
  [string]$WorkforceUrl = 'http://localhost:5404',
  [string]$Pin = '4711'
)

$ErrorActionPreference = 'Stop'

function Get-Login([string]$personnelNumber) {
  Invoke-RestMethod -Method Post "$AccessUrl/api/auth/login" -ContentType 'application/json' -Body (@{ personnelNumber = $personnelNumber; pin = $Pin } | ConvertTo-Json)
}

function Get-EmployeeId($login) {
  (Invoke-RestMethod "$AccessUrl/api/me/privileges" -Headers @{ Authorization = "Bearer $($login.accessToken)" }).employeeId
}

# Das Anmelden legt die Personen erst an, wenn BC Integration sie geliefert hat; ein paar Versuche abwarten.
function Wait-Login([string]$personnelNumber) {
  for ($i = 0; $i -lt 20; $i++) {
    try { return Get-Login $personnelNumber } catch { Start-Sleep -Seconds 3 }
  }
  throw "Anmeldung von $personnelNumber nicht möglich (laufen alle Dienste? stimmt die PIN?)."
}

$admin = Wait-Login '10099'
$adminHeaders = @{ Authorization = "Bearer $($admin.accessToken)" }
$clara = Wait-Login '10045'
$erika = Wait-Login '10042'
$claraId = Get-EmployeeId $clara
$erikaId = Get-EmployeeId $erika

# 1. Eigene Rechte der Rolle "Public User"
$publicUserId = 'a0000000-0000-0000-0000-000000000002'
$own = @(
  'absence:read-own', 'absence:create-own', 'absence:update-own', 'certificate:upload-own', 'employee:read-own', 'employee:request-change-own',
  'notification:read-own', 'notification:update-own', 'preference:read-own', 'preference:update-own', 'workingtime:read-own'
)
Invoke-RestMethod -Method Put "$AccessUrl/api/roles/$publicUserId" -Headers $adminHeaders -ContentType 'application/json' -Body (@{ privileges = $own } | ConvertTo-Json) | Out-Null
Write-Host 'Public User: eigene Rechte gesetzt.'

# 2. Rolle "HR" (anlegen oder aktualisieren) und Clara zuweisen
$hrPrivileges = @(
  'absence:*', 'certificate:*', 'employee:*', 'workingtime:*', 'unavailability:*', 'template:manage', 'access:person:read',
  'faq:*', 'report:*', 'statistic:read'
)
$roles = Invoke-RestMethod "$AccessUrl/api/roles" -Headers $adminHeaders
$hr = $roles | Where-Object { $_.name -eq 'HR' } | Select-Object -First 1
if ($hr) {
  Invoke-RestMethod -Method Put "$AccessUrl/api/roles/$($hr.id)" -Headers $adminHeaders -ContentType 'application/json' -Body (@{ privileges = $hrPrivileges } | ConvertTo-Json) | Out-Null
  $hrId = $hr.id
} else {
  $hrId = (Invoke-RestMethod -Method Post "$AccessUrl/api/roles" -Headers $adminHeaders -ContentType 'application/json' -Body (@{ name = 'HR'; privileges = $hrPrivileges } | ConvertTo-Json)).id
}
$assigned = Invoke-RestMethod "$AccessUrl/api/people/$claraId/assignments" -Headers $adminHeaders
if (-not ($assigned | Where-Object { $_.roleId -eq $hrId })) {
  Invoke-RestMethod -Method Post "$AccessUrl/api/people/$claraId/assignments" -Headers $adminHeaders -ContentType 'application/json' `
    -Body (@{ roleId = $hrId; unrestricted = $true; scopeIds = @(); includeHomeStore = $false } | ConvertTo-Json) | Out-Null
}
Write-Host 'HR: Rolle angelegt und Clara zugewiesen.'

# 3. Clara entscheidet die Urlaubsanträge von Erika
Invoke-RestMethod -Method Post "$AccessUrl/api/responsibilities" -Headers $adminHeaders -ContentType 'application/json' `
  -Body (@{ topic = 'absence.approval'; responsibleEmployeeId = $claraId; target = @{ kind = 'employee'; id = $erikaId } } | ConvertTo-Json -Depth 3) | Out-Null
Write-Host 'Zuständigkeit absence.approval: Clara für Erika.'

# 4. Urlaubsanspruch (Workforce kennt die Person erst nach der Verteilung der Rechte)
$year = (Get-Date).Year
for ($i = 0; $i -lt 10; $i++) {
  try {
    Invoke-RestMethod -Method Put "$WorkforceUrl/api/employees/$erikaId/vacation-entitlements/$year" -Headers $adminHeaders -ContentType 'application/json' -Body '{"days":30}' | Out-Null
    Write-Host "Urlaubsanspruch $year für Erika: 30 Tage."
    break
  } catch {
    if ($i -eq 9) { throw }
    Start-Sleep -Seconds 3
  }
}

Write-Host 'Fertig. Anmeldung im Frontend mit den Personen aus mock-login.md.'
