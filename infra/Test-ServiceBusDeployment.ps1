<#
.SYNOPSIS
  Prüft infra/servicebus.bicep gegen eine echte Azure-Umgebung, in einer Wegwerf-Resource-Group.

.DESCRIPTION
  1. legt eine Resource-Group und fünf benutzerzugewiesene Managed Identities an (stellvertretend für die Dienste),
  2. führt `what-if` aus und deployt das Bicep,
  3. gleicht Topics, Subscriptions und Queues mit infra/servicebus-emulator.json ab (dieselbe Topologie wie lokal),
  4. prüft, dass keine Shared Access Keys erlaubt sind (disableLocalAuth) und dass es nur Rollenzuweisungen für Sender und Empfänger gibt,
  5. löscht die Resource-Group wieder (außer mit -KeepResources).

  Was das Skript NICHT prüft: den Nachrichtenfluss selbst (Senden/Empfangen mit den Diensten) — das geht erst mit laufenden Diensten.
  Voraussetzungen: Azure CLI (`az login`), Berechtigung zum Anlegen von Rollenzuweisungen (Owner oder User Access Administrator)
  auf der Subscription, Bicep (`az bicep install`). Es entstehen Kosten für einen Standard-Namespace für die Dauer des Tests (Minuten).

.EXAMPLE
  ./infra/Test-ServiceBusDeployment.ps1 -SubscriptionId <id> -Location westeurope
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    [string] $Location = 'westeurope',
    [string] $Suffix = (Get-Date -Format 'MMddHHmm'),
    [switch] $KeepResources
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bicep = Join-Path $root 'infra/servicebus.bicep'
$emulator = Join-Path $root 'infra/servicebus-emulator.json'
$group = "rg-newworld-sbtest-$Suffix"
$namespace = "sb-newworld-test-$Suffix"
$services = @('bcintegration', 'access', 'notifications', 'workforce', 'portal')
$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-Az {
    # az gives its result as text; a non-zero exit code is an error
    $output = & az @args --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed ($LASTEXITCODE)" }
    return $output
}

az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) { throw 'az account set failed (az login?)' }

try {
    Write-Host "== Resource-Group $group in $Location"
    Invoke-Az group create --name $group --location $Location --output none

    Write-Host '== Managed Identities (stellvertretend für die Dienste)'
    $principals = @{}
    foreach ($service in $services) {
        $identity = Invoke-Az identity create --resource-group $group --name "id-$service-$Suffix" --output json | ConvertFrom-Json
        $principals[$service] = $identity.principalId
    }

    $parameters = @(
        "namespaceName=$namespace",
        "location=$Location",
        "bcIntegrationPrincipalId=$($principals['bcintegration'])",
        "accessPrincipalId=$($principals['access'])",
        "notificationsPrincipalId=$($principals['notifications'])",
        "workforcePrincipalId=$($principals['workforce'])",
        "portalPrincipalId=$($principals['portal'])"
    )

    Write-Host '== what-if'
    Invoke-Az deployment group what-if --resource-group $group --template-file $bicep --parameters @parameters --no-pretty-print --output none

    Write-Host '== Deployment'
    Invoke-Az deployment group create --resource-group $group --template-file $bicep --parameters @parameters --output none

    Write-Host '== Topologie gegen den Emulator abgleichen'
    $expected = (Get-Content $emulator -Raw | ConvertFrom-Json).UserConfig.Namespaces[0]
    $expectedTopics = @($expected.Topics | ForEach-Object { $_.Name } | Sort-Object)
    $expectedQueues = @($expected.Queues | ForEach-Object { $_.Name } | Sort-Object)
    $actualTopics = @(Invoke-Az servicebus topic list --resource-group $group --namespace-name $namespace --query '[].name' --output json | ConvertFrom-Json | Sort-Object)
    $actualQueues = @(Invoke-Az servicebus queue list --resource-group $group --namespace-name $namespace --query '[].name' --output json | ConvertFrom-Json | Sort-Object)

    foreach ($diff in Compare-Object $expectedTopics $actualTopics) { $failures.Add("Topic $($diff.InputObject): $($diff.SideIndicator -eq '<=' ? 'nur im Emulator' : 'nur in Azure')") }
    foreach ($diff in Compare-Object $expectedQueues $actualQueues) { $failures.Add("Queue $($diff.InputObject): $($diff.SideIndicator -eq '<=' ? 'nur im Emulator' : 'nur in Azure')") }

    foreach ($topic in $expected.Topics) {
        $expectedSubscriptions = @($topic.Subscriptions | ForEach-Object { $_.Name } | Sort-Object)
        $actualSubscriptions = @(Invoke-Az servicebus topic subscription list --resource-group $group --namespace-name $namespace --topic-name $topic.Name --query '[].name' --output json | ConvertFrom-Json | Sort-Object)
        foreach ($diff in Compare-Object $expectedSubscriptions $actualSubscriptions) {
            $failures.Add("Subscription $($topic.Name)/$($diff.InputObject): $($diff.SideIndicator -eq '<=' ? 'nur im Emulator' : 'nur in Azure')")
        }
    }

    Write-Host '== Sicherheit: keine Shared Access Keys, Rollen nur Sender/Empfänger'
    $disableLocalAuth = Invoke-Az servicebus namespace show --resource-group $group --name $namespace --query 'disableLocalAuth' --output tsv
    if ($disableLocalAuth -ne 'true') { $failures.Add('disableLocalAuth ist nicht true: Shared Access Keys wären erlaubt') }

    $namespaceId = Invoke-Az servicebus namespace show --resource-group $group --name $namespace --query 'id' --output tsv
    $allowedRoles = @('Azure Service Bus Data Sender', 'Azure Service Bus Data Receiver')
    $roles = @(Invoke-Az role assignment list --scope $namespaceId --query '[].roleDefinitionName' --output json | ConvertFrom-Json) | Sort-Object -Unique
    foreach ($role in $roles | Where-Object { $_ -notin $allowedRoles }) { $failures.Add("Unerwartete Rolle auf dem Namespace: $role") }
}
finally {
    if ($KeepResources) {
        Write-Host "== Resource-Group $group bleibt bestehen (-KeepResources). Löschen: az group delete --name $group --yes"
    }
    else {
        Write-Host "== Resource-Group $group wird gelöscht"
        az group delete --name $group --yes --no-wait --only-show-errors | Out-Null
    }
}

if ($failures.Count -gt 0) {
    Write-Host '== FEHLER' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host '== Alles in Ordnung: Bicep deployt, Topologie stimmt mit dem Emulator überein, keine Shared Access Keys.' -ForegroundColor Green
