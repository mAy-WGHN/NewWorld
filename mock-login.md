# Mock-Login für die lokale Entwicklung

Nur für die **lokale Landschaft** (`dotnet run --project src/AppHost`, Frontend `npm start` in `Frontend/`). Business Central wird dort durch WireMock ersetzt (`tests/Fixtures/Bc/e2e-landscape`); die Personen, Personalnummern und die PIN stammen aus diesen Dateien. Das sind Testwerte, **keine echten Zugänge** und nirgends sonst gültig.

Anmeldung im Frontend (`http://localhost:4200`): **Personalnummer + PIN**. Die PIN ist für alle Testpersonen dieselbe: **`4711`**.

## Personen und Rollen

| Rolle (Zweck) | Person | Personalnummer | PIN | Was sie sieht |
| --- | --- | --- | --- | --- |
| **Administrator** | Anna Admin | `10099` | `4711` | alles (Recht `*`); beim Start von Access als erster Administrator eingetragen (`Access__BootstrapAdministrators__0`) |
| **HR** (Personalabteilung) | Clara Drossel | `10045` | `4711` | Abwesenheiten, AU-Prüfung, Mitarbeitende, Arbeitszeit, Nicht-Verfügbarkeit, Benachrichtigungstexte, FAQ, Meldungen, Statistik. Zuständig für die Urlaubsanträge von Erika. **Nicht automatisch vorhanden**, siehe unten |
| **Mitarbeiterin** (Public User) | Erika Mustermann | `10042` | `4711` | die eigenen Bereiche: Krankmeldungen, Urlaub, Abwesenheiten, Arbeitszeitkonto, Karte, Mitteilungen |
| Mitarbeiter (Public User) | Ben Kollege | `10044` | `4711` | wie Erika |
| Mitarbeiterin (Public User) | Dora Fehler | `10046` | `4711` | wie Erika |
| Filialleitung (ohne eigene Rolle) | Max Filialleiter | `10043` | `4711` | zunächst wie Erika; eine Rolle für die Filialleitung gibt es nicht vorbereitet |

Alle außer Anna gehören zur Filiale **F017 (Musterstadt)**. Anna hat keine Filiale.

Jede aktive Person bekommt von Access automatisch die Rolle **Public User**. Alles Weitere wird in Access von Hand vergeben (Entscheidung 13 und 22 in `CLAUDE.md`). Darum ist nach jedem Start der Landschaft nur Anna mehr als eine einfache Mitarbeiterin.

## Was nach einem Start eingerichtet werden muss

Die lokale Datenbank ist bei **jedem Start des AppHost leer** (Postgres-Container ohne Volume). Danach fehlen die Rechte der Rolle „Public User“, die Rolle „HR“ und deren Zuweisung.

**Schnell per Skript** (wenn alle Dienste laufen, aus dem Root):

```powershell
pwsh scripts/Setup-MockLogin.ps1
```

Das Skript tut genau das Folgende (Ports als Parameter `-AccessUrl` und `-WorkforceUrl`, falls Aspire andere vergeben hat). **Von Hand** geht es über die Oberfläche, als Anna (`10099`) angemeldet unter **Verwaltung → Rollen und Rechte**:

1. **Rollen → Public User:** die eigenen Rechte vergeben:
   `absence:read-own`, `absence:create-own`, `absence:update-own`, `certificate:upload-own`, `employee:read-own`, `employee:request-change-own`, `notification:read-own`, `notification:update-own`, `preference:read-own`, `preference:update-own`, `workingtime:read-own`.
2. **Rollen → Neue Rolle „HR“** mit den Rechten:
   `absence:*`, `certificate:*`, `employee:*`, `workingtime:*`, `unavailability:*`, `template:manage`, `access:person:read`, `faq:*`, `report:*`, `statistic:read`.
3. **Personen:** Clara suchen (nach Name oder Personalnummer, zum Beispiel „drossel“ oder „10045“) und ihr die Rolle „HR“ zuweisen (uneingeschränkt).
4. **Zuständigkeit** `absence.approval` für Clara, Ziel: Person Erika (dann gehen Urlaubsanträge von Erika an Clara).
5. **Urlaubsanspruch** für Erika eintragen (sonst ist ein Urlaubsantrag nicht möglich): `PUT /api/employees/{erikaId}/vacation-entitlements/{jahr}` mit `{"days": 30}` als Administrator oder HR.

Dieselben Schritte als Aufrufe an die Dienste (PowerShell; Ports siehe unten, `$token` ist der `accessToken` aus der Anmeldung als Anna):

```powershell
$h = @{ Authorization = "Bearer $token" }
$base = "http://localhost:5402"   # Access
Invoke-RestMethod -Method Post "$base/api/auth/login" -ContentType 'application/json' -Body '{"personnelNumber":"10099","pin":"4711"}'
# Public User ändern:   PUT  $base/api/roles/a0000000-0000-0000-0000-000000000002   { "privileges": [ ... ] }
# Rolle HR anlegen:     POST $base/api/roles                                        { "name": "HR", "privileges": [ ... ] }
# Rolle zuweisen:       POST $base/api/people/{personId}/assignments                { "roleId": "...", "unrestricted": true, "scopeIds": [], "includeHomeStore": false }
# Zuständigkeit:        POST $base/api/responsibilities                             { "topic": "absence.approval", "responsibleEmployeeId": "{claraId}", "target": { "kind": "employee", "id": "{erikaId}" } }
```

## Adressen

- Frontend: `http://localhost:4200`. Der Dev-Server leitet `/svc/...` an die Dienste weiter (`Frontend/proxy.conf.json`).
- Aspire vergibt die Ports der Dienste; in dieser Umgebung waren sie zuletzt: Access `5402`, Notifications `5403`, Workforce `5404`, Portal `5405`, BC Integration `5401`. Weichen sie ab, die Ziele in `Frontend/proxy.conf.json` anpassen (oder eine eigene, nicht versionierte Kopie nutzen, z. B. `proxy.local.json`) und das Aspire-Dashboard öffnen.
- Zusätzliche Personen oder andere PINs: eine weitere `pin-<Nummer>.json` und den Eintrag in `employees.json` unter `tests/Fixtures/Bc/e2e-landscape/` anlegen (die Datei wird auch von den E2E-Tests benutzt).

## Hinweise

- Die PIN-Prüfung geht gegen den WireMock-Ersatz, nie gegen das echte Business Central. Gegen echtes BC läuft der Login noch nicht (nur der Sandbox-Test steht bereit).
- Das Frontend speichert das Anmelde-Token nur im Speicher und das Erneuerungs-Token im `sessionStorage` des Tabs; ein neuer Tab verlangt eine neue Anmeldung.
- Nichts davon in `appsettings*.json` oder in Konfiguration für andere Umgebungen übernehmen.
