# HrTool-API

ASP.NET-Web-API auf .NET 10 (`net10.0`): das **HR-Tool** des Unternehmensportals (Mitarbeiter, Dienstpläne, alles Personalbezogene). Projektdatei: `HrTool-API.csproj`,
Solution: `HrTool-API.slnx`. Datenbank: eigenes PostgreSQL über EF Core (Npgsql, snake_case); Entities und `InitialCreate` stehen (Abschnitt „Datenmodell“), die Anbindung an BC fehlt noch.

Das HR-Tool ist **einer von fünf Bausteinen** (Repos unter `source/repos`, jeweils ASP.NET 10, eigene Datenbank). Es kennt keine Benutzer, Rollen, Filialen und kein Business Central selbst:

| Baustein | Aufgabe | Das HR-Tool nutzt es für |
|---|---|---|
| **Rollen-Rechte** | Anmeldung, Benutzer, Rollen, Rechte, Scopes, Filialen | Token prüfen, Rechte und Reichweite auflösen (`GET /api/me/privileges`), Filialen (`GET /api/apps/hr/locations`), Rechtekatalog anmelden (`PUT /api/apps/hr/catalog`) |
| **BC-Anbindung** | einziger Weg zu Business Central | noch nicht angebunden (bietet bisher nur `employees/verify` und `stores`; HR-Daten aus BC brauchen dort neue Endpunkte) |
| **Benachrichtigungen** | Web Push, Empfänger, Einstellungen | noch nicht angebunden (kennt nur `report.created`; für `schedule.*` braucht es dort zuerst einen Handler) |
| **Portal** | Meldungen, FAQ | zeigt später Dienstpläne aus dem HR-Tool an (`GET /api/schedules` mit dem Token des Users) |

Das Konzept steht in `../Portal/.claude/references/PLATTFORM-SCHNITTSTELLEN.md`; **das Portal ist die Vorlage** für Aufbau und Anbindung (Struktur, Konventionen und Rechtesystem sind von dort übernommen).
Der frühere eigene BC-Login (Notizen in `docs/BC_LOGIN_AUTHENTIFIZIERUNG.md`, aus der Inventur-API) ist **entfallen**: Die Anmeldung läuft über Rollen-Rechte, BC-Zugangsdaten gehören nur BC-Anbindung.

## Ordnerstruktur

Diese Struktur ist verbindlich und wird bei neuem Code eingehalten.

```
hr-tool-API/
├── Authentication/     JWT-Prüfung (Token stammt von Rollen-Rechte), Rechteprüfung gegen Rollen-Rechte (AccessDirectory, PrivilegeHandler),
│                       Rechtekatalog (generiert aus Entities) und dessen Anmeldung bei Rollen-Rechte (CatalogRegistration), Scope
├── Common/
│   ├── Api/            Response-Envelope, ApiException, Fehler-Handler, 422-Validierung, Paging-Helfer
│   └── RateLimiting/   globales Rate-Limit pro Client-IP
├── Workforce/          Bereich Workforce (Namespace `HrTool_API.Workforce`), nach Features:
│   ├── Absences/       Absence, AbsenceType, Controller, Contracts (Nichtverfügbarkeiten)
│   ├── ShiftPlanning/  Schedule, Shift, ShiftPattern, ScheduleStatus, ShiftPatternKind, ScheduleCalculator, Controller, Contracts (Dienstplanung)
│   └── Workplaces/     Workplace, PlanningRole, Controller, Contracts (Arbeitsorte, Planungsrollen)
├── Hr/                 Bereich HR (Namespace `HrTool_API.Hr`)
│   └── Employees/      Employee, EmploymentContract, CashierProfile, Controller, Contracts (Mitarbeiterstamm)
├── Contracts/          bereichsübergreifende DTOs (LocationResponse von Rollen-Rechte)
├── Controllers/        PingController
├── Data/               HrToolDbContext (Scope-Filter), Value-Converter, EF-Migrationen
├── Domain/Entities/    nur noch das gemeinsame Interface IHasUpdatedAt
├── Serialization/      zentrale JSON-Optionen (camelCase, camelCase-Enums)
├── Services/           Filialverzeichnis (LocationDirectory), ReferenceDataMaintenance (Stammdaten anlegen, Filialen auflösen), Import/ (xlsx-Import der BC-Exporte; bereichsübergreifend)
├── Commands/           CLI-Befehle (import)
├── docs/               Dokumentation und Tabellen-Vorlagen (docs/spreadsheets, git-ignoriert)
├── HrTool-API.Tests/   xUnit: Functional/ (HTTP gegen die API, Nachbardienste ersetzt), Unit/, Infrastructure/ (Fakes, ProbeController)
│                       — per <Compile Remove> vom Haupt-Build ausgeschlossen
└── Dockerfile          Multi-Stage-Image (Port 8080); docker-compose.yaml: Postgres (Port 5435) + Profil "app" (API auf :8300)
```

## Konventionen

- Root-Namespace: `HrTool_API`. Fachlich getrennt in zwei Bereichen mit je einem Namespace: `HrTool_API.Workforce` (Absences/, ShiftPlanning/, Workplaces/) und `HrTool_API.Hr` (Employees/); die Feature-Ordner darunter sind kein eigener Namespace (beide per `<Using>` global eingebunden). Übrige Ordner: Unterordner = Unternamespace. Neue Fachlichkeit gehört in genau einen der beiden Bereiche.
- Alle Antworten unter `/api` im Envelope `{success, data}` bzw. `{success, message, errors[{field, message}]}`; `204` ohne Body. Controller erben von `ApiControllerBase`
  (Ausnahme: `PingController`). **Die frühere Route `/api/v1/ping` und die Antwort ohne Envelope gibt es nicht mehr** (jetzt `GET /api/ping`, `GET /health`).
- Validierungsfehler → `422`; Pflichtfelder per `[Required(ErrorMessage = ...)]` mit deutscher Meldung (implizite Pflicht für Non-Nullable ist abgeschaltet). Fachliche Fehler per `ApiException`.
- **401 nur für „nicht angemeldet/Token ungültig/User deaktiviert“** — nie für etwas anderes, sonst loggt das Frontend den User aus. Fehlendes Recht ist 403 mit der Meldung aus `AuthMessages`.
- PATCH ist partiell: fehlend/`null` = unverändert.
- IDs UUIDv7. Zeitstempel: UTC im Code, `timestamp(0) without time zone` in der DB (Converter aus `Portal/Data/UnspecifiedDateTimeConverters.cs` übernehmen, sobald die erste Entity entsteht).
- Keine DB-Defaults: Defaults stehen nur in den C#-Entities.
- Mitarbeiter, User und Filialen werden **nur als ID** gespeichert (User-ID = Claim `sub`, Personalnummer = `username`, Filial-ID von Rollen-Rechte), ohne Fremdschlüssel und ohne Kopie der Stammdaten.
- Ein Dienst, der ausfällt, darf das HR-Tool nicht mitreißen: geschützte Routen antworten bei nicht erreichbarem Rollen-Rechte mit **503** (fail-closed, nie „erlaubt“), `ping`/`health` laufen weiter.

## Betrieb / Konfiguration

- Lokale Konfiguration über `.env` (Vorlage: `.env.example`), wird beim Start in `Program.cs` geladen; echte Umgebungsvariablen haben Vorrang.
- **JWT:** Das Token stellt Rollen-Rechte aus; hier wird nur die Signatur geprüft: `Jwt__PublicKey`/`Jwt__PublicKeyPath` (RS256, PEM) bzw. Fallback `Jwt__Key` (HS256, mind. 32 Zeichen,
  gleicher Wert wie dort). Issuer/Audience sind nicht gesetzt. Ob der User noch aktiv ist und was er darf, klärt Rollen-Rechte.
- **Rollen-Rechte:** `Access__BaseUrl`, `Access__AppKey` (Default `hr`), `Access__ApiKey` (dort: `Apps__Keys__hr`), `Access__CacheSeconds` (Default 10). Lokal läuft Rollen-Rechte auf `http://localhost:5302`.
- **Datenbank:** `ConnectionStrings__Default` (lokal Port 5435, damit sich Postgres nicht mit Portal 5432, Rollen-Rechte 5433 und Benachrichtigungen 5434 beißt).
- **CORS:** `Cors__AllowedOriginPattern` (Regex) und/oder `Cors__AllowedOrigins__N`. Auch Fehlerantworten tragen die Header.
- **Proxy:** `X-Forwarded-*` wird ausgewertet; ohne `ForwardedHeaders__KnownProxies__N` wird jedem Proxy vertraut (ForwardLimit 1). Rate-Limits liegen im Speicher (pro Instanz).
- Kein `UseHttpsRedirection`: TLS terminiert die Plattform (Azure).
- OpenAPI-Dokument: `GET /api/doc.json` (nur in Development), Security-Scheme `bearerAuth`.
- **Entfernt gegenüber dem ersten Gerüst:** `BusinessCentral__*` (liegt jetzt in BC-Anbindung), Data-Protection-Schlüssel in der DB (nur für die BC-PIN-Ablage nötig), DotNetEnv, `System.Linq.Dynamic.Core`.
  Die lokale `.env` enthält ggf. noch alte `BusinessCentral__*`-Werte; sie werden nicht mehr gelesen und können dort gelöscht werden.

## Rechte (Durchsetzung hier, Vergabe bei Rollen-Rechte)

- Jede Controller-Route ist ausdrücklich `[AllowAnonymous]` (Ping) oder `[HasPrivilege<Entity>(CrudAction.X)]` / `[HasPrivilege(Privileges.X)]`. Ein globaler Filter verlangt für Routen ohne Angabe
  mindestens die Anmeldung; bewusst keine `FallbackPolicy` (sie machte aus unbekannten Routen 401 statt 404).
- **Ablauf je geschützter Anfrage:** Das Token wird lokal auf Signatur geprüft (kaputt/fehlend → 401, ohne Rollen-Rechte zu fragen). Dann fragt der `PrivilegeHandler` Rollen-Rechte
  (`GET /api/me/privileges` mit dem Token des Users, `Access__CacheSeconds` je Token zwischengespeichert): hat der User das Recht, und in welcher Reichweite? Lehnt Rollen-Rechte das Token
  ab (User deaktiviert) → 401; nicht erreichbar → 503; Recht fehlt → 403. Ein Recht ohne gelieferte Reichweite gilt als nicht vergeben (fail-closed).
- **Katalog (generiert):** `PrivilegeCatalog` erzeugt die Rechte aus den Entities: Eine Entity mit `[PrivilegeResource("Label")]`, die als `DbSet` im `HrToolDbContext` steht, bekommt
  `<key>:read/create/update/delete` (Key = Klassenname in snake_case; `Actions =`, `DeleteLabel =`, `ScopedActions =`, `DefinesReach =` steuern es; `[PrivilegeAction("x", "Label")]` ergänzt
  Zusatzaktionen, Bereiche ohne Entity stehen in `CustomAreas`). **Neue Entity = Attribut setzen**, dann die Routen mit `[HasPrivilege<Entity>(…)]` versehen. Die Tests verlangen, dass Katalog
  und Routen einander genau entsprechen. Der Schlüsselraum ist pro Anwendung eindeutig: `schedule:*` u. Ä. gehören dem HR-Tool, `report:*` dem Portal. **Rechte aktuell:** `schedule:*`, `employee:read/update/delete` (Löschen = Deaktivieren, scopbar), `absence:*` (scopbar), `absence_type:*`, `shift_pattern:*`, `workplace:read/update` (Stammdaten nicht scopbar). `Shift` hat kein eigenes Recht: Schichten lesen = `schedule:read`, ändern = `schedule:update`. Noch ohne Rechte und Routen: `cashier_profile` (kommt mit dem Import in die DB, aber noch keine API). **Nur CRUD**, Zusatzrechte (`publish`, `approve`, Abwesenheitsgründe) kommen bewusst erst, wenn feststeht, wer was sehen darf.
- **Anmeldung des Katalogs:** `CatalogRegistration` meldet ihn beim Start per `PUT /api/apps/hr/catalog` (`Access__ApiKey`) an Rollen-Rechte; läuft im Hintergrund, Wiederholung alle 30 s, das HR-Tool startet auch ohne Rollen-Rechte.
- **Scope (Reichweite):** Der Scope zum verlangten Recht (`ScopeContext`) gilt für die Abfragen der Anfrage; Daten außerhalb liefern 403 mit der vordefinierten Meldung (existiert der Datensatz nicht: 404),
  in Listen fehlen sie. Das HR-Tool kennt die Dimension `location` (dieselben Filialen wie überall; die Region eines Mitarbeiters ergibt sich aus seiner Heimatfiliale). Weitere Dimensionen in einem Scope
  (z. B. `report`) werden ignoriert und gewähren nichts. Die Dimension `employee` (Vertretung, Einzelfreigabe) braucht erst Rollen-Rechte (`ScopeDimensions`) und dann `ScopeSet` hier.
  Ein globaler EF-Filter im `HrToolDbContext` (wie bei `Report` im Portal) setzt den Scope durch; `FindAsync` wendet keine Filter an.
- **Filialen:** `LocationDirectory` holt Namen, Store-Code und Archiv-Status von Rollen-Rechte (`GET /api/apps/hr/locations`, 60 s zwischengespeichert; bei Ausfall die zuletzt bekannte Liste, ohne Liste → 503).

## Noch offen

- Controller, `[PrivilegeResource]`-Rechte und Import aus BC für die Entities des Datenmodells (Importquellen: `docs/spreadsheets`, BC-Seiten `CS_Employees`/`StaffList`/`EmplNonAvailability`, siehe unten).
- BC-Daten (Mitarbeiterliste, Dienstplan-Webdienste): neue Endpunkte in BC-Anbindung, hier ein Client nach dem Muster `HttpLocationDirectory` (eigener Aufruferschlüssel `Gateway__ApiKeys__Hr`).
- Ereignisse (`schedule.published`, `schedule.changed`) an Benachrichtigungen: Handler dort, hier Client nach dem Muster `HttpReportEvents` des Portals (Ereignis-ID, zwei Versuche; später Outbox).
- Dienstplan-Leseschnittstelle für das Portal (`GET /api/schedules?employeeId=…&from=…&to=…`, Token des Users wird weitergereicht).
- `GET /api/access/me?app=…` statt `/api/me/privileges`, sobald Rollen-Rechte es anbietet; gemeinsames Rechte-Paket statt kopiertem Code (derzeit identisch mit dem Portal, Änderungen an `Authentication/` dort mitziehen).

## Tests

- `make test` bzw. `dotnet test HrTool-API.Tests` — **Docker muss laufen** (Testcontainers startet `postgres:17-alpine`, `SchemaTests` prüft Migration, Eindeutigkeiten, Löschverhalten und Scope-Filter).
- `Functional/` startet die komplette API per `WebApplicationFactory<Program>`; jeder Test nutzt eine eigene Fake-Client-IP (`X-Forwarded-For`), damit sich die Rate-Limiter nicht überlappen.
- Die anderen Dienste sind ersetzt: `FakeAccessDirectory` (Rechte/Scopes je Token, „Token unbekannt“, „nicht erreichbar“), `FakeLocationDirectory`. Die echten HTTP-Clients sind in `Unit/ServiceClientTests`
  gegen einen Fake-HTTP-Handler getestet. Tokens sind echt (HS256) signiert (`NewToken`).
- Solange das HR-Tool keine geschützten Routen hat, zeigt `Infrastructure/ProbeController` (`/api/probe`, Recht `probe:read`, per ApplicationPart nur in den Tests eingebunden) das Verhalten der Rechteprüfung.
  Die Katalog-Tests betrachten nur Routen der Anwendung selbst, nicht den ProbeController.
- Neues Verhalten zuerst mit einem Test festnageln.

## Build / Deployment

- `make build`, `make run` (Port 5129), `make test`, `make db-up`, `make migrate`, `make api-up` (Container, Port 8300; Rollen-Rechte muss erreichbar sein).
- `make publish` erzeugt `app.zip`, `make deploy` lädt es per `az webapp deploy` hoch (`RESOURCE_GROUP`/`APP_NAME` im Makefile prüfen). Kein Migrationsschritt im Deploy; `/health` ist der Health-Check.
  Für Updates ohne Ausfall: Deployment-Slot mit Tausch.

## Datenmodell

Abgeleitet aus den Exporten in `docs/spreadsheets` (BC/LS Central; sie enthalten echte Personaldaten und sind git-ignoriert — nicht weitergeben). Die echte Anbindung steht aus: **Die Entities sind die Zielstruktur für den Import**,
die Schlüssel aus BC („Nr.“, „Code“) stehen deshalb als eindeutige Spalten dabei. Alle IDs UUIDv7, Zeiten UTC, Datum/Uhrzeit als `DateOnly`/`TimeOnly`, Enums als Kleinbuchstaben-Text.

| Entity (Tabelle) | Quelle | Kern |
|---|---|---|
| `Workplace` (`workplaces`) | **Arbeitsorte.xlsx** (Stamm aus BC, 42) | `Code` (unique), `Name`, `StoreCode` („Store Dim Code“), `CostCenterCode`, `RegionCode`, `TimeAccountActive`, `VacationThreshold`, `LocationId` (Filiale bei Rollen-Rechte, aufgelöst über die Store-Nr.; unique) |
| `PlanningRole` | „Rolle“: `VK`, `VK_SA`, `BÜ`, `LG` | `Code` |
| `EmploymentContract` | „Beschäftigungsverhältniscode“ (81, z. B. `TZ 15,25H`, `MINI 6H`, `AZU 40H`) | `Code`, `WeeklyHours` (aus dem Code gelesen) |
| `Employee` (`employees`) | Mitarbeiterliste (Personalplanung), ~770 | `EmployeeNo` („Nr.“, fünfstellig, **Text**), `UserId` (sub), Name, Funktion, E-Mail, Vertrag, `WorkplaceId` (Arbeitsort), Rolle, `RegionCode`, `ShiftSwapEnabled` |
| `CashierProfile` | Kassiererübersicht (1:1, ID = Personalnummer) | Bonname, Arbeitsverhältnis (Kassierer/Verkäufer), Filialnr., Berechtigungsgruppe, Profile (im Export leer) |
| `ShiftPattern` | Schichtmuster, ~530 | `Code` (unique, z. B. `AB/VZ1`), Beschreibung, `Kind` (Tages-/Wochenmuster), `WeekCount` |
| `AbsenceType` | Nichtverfügbarkeitsarten, 24 | `Code`, Beschreibung, `PaidTime`, `ScheduleSymbol` (`U`, `K` …), **`RequiresApproval`** (nur `URLAUB`; beim Import zu setzen) |
| `Absence` | Mitarbeiter Nicht-Verfügbarkeit (nur Spaltenköpfe), **HR Urlaubscockpit.xlsx** | Mitarbeiter, Art, von/bis, `Days` („Arbeitstage (berechnet)“, aus BC), `VacationDays`, Zeit von/bis, Bemerkung, `RequestedAt`, **`ApprovalStatus`** und **`RejectionReason`** (nur Urlaubsanträge), bestätigt, erfasst von |
| `Schedule` (`schedules`) | Mitarbeiterliste (= Liste der **Dienstpläne**), ~2300 | `Number` (unique, `DPAB0000039`), `WorkplaceId`, Jahr/Monat (**unique je Arbeitsort**), `Status` (Entwurf/Bestätigt), `BudgetHours` |
| `Shift` (`shifts`) | Dienstplan-Matrix, Arbeitsplanansicht | Plan, Mitarbeiter, Datum, Beginn/Ende, `ShiftCode` (`VKA1`), Rolle, Nicht-Verfügbarkeit, Bemerkung |

- **Scope:** `Employee`, `CashierProfile`, `Absence`, `Schedule` und `Shift` sind per globalem Filter auf die Filialen im Scope begrenzt, und zwar **über den Arbeitsort** (`Workplace.LocationId`): Mitarbeiter und Pläne über ihr `WorkplaceId`, Schichten über den Plan, Absence/CashierProfile über den Mitarbeiter.
  Daten von Arbeitsorten **ohne Filiale** (`BS`, `SABA`; auch solange die Filiale noch nicht aufgelöst ist) und Mitarbeiter ohne Arbeitsort sieht nur, wer uneingeschränkt darf; ein leerer Scope sieht nichts.
  Stammdaten (`Workplace`, `PlanningRole`, `EmploymentContract`, `ShiftPattern`, `AbsenceType`) sind ungefiltert.
- **Nicht gespeichert, weil berechnet oder aus anderen Quellen:** Gesamt-/Soll-Stunden (Wochenstunden × Tage / 7), „Gesamt Geplant“, „Geplant %“, Warnungen, Beträge, „Tatsächlich“ (Zeiterfassung in BC).
- **Arbeitsorte (Stamm `Arbeitsorte.xlsx`, 42):** Fast jeder Arbeitsort ist eine Filiale (Store-Nr. = „Store Dim Code“, z. B. `AH`→`0012`); **Ausnahmen `BS` (Berufsschule) und `SABA` (Samurai Bar)** ohne Store-Nr., `SABA` hat einen Plan im Export. `HO` hat die Store-Nr. `95`.
  Alle Arbeitsorte aus den Exporten der Mitarbeiter und Pläne stehen im Stamm. Der Stamm liegt als `Data/Seed/workplaces.json` im Repo (eingebettet) und wird beim Start von `ReferenceDataMaintenance` angelegt (nur fehlende; vorhandene werden nie überschrieben).
  Dieselbe Hintergrundaufgabe löst alle 10 Minuten `Workplace.LocationId` auf: Store-Nr. des Arbeitsorts = `store_code` der Filialen von Rollen-Rechte (führende Nullen zählen nicht; je Filiale höchstens ein Arbeitsort). Bis Rollen-Rechte die Filialen hat, bleibt `LocationId` leer und der Scope sieht diese Arbeitsorte nicht.
  Abschalten: `ReferenceData__MaintenanceEnabled=false`. Manuell zuordnen: `PATCH /api/workplaces/{id}`. **Bei Wechsel auf die echte BC-Anbindung** den Stamm von dort laden statt aus der Datei.
- **Urlaubsanträge und Genehmigung:** Genehmigt werden muss nur **Urlaub** (`AbsenceType.RequiresApproval`). Alle anderen Nichtverfügbarkeiten pflegt ausschließlich HR im Dienstplan ein und sie gelten sofort als genehmigt (`ApprovalStatus` bleibt leer).
  Ablauf: Mitarbeiter beantragt → Vorgesetzter akzeptiert → HR entscheidet im **HR Urlaubscockpit** (BC-Liste mit Filter Filiale, Von, Bis und „Alle Status anzeigen“; ohne den Haken nur Anträge, die auf HR warten). Im Cockpit-Export gibt es nur den Status `Vorgesetzter akzeptiert`
  (29 von 29 Zeilen) und ein leeres „Ablehnungsgrund“; weitere Status sind nicht belegt, deshalb bleibt `ApprovalStatus` Text. „Arbeitstage (berechnet)“ (= `Days`) rechnet BC nach den Arbeitstagen des Mitarbeiters (Wochenende zählt, Feiertage nicht) und lässt sich hier nicht nachrechnen.
- **Bewusst offen:** Tage/Zeiten der Schichtmuster und ihre Zuordnung zu Mitarbeitern (nicht im Export), Schichtdefinitionen hinter Codes wie `VKA1` (Zeiten, Rolle, Pause), Pausenregeln, Schichttausch-Anträge (im Export gibt es nur die Freigabe `ShiftSwapEnabled`), Outbox für Ereignisse.
- **BC-Webdienste** (`Webdienste.xlsx`, 380 Einträge): vorhanden sind `CS_Employees` (Seite 6225278), `StaffList` (99001473), `EmplNonAvailability` (10015024), die Codeunits `GetStaff`/`GetStaffV2`, `GetWorkShift`/`SendWorkShift` und `Zeiterfassung`.
  **Nicht veröffentlicht** sind Dienstpläne, Schichtmuster und Nichtverfügbarkeitsarten: Dafür müssen die BC-Seiten erst als Webdienst freigegeben werden, bevor BC-Anbindung sie liefern kann.

## Schnittstelle: Dienstpläne (`/api/schedules`)

| Route | Recht | Verhalten |
|---|---|---|
| `GET /api/workplaces`, `GET /api/workplaces/{id}`, `PATCH /api/workplaces/{id}` | `workplace:read` / `workplace:update` | Arbeitsorte mit Stammdaten (Name, Store-Nr., Kostenstelle, Region, Zeitkonten, Urlaubs-Schwellwert, `locationId`); PATCH setzt nur `locationId` (Filiale muss existieren, je Filiale ein Arbeitsort: 409) |
| `GET /api/schedules` | `schedule:read` | Liste mit Paging; Filter `workplaceId`, `locationId` (alle Arbeitsorte der Filiale), `year`, `month`, `status`; neueste zuerst |
| `GET /api/schedules/{id}` | `schedule:read` | ein Plan |
| `POST /api/schedules` | `schedule:create` | Plan anlegen (`number`, `workplaceId`, `year`, `month`, optional `status`, `budgetHours`); die Filiale des Arbeitsorts muss im Scope liegen (Arbeitsorte ohne Filiale nur uneingeschränkt, sonst 403) und bei Rollen-Rechte existieren (nicht archiviert, sonst 404); je Arbeitsort und Monat ein Plan, Plannummer eindeutig (409) |
| `PATCH /api/schedules/{id}` | `schedule:update` | nur `status` und `budgetHours`; Nummer, Filiale, Jahr, Monat bleiben |
| `DELETE /api/schedules/{id}` | `schedule:delete` | nur Pläne im **Entwurf** (sonst 409); die Schichten gehen mit |
| `GET /api/schedules/{id}/matrix` | `schedule:read` | **Dienstplan-Ansicht**: Monat + Rand (`marginDays`, Standard 6), je Mitarbeiter Schichten, Soll, Geplant, Geplant %, dazu „anderswo eingeteilt“ (`away`) |
| `GET /api/schedules/{id}/shifts` | `schedule:read` | Schichten, Filter `employeeId`, `from`, `to`; ohne Paging, sortiert nach Tag/Beginn |
| `POST /api/schedules/{id}/shifts` | `schedule:update` | Schicht anlegen |
| `PATCH /api/schedules/{id}/shifts/{shiftId}` | `schedule:update` | partiell; Mitarbeiter und Plan ändern sich nicht |
| `DELETE /api/schedules/{id}/shifts/{shiftId}` | `schedule:update` | 204 |

- Plan außerhalb des Scopes: **403** mit der vordefinierten Meldung, unbekannte ID: 404. Die Schichten folgen dem Scope ihres Plans.
- Schichten: das Datum muss im Monat des Plans liegen, Beginn und Ende gelten nur zusammen (Ende vor Beginn = über Mitternacht), Rolle und Nichtverfügbarkeitsart müssen existieren (422).
  Der Mitarbeiter muss existieren und aktiv sein, **liegt aber nicht zwingend in der Filiale des Plans** (Aushilfe aus anderer Filiale); die Prüfung läuft ohne Scope-Filter und gibt keine Mitarbeiterdaten preis.
- Antworten tragen nur die Mitarbeiter-ID, keine Namen: Namen holt man über die (noch fehlenden) Mitarbeiter-Routen mit deren Rechten.
- **Stundenrechnung** (`ScheduleCalculator`, abgeleitet aus dem fertigen BC-Dienstplan `docs/spreadsheets/Dienstplan (1).xlsx`, an 10 von 10 Mitarbeitern ohne Abwesenheit nachgerechnet): Schicht mit Zeiten abzüglich Pause
  (über 6 h: 30 min); über Mitternacht, wenn das Ende vor dem Beginn liegt (`16-0` = 8 h brutto); Schichtcode ohne Zeiten zählt 0; Nicht-Verfügbarkeit **mit** Zeiten zählt als geplant, **ohne** Zeiten (`U`, `FT`) nicht;
  Soll = Wochenstunden aus dem Vertrag × Tage des Monats / 7; Geplant % = Geplant / Soll. Nur Schichten im Monat des Plans zählen. `plannedHours` steht in den Antworten für einen einzelnen Plan (Detail, Anlegen, Ändern), nicht in der Liste.
  **Nicht an BC geprüft:** die Pause für Schichten über 9 h (45 min, gesetzliches Minimum; im Export gibt es keine).
- **Matrix = BC-Ansicht:** Der Export zeigt 6 Tage Rand vor und nach dem Monat; diese Schichten gehören zu den Nachbarplänen derselben Filiale und zählen nicht in die Summen. Nach den Mitarbeitern der Filiale (auch ohne Schichten, nur aktive)
  folgen Mitarbeiter anderer Filialen mit Schichten in diesem Plan (ohne Soll). Die „YY &lt;Arbeitsort&gt; von - bis“-Zellen aus BC sind **keine eigenen Daten**, sondern Schichten eigener Mitarbeiter in Plänen anderer Filialen: Die Matrix liefert sie als `away`
  (Datum, Zeit, Filiale; ohne Scope-Filter, aber nur diese drei Angaben) und zählt sie nicht in diesem Plan.
- **Aus dem Export noch unverstanden:** Zellen der Form `0/1`, `2/1`, `5/1` (nur bei ca. 6 Mitarbeitern ohne Stunden; vermutlich Platzhalter/offene Schichten), die Zelle `OK`, das Freitext `U per al…`, sowie die ~46 Mitarbeiter anderer Filialen unter dem Trennstrich `--------`
  (BC listet sie als verfügbar; die Matrix zeigt nur die mit Schichten). Dafür fehlen Daten oder eine Auskunft aus BC.
- **Annahmen, die ich nicht belegen konnte:** Bestätigte Pläne sind nicht löschbar; ein bestätigter Plan darf weiter bearbeitet werden (Schichten ändern ist nicht gesperrt) — beides ist eine fachliche Entscheidung und leicht zu ändern.
