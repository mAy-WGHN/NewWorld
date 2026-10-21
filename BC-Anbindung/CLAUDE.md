# BC-Anbindung

ASP.NET-Web-API auf .NET 10 (`net10.0`): der **einzige Weg zu Business Central (BC)** für das Unternehmensportal. Die anderen Programme
(Rollen & Rechte, Portal, HR-Tool) sprechen mit diesem Dienst statt mit BC; nur hier liegen die BC-Zugangsdaten, hier kennt man die
Codeunits und ihre Eigenheiten. Projektdatei: `BC-Anbindung.csproj`, Solution: `BC-Anbindung.slnx`. **Keine Datenbank.**

Teil von vier Bausteinen (Repos unter `source/repos`): **Portal**, **Rollen-Rechte** (Login, Rollen, Rechte, Scopes, Filialen),
**BC-Anbindung** (dieser Dienst), HR-Tool (`hr-tool-API`). Das Konzept steht in `Portal/.claude/references/PLATTFORM-SCHNITTSTELLEN.md`.

## Ordnerstruktur

```
BC-Anbindung/
├── Authentication/     API-Schlüssel-Prüfung (X-Api-Key, je Aufrufer ein Schlüssel) und GatewayOptions
├── BusinessCentral/    Token (Azure AD), SOAP-Codeunit-Client, LSC-Antwortformat, BcGateway (die fachlichen Abfragen)
├── Common/Api/         Response-Envelope, ApiException, Fehler-Handler, 422-Validierung
├── Contracts/          DTOs der Schnittstelle
├── Controllers/        Employees (verify), Stores, Ping
├── Serialization/      JSON-Optionen (camelCase, ISO-8601)
└── BC-Anbindung.Tests/ xUnit: Functional/ (HTTP gegen den Dienst mit Fake-BC), Infrastructure/ — kein Docker nötig
```

## Schnittstelle (alle außer `/health` und `/api/ping` brauchen den Header `X-Api-Key`)

- `POST /api/employees/verify` `{employeeNo, pin}` → `{"data":{"authenticated":bool,"employeeName":string|null},"success":true}`.
  `authenticated=false` heißt: BC kennt Personalnummer/PIN nicht (HTTP 200). **503**, wenn BC oder Azure AD nicht erreichbar ist oder die Antwort
  unbrauchbar ist — so kann der Aufrufer „BC sagt nein“ von „BC ist weg“ unterscheiden (Rollen-Rechte fällt dann auf den lokalen Stand zurück).
  Ein leeres PIN wird nie an BC gegeben (BC würde es als gültig werten).
- `GET /api/stores` → `{"data":[{"storeNo","name"}],"success":true}`, nach Store-Nr. sortiert, `Gateway__StoresCacheSeconds` (Default 300) zwischengespeichert.
- Das Format ist ein **Vertrag**: Dieselben Rohtexte stehen in `GatewayTests` (hier) und `HttpBcGatewayTests` (Rollen-Rechte). Änderungen nur abwärtskompatibel.

## Betrieb / Konfiguration

- Lokale Konfiguration über `.env` (Vorlage: `.env.example`), wird beim Start geladen; echte Umgebungsvariablen haben Vorrang.
- `BusinessCentral__ClientId/ClientSecret/Scope/TenantId/Environment/CompanyName` (Azure-AD-App). Fehlt etwas, antworten die Endpunkte mit 503
  „nicht konfiguriert“.
- `Gateway__ApiKeys__<Aufrufer>`: je Aufrufer (Identity, Portal, Hr) ein eigener Schlüssel, mind. 32 zufällige Zeichen, vergleich zeitkonstant. Ohne Schlüssel
  lehnt der Dienst alles mit 401 ab (Warnung beim Start). **Nie öffentlich erreichbar machen**: Der Dienst hält mächtige BC-Zugangsdaten.
- Das PIN steht nie im Log und nie in einer Fehlermeldung; geloggt wird nur der Fehlertyp. Neue BC-Aufrufe müssen das beibehalten (SOAP-Body kann das PIN enthalten).
- Token-Retry nur für den sporadischen Azure-AD-Fehler `AADSTS7000215` (2/5/10 s), alles andere schlägt sofort fehl.

## Konventionen

- Root-Namespace `BC_Anbindung`. Antworten im Envelope `{success, data}` bzw. `{success, message, errors[]}`. Validierungsfehler 422 mit deutschen Meldungen,
  Texte deutsch.
- Neue BC-Funktion: Methode in `BcGateway` (fachliche Antwort statt SOAP), Endpunkt im Controller, Test mit `FakeBcClient`, Rohformat in den Vertragstest.
- Vor dem Verwenden einer neuen Codeunit das tatsächliche Antwortformat live prüfen (siehe `LscCodeunitResponse`).

## Tests

- `make test` bzw. `dotnet test BC-Anbindung.Tests` (kein Docker). `FakeBcClient` ersetzt BC (Mitarbeiter, Filialen, „nicht erreichbar“).

## Build / Deployment

- `make build`, `make run` (Port 5301), `make test`, `make publish` (`app.zip`), `make deploy` (`az webapp deploy`; `RESOURCE_GROUP`/`APP_NAME` im Makefile prüfen).
- Für Updates ohne Ausfall: Deployment-Slot mit Tausch; `/health` ist der Health-Check.
