# Phase 0: Bestandsaufnahme

Stand: 2026-10-21 · Grundlage: `Umsetzungsplan Mitarbeiterportal – Backend-Architektur.md`

> **Nachtrag (2026-10-21, nach Freigabe durch den Projektverantwortlichen):** Der Bericht beschreibt den Stand vor den Entscheidungen. Seitdem gilt: `IRS-API` heißt jetzt `Portal` (Ordner, Projekt, Namespace); `hr-tool-API` ist intern in `Workforce/` und `Hr/` getrennt; das feingranulare ACL in Rollen-Rechte, der BC-PIN-Login (mit zusätzlichem DB-Cache), `GET /api/me/privileges` und `report.created` bleiben. Damit entfallen bzw. ändern sich u. a. die Fragen 1–6 in Abschnitt 5 und mehrere Umbauzeilen für Rollen-Rechte und Benachrichtigungen (Rechtemodell-Reduktion, Entfernen der Report-Fachlogik, „als veraltet markieren“). Die verbindliche Fassung steht in der Root-`CLAUDE.md` unter „Entscheidungen nach Phase 0“.
>
> **Nachtrag 2 (2026-10-21):** Die Bestandsdienste liegen jetzt unter `Archiv/` (z. B. `Archiv/Rollen-Rechte`); die Pfade in diesem Bericht meinen diese Ordner. Neue Entwicklung findet unter `src/` statt (Entscheidung 10).

Es wurde **kein Code geändert**. Die Analyse ist rein lesend. Es wurde nichts gebaut und kein Test ausgeführt; Testanzahlen stammen aus dem Zählen der `[Fact]`/`[Theory]`-Attribute (Theory-Fälle nicht mitgezählt). „Portal“ im Plan = `IRS-API`.

## 1. Überblick

Im Repo liegen fünf Dienste, der Plan nennt in Phase 0 nur drei. `IRS-API` und `hr-tool-API` wurden zusätzlich analysiert, weil sie im Repo liegen und die Zuordnung sonst nicht beurteilbar ist.

| Ordner | Zielbild laut Plan | LOC (ohne Tests) | Tests (Attribute) | Umbauaufwand gesamt |
| --- | --- | --- | --- | --- |
| `BC-Anbindung` | BC Integration Service | ~830 | ~27 | **groß** (kaum Code, aber fast alles neu) |
| `Rollen-Rechte` | Access Service | ~5.400 | ~225 | **groß** |
| `Benachrichtigungen` | Notification Service | ~1.700 | 59 | **mittel bis groß** |
| `IRS-API` | Portal API (nur Gerüst) | ~8.000 | ~167 | **mittel** (Fachlogik bleibt unangetastet) |
| `hr-tool-API` | siehe 1.1 | ~5.400 | ~134 | **groß** |

### 1.1 Zuordnung von `hr-tool-API` (Entscheidung nötig)

Der Ordner heißt „HR-Tool“, enthält aber überwiegend **Workforce-Fachlichkeit**: Dienstpläne, Schichten, Abwesenheiten, `ScheduleCalculator`, Flag für Schichttausch. Recruiting, Lohnarten und DATEV (der Kern der Plan-„HR API“) gibt es nicht. Zusätzlich liegen dort Stammdaten (`Employee`, `Workplace`, `EmploymentContract`, `PlanningRole`, `ShiftPattern`) und `CashierProfile` (Kassenberechtigungen, passt in keinen Plan-Dienst).

Das steht im Konflikt mit dem Plan: Dort ist die HR API out of scope, Dienstplanung/Schichttausch sind ebenfalls out of scope, und nur die Abwesenheiten (Phase 6) werden gebaut. Die Zuordnung in der Root-`CLAUDE.md` („hr-tool-API = HR API“) ist deshalb wahrscheinlich falsch und wird nach deiner Entscheidung korrigiert.

### 1.2 Repo-Struktur (Monorepo)

- `BC-Anbindung`, `Benachrichtigungen`, `IRS-API` und `Rollen-Rechte` enthalten jeweils ein **eigenes `.git`** (2, 2, 17 bzw. 2 Commits). `hr-tool-API` hat keines. Das Root-Repo kann diese Ordner so nicht als normale Verzeichnisse versionieren (`git status` zeigt sie nur als untracked). Vor dem Umzug nach `src/` muss entschieden werden, ob die Historie (v. a. die 17 Commits der IRS-API) erhalten bleibt oder verworfen wird.
- Alle Dienste haben Dockerfile/Makefile/docker-compose, ein Hauptprojekt mit dem Testprojekt im Unterordner (per `<Compile Remove>` ausgeschlossen) und je eine eigene `.slnx`. Es gibt kein `Directory.Build.props`, kein `Directory.Packages.props`, kein Aspire, keine `Shared.*`.

## 2. Übergreifende Abweichungen (gelten für alle Dienste)

| Thema | Ist | Soll (Plan) | Aufwand |
| --- | --- | --- | --- |
| Messaging | nur synchrones HTTP, API-Keys | Service Bus (Topics/Queues), Wolverine, Outbox/Inbox | groß (Phase 1) |
| Auth | eigene JWTs (Rollen-Rechte stellt aus), `X-Api-Key` zwischen Diensten | Entra ID, App Roles, Managed Identity, `Shared.Authorization`, Test-Auth-Handler | groß |
| Rechteprüfung | `GET /api/me/privileges` pro Request (Cache 10 s), bei Ausfall 503 | lokal über `PermissionsChanged` und Cache, Access nie im Request-Pfad, Weiterbetrieb mit letztem Stand | groß |
| Mitarbeitende/Filialen | nur IDs oder In-Memory-Cache, per HTTP geholt | Kopien aus Events (`EmployeeChanged`, `StoreChanged`), Erasure-Handler | groß |
| Zeit | `DateTime.UtcNow` in jedem Dienst (BC 2 Dateien, Benachr. 5, IRS 5, Rollen-Rechte 8, hr-tool 7); `TimeProvider` teils registriert, nur punktuell genutzt | ausschließlich `TimeProvider`, Architekturtest | klein bis mittel |
| `TreatWarningsAsErrors` | fehlt überall | Pflicht | klein (danach ggf. Warnungen beheben) |
| Nullable | aktiv überall | aktiv | passt |
| Secrets | `appsettings.json` überall sauber; Dev-Passwörter in `appsettings.Development.json` (IRS-API, hr-tool-API) und `docker-compose.yaml` (Rollen-Rechte); eigener `.env`-Loader in `Program.cs` | User Secrets / Aspire-Parameter | klein |
| DB-Schema | jeder Dienst `public` | ein Schema pro Dienst | klein |
| Sensitivity-Klassifizierung | nicht vorhanden | `[Sensitivity]`, Architekturtest | mittel |
| Löschung | nirgends `EmployeeDataErasureRequested` | Handler + Integrationstest pro API | mittel |
| Projektlayout | Ein-Projekt-Monolithen, Controller | `Api/Core/Infrastructure/Contracts`, Minimal APIs (Controller nur wo vorhanden, also erlaubt) | mittel |
| Tests | xUnit, `WebApplicationFactory`, Testcontainers/Postgres, kein EF-InMemory (passt) | zusätzlich Service-Bus-Emulator, WireMock, Verify, NetArchTest, Aspire-E2E | mittel |
| Observability | `/health` vorhanden (ohne DB-Check), keine OpenTelemetry | ServiceDefaults, `/health/live` und `/health/ready` | klein bis mittel |

## 3. Dienste im Detail

### 3.1 BC-Anbindung → BC Integration Service

**Ist:** MVC-Controller, **keine Datenbank**, keine NuGet-Pakete im Hauptprojekt. Endpunkte: `POST /api/employees/verify` (PIN-Prüfung über BC), `GET /api/stores` (300 s Cache), `/api/ping`, `/health`. Eingehende Auth per `X-Api-Key` je Aufrufer (Identity, Portal, Hr), zeitkonstanter Vergleich.

**BC-Zugang:** ausschließlich SOAP-1.1-Codeunit-Webservices (`…/WS/{Company}/Codeunit/{Name}`), nur zwei Aufrufe: `Zeiterfassung.GetStatus` und `GetStores.GetStores` (LSC Store, Felder `No.`, `Name`). Auth: Client Credentials mit Client Secret gegen Entra, Token im Singleton gecacht. Kein OData, keine Standard-API, keine API Pages.

| Thema | Ist | Soll | Aufwand |
| --- | --- | --- | --- |
| Einziger BC-Zugang | ja (laut CLAUDE.md, Aufrufer nicht geprüft) | ja | passt |
| Sync-Status, ID-Mapping, Outbox, Fehlerprotokoll | fehlt (keine DB) | Postgres/EF, Mapping-Tabelle | groß |
| Events/Commands (`Employee*`, `StoreChanged`, `SyncAbsenceToBc`, `BcSync*`) | fehlt | Service Bus + Wolverine | groß |
| Stammdaten lesen (`lastModifiedDateTime`, Hired/Left ableiten) | nur Stores, keine Mitarbeiterliste, keine Zeitstempel | periodischer Abgleich mit Delta-Erkennung | groß, **hängt am BC-Modell** |
| Resilience: Rate Limiter, Retry mit `Retry-After`/429, Circuit Breaker, Timeouts, Prioritäts-Queues | nur 10-s-Timeout und Retry für Token-Fehler `AADSTS7000215`; HTTP-Status von BC gehen verloren (`BcCodeunitClient.cs:44`) | vollständig | mittel |
| BC-Schreiben (Upsert, Versionsprüfung, Fehlerklassen) | fehlt | ja | groß, hängt am BC-Modell |
| Auth eingehend | API-Key | Entra, App Role je Fähigkeit | mittel |
| BC-Auth ausgehend | Client Secret | Managed Identity bevorzugt (Plan: Entra-Token für BC) | klein bis mittel |
| Logging | PIN/Body werden nicht geloggt (Test belegt); `BcAuthService.cs:108` loggt den vollständigen AAD-Fehlerbody (Tenant-/App-Details, zu prüfen) | nur Typ/IDs | klein |
| Zeit | `DateTime.UtcNow`/`DateTimeOffset.UtcNow` in `BcGateway.cs:38`, `BcAuthService.cs:30,38,90` | `TimeProvider` | klein |
| Domänenordner `MasterData/`, `Workforce/`, `Inventory/` (README) | fehlt | ja | klein |
| Tests | Fake auf `IBcCodeunitClient`-Ebene; Token-Service, SOAP-Client, Retry, 429 ungetestet | WireMock-Szenarien unter `tests/Fixtures/Bc`, `BcSandbox`-Trait | mittel bis groß |

**Begründung der Änderungen:** Der Dienst ist heute ein reines Gateway ohne Zustand. Der Plan macht ihn zum Besitzer von Sync-Status, Mapping und Outbox, daher sind DB und Messaging zwingend. Der Gateway-Kern (Token, SOAP-Client, `LscCodeunitResponse`) ist wiederverwendbar.

**Offene Frage (BC-Modell):** Der Plan nimmt `lastModifiedDateTime` und Upsert über externe ID an. Der Bestand kennt nur SOAP-Codeunits. Ob BC dafür Standard-API/OData/API Pages anbietet oder neue Codeunits nötig sind, ist unbekannt.

### 3.2 Rollen-Rechte → Access Service

**Ist:** Controller; Login, Benutzer, Rollen, Rechte, Scopes, Filialen. Tabellen: `locations`, `users`, `refresh_tokens`, `acl_role`, `acl_user_role` (mit `scoped`, `home_scope`, `valid_from`/`valid_to`), `acl_user_role_scope`, `scope`, `scope_member` (Dimension `location`/`report`), `custom_privilege`, `app_privilege`. Keine Employee-Tabelle (`users` ist ein Login-Konto, Username = Personalnummer).

**Endpunkte:** Auth (`/api/auth/login`, `/api/token/refresh`), Me (`/api/me/privileges`, `PATCH /api/me/password`), Apps (`PUT /api/apps/{appKey}/catalog`, `GET …/locations`, `POST …/recipients`, API-Key), Locations, Admin-CRUD (`users`, `roles`, `scopes`, `custom-privileges`, `privileges`).

**Synchron pro Request genutzt:** `GET /api/me/privileges` (Portal, hr-tool), `POST …/recipients` (Benachrichtigungen), `GET …/locations`. Das ist der Hauptkonflikt mit „Access nie im Request-Pfad“.

| Thema | Ist | Soll | Aufwand |
| --- | --- | --- | --- |
| Besitzer von Scopes/Vertretungen/Sonderrechten | Scope-Modell mit Gültigkeit und Heimat-Scope vorhanden | zusätzlich abgeleitete Scopes, Vorgesetztenbeziehung, Vertretungen, zeitgesteuerter Ablauf | groß |
| Rechtemodell | feingranulare Privilegien pro Rolle, Katalog, Registrierung durch Apps, Custom Privileges | 4 grobe Entra-App-Roles + Scopes | groß (**zentrale Entscheidung**) |
| `PermissionsChanged` mit Versionszähler, Cache-Rebuild-Endpunkt | fehlt | ja | groß |
| Kopien Mitarbeiter/Filialen aus Events | fehlt, Filialen per HTTP-Sync | Events | groß |
| Eigener Token-Issuer (RS256/HS256, 30 Tage, Refresh), Passwörter (bcrypt/Argon2), BC-PIN-Login mit verschlüsselter PIN, Login-Throttle | vorhanden | Entra ID, nicht mehr hier | groß |
| BC-Zugriff | `HttpBcGateway`/`BcStoreSync` im Access Service | Regel 5: nur BC Integration | mittel |
| Service-zu-Service | statischer API-Key | Managed Identity + App Roles | mittel |
| Synchrone Prüf-Endpunkte | in Benutzung | als veraltet markieren, Entfernung erst nach Rückfrage | mittel |
| Zeit | `DateTime.UtcNow` in `AccessDbContext.cs:158` und allen Entity-Initialisierern (`AclRole`, `AclUserRole`, `AppPrivilege`, `CustomPrivilege`, `Scope`, `User`); Services injizieren überwiegend `TimeProvider` | nur `TimeProvider` | klein |
| `EmployeeLeft`/`EmployeeDataErasureRequested` | fehlt | Handler + Test | mittel |
| Tests | ~225, TimeProvider.Testing vorhanden, `FakeBcGateway` | zusätzlich Event-/Architektur-/Verify-Tests | mittel |

**Wiederverwendbar:** Scope-Modell und `ScopeQueries`/`ScopeSet`, `AclGuard`, `RecipientResolver`-Logik, Privilegien-Generator, EF-Konfiguration, Envelope/Fehlerbehandlung, Testinfrastruktur.
**Wahrscheinlich entfallend (nach Plan, nur mit Zustimmung):** `Jwt*`, `PasswordService`, `PinProtector`, `BcLoginService`, `LoginThrottle`, `refresh_tokens`, Passwort-Endpunkte, `HttpBcGateway`, `BcStoreSync`, CLI `decrypt-pin`/`sync-stores`, ggf. `custom_privilege`.

### 3.3 Benachrichtigungen → Notification Service

**Ist:** Controller. Endpunkte: `POST /api/events` (API-Key je Absender), `GET /api/me/push-public-key`, `POST/DELETE /api/me/push-subscriptions`, `GET/PATCH /api/me/notification-settings`, `ping`, `health`. Tabellen: `push_subscriptions`, `user_notification_settings`, `processed_event`. Kanal: nur Web Push (VAPID, `Lib.Net.Http.WebPush 3.3.1`). Idempotenz über Event-ID. Es gibt **keinen** InApp-Kanal, keine Notification-Tabelle, keine Vorlagen.

**Fachlogik anderer Domänen (Entfernung nur nach Rückfrage):**
- `Services/ReportCreatedHandler.cs`: Recht `report:read`, Filterung nach `locationIds`, Kategorieregeln (Virus immer, Bug nach Mindestschwere usw.), deutsche Titel „Neue Meldung: …“, Link `/wghn-admin/meldungen/{category}`.
- `Domain/Enums/ReportCategory.cs`, `ReportSeverity.cs`, `Contracts/EventContracts.cs` (`ReportCreatedData`).
- `UserNotificationSettings`: Felder Bug/Theft/Other/Suggestion, `BugNotificationMinSeverity`, samt DTOs und Controller.
- Empfängerermittlung per Rollen-Rechte (`HttpRecipientDirectory`).
- Die Wochentagsregel (Europe/Berlin) ist generisch und könnte als Präferenz bleiben (offen).

| Thema | Ist | Soll | Aufwand |
| --- | --- | --- | --- |
| `SendNotification`-Command mit NotificationId, TemplateKey | fehlt | ja, Idempotenz über NotificationId | groß |
| Vorlagen (Dateien im Repo) | fehlt, Push-Body ist der Freitext `description` der Meldung (`ReportCreatedHandler:43`) | Vorlagen mit Platzhaltern | mittel |
| Klasse-2-Schutzregel | fehlt, **aktuell gelangt Meldungsfreitext in den Push-Text** | Ablehnung bei Klasse 2 und anderem Kanal als `InApp` | mittel |
| Kanäle | Push (Adapter-Interface + Implementierung), E-Mail/InApp fehlen; Fakes nur in Tests | `InApp` (gespeichert, Endpunkt „meine Benachrichtigungen“, gelesen markieren), Push/Mail als Adapter mit Fake | mittel |
| Zustellung | synchron im Request, Fehler nur geloggt, kein Retry, keine Zustellstatus | Zustellstatus, Retry | mittel bis groß |
| Präferenzen | domänenspezifisch (Report-Kategorien) | pro Kanal und Benachrichtigungstyp, Pflicht-Kennzeichen in Vorlage | mittel |
| Retention 90 Tage, Erasure | fehlt | ja | klein bis mittel |
| Auth | JWT (nur Signatur, Issuer/Audience nicht geprüft, `AuthSetup.cs:78-79`), API-Key je Absender | Entra, App Roles | mittel |
| Logging | Handler loggt nur ReportId; Push-Endpoint-URL wird geloggt (pseudonymer Identifier, `PushNotificationSender`) | kürzen/hashen | klein |
| Zeit | `DateTime.UtcNow` in `ProcessedEvent.cs:19`, `PushSubscription.cs:20`, `UserNotificationSettings.cs:25`, `NotificationsDbContext.cs:84`; `DateTimeOffset.UtcNow` in `PingController.cs:12` | `TimeProvider` | klein |

**Wiederverwendbar:** Push-Adapter samt Tests (VAPID-Schlüsselpaar und Payload-Format für den Angular-Service-Worker unverändert lassen), Push-Abo-Tabelle/-Endpunkte, Idempotenzmuster, Testinfrastruktur.
Hinweis: Der Abgleich dieses Berichts mit dem Plan beruhte beim ersten Analyseschritt auf einer Zusammenfassung; die Tabelle oben wurde gegen die Plan-Phase 5 und den Nachrichtenkatalog geprüft.

### 3.4 IRS-API → Portal API (nur Gerüst)

**Ist:** Controller: `reports`, `reports/archived`, `statistics/*`, `faqs`, `admin/faq/images`, `ping`. Tabellen: `reports`, `report_location`, `faqs`, `faq_images`; Scope-Filter als EF-Query-Filter auf Reports; neun Migrationen (Altlast aus Symfony/Doctrine, Migrationen nicht auf die Prod-DB anwenden, History manuell markieren).

**Externes (alles synchron):** `HttpAccessDirectory` (pro Request `GET /api/me/privileges`, 10 s Cache, kein Fallback auf letzten Stand), `HttpLocationDirectory` (60 s Cache, stale-ok), `CatalogRegistration` (BackgroundService), `HttpReportEvents` (`POST /api/events` an Benachrichtigungen, 2 Versuche, bei längerem Ausfall geht das Ereignis verloren; enthält `description`).

| Thema | Ist | Soll | Aufwand |
| --- | --- | --- | --- |
| Kopien Mitarbeitende/Filialen (Tabellen, Inbox, Handler, Erasure) | fehlt (nur In-Memory) | ja | groß |
| Event-Verteilung an dritten Konsumenten | HTTP-Push | Service Bus | groß |
| Outbox für `report.created` | fehlt | in derselben Transaktion | mittel bis groß |
| Auth | Rollen-Rechte-JWT, Rechtekatalog, `PrivilegeHandler` | Entra + `Shared.Authorization` | groß |
| Datenschutz | `report.created` enthält `description` (Klasse 2); kein `[Sensitivity]`; `PersonnelNumber` an Meldungen, auch bei anonymer Erfassung | dünnes Event, Erasure | mittel |
| Zeit | `DateTime.UtcNow` in `Report.cs:32-33`, `Faq.cs:19`, `FaqImage.cs:14`, `IrsDbContext.cs:100`; `DateTimeOffset.UtcNow` in `PingController.cs:12` | `TimeProvider` | klein bis mittel |
| `/health` | ohne DB-Check | `/health/live`, `/health/ready` | klein |

**Unangetastet bleibt (Fachfunktionen out of scope):** `Domain/`, Report-/Faq-/Statistics-Contracts und -Controller, `ReportQueryService`, `FaqImageStorage`, RateLimiting, Serialization, Fachmigrationen und deren Tests.
**Umbau betrifft:** `Authentication/*`, `Services/LocationDirectory`, `Services/ReportEvents`, `Program.cs`, `ScopeContext`/Query-Filter in `IrsDbContext`, `LocationContracts` sowie Tests `PrivilegeTests`, `ScopeTests`, `ServiceClientTests`, `JwtKeysTests`, Fakes.
**Risiko:** Der Scope-Filter auf Reports ist mit dem Auth-Modell verzahnt. Ein Austausch von `ScopeContext` berührt die Fachabfragen und ihre Tests.

**Datenschutzeinschätzung (Entwurf, Klassifizierung ist mit dem DSB abzustimmen):** Klasse 2: `Report.Description`, `Comment`, `PriorActivity`, `NoticedHow`, `Device`, `NoticedAt` (+ `PersonnelNumber` im Zusammenhang), Statistik `by-personnel-number`. Freitext kann Gesundheitsangaben enthalten; das Plan-Verbot „keine Diagnosen“ ist technisch nicht abgesichert. Klasse 1: Filial-IDs, FAQ-Inhalte. Klasse 3: nicht erkennbar.

### 3.5 hr-tool-API → Workforce (vorbehaltlich Entscheidung 1.1)

**Ist:** Controller. Endpunkte: `schedules` (+ `matrix`, `shifts`), `absences`, `employees`, `absence-types`, `shift-patterns`, `workplaces`, `ping`; alle mit `[HasPrivilege<T>]` aus Rollen-Rechte, scopbar nach Filiale. Eine unmigrierte `InitialCreate`. Mitarbeitende, User und Filialen nur als ID ohne FK. CLI `import` mit eigenem xlsx-Leser (769 Mitarbeitende, 2.293 Pläne aus echten BC-Exporten, lokal in git-ignoriertem `docs/spreadsheets`). BC nicht angebunden. Offene Arbeit laut `todo-hr-tool-api.md`: Genehmigungsstatus-Werte, Berechtigungskonzept, Urlaubscockpit, Outbox, Cashier-API, Doku veraltet.

**Abwesenheitsdomäne gegen Phase 6:**

| Regel (Phase 6) | Ist |
| --- | --- |
| Krankmeldung ohne Freitext, AU-Flag, offenes Bis | `Remark` ist Freitext, kein AU-Flag, `DateTo` nicht nullable |
| Urlaub mit Genehmigungskette (`Shared.Approvals`, Vorgesetzte aus Access) | `ApprovalStatus` freier Text, Genehmigen über pauschales `absence:update`, nur Urlaub genehmigungspflichtig |
| Urlaubskonto, Restprüfung | keine Entity |
| Überlappungsregel inkl. Ausnahme Krank im Urlaub | nicht gefunden |
| Arbeitstage und Feiertage | nicht berechnet, `Days` kommt aus BC |
| Versionierung (an BC übertragen) | keine Versionsspalte |
| Grund nur für Person, Vorgesetzte, HR sichtbar; Kolleg:innen nur „abwesend“ | jeder mit `absence:read` sieht Art und Grund; keine „eigene Daten“-Routen |
| BC-Sync-Status, `EmployeeLeft`, Erasure, `AbsenceDetails`-Endpunkt | fehlt |

Weitere Punkte: Abwesenheitsart (z. B. `KRANK`, `MUTTERSCHUTZ`) im Klartext (Klasse 2); `Employee.Remark` Freitext; keine Klasse-3-Felder (Gehalt, Bank, Steuer) im Domain-Code gefunden (Grep auf bank/iban/salary/gehalt/steuer/geburt). `DateTime.UtcNow` in `HrToolDbContext.cs:197`, `AbsencesController.cs:69`, Entity-Initialisierern (`Absence`, `Employee`, `Schedule`, `Shift`), `PingController.cs:13`. Die Domänenregeln sind nicht von der Infrastruktur getrennt (kein `*.Core`).

| Thema | Aufwand |
| --- | --- |
| Architektur (Core/Infrastructure/Api, `Workforce.*`) | groß |
| Messaging/Outbox/Sync-Status | groß |
| Auth/`Shared.Authorization`, Identität auf EmployeeId statt Personalnummer/`sub` | groß bzw. mittel |
| Datenschutz (Sichtbarkeit, Freitext, Klassifizierung) | mittel |
| Fachregeln Phase 6 (Konto, Überlappung, Feiertage, Versionierung, Genehmigung) | groß |
| Dienstplanung/Schichten (laut Plan out of scope) | Entscheidung offen: einfrieren, auslagern, mitnehmen |

## 4. Abgleich mit „Verbindliche Architekturentscheidungen“ und „Datenschutz-Regeln“

| Abschnitt des Plans | Stand |
| --- | --- |
| Jede Datenart hat einen Besitzer | teils: Besitzgrenzen heute anders geschnitten (Mitarbeiter/Workplace in hr-tool, Login in Rollen-Rechte) |
| Kommunikationsregel 1–2 (Events, Commands) | fehlt |
| Regel 3 (synchrones HTTP nur im Ausnahmefall) | **weicht ab**: Rechteprüfung, Empfängerermittlung, Filialen laufen synchron pro Request |
| Regel 4 (Frontends direkt zur besitzenden API) | passt, soweit erkennbar |
| Regel 5 (BC nur über BC Integration) | **weicht ab** in Rollen-Rechte (`HttpBcGateway`, `BcStoreSync`); sonst passt |
| Zuverlässigkeit (Outbox, Inbox, Upsert, Sync-Status) | fehlt |
| Autorisierung (Entra, App Roles, lokal, `PermissionsChanged`) | weicht ab (siehe 2.) |
| Genehmigungen (`Shared.Approvals`) | fehlt |
| Sensibilitätsklassen, `[Sensitivity]`, Architekturtest | fehlt |
| Logging ohne Inhalte | weitgehend ok (keine Bodies gefunden, PIN nie im Log); Ausnahmen: AAD-Fehlerbody (BC), Push-Endpoint-URL (Benachr.); nicht vollständig geprüft |
| Push/Mail ohne Klasse-2-Inhalte | **weicht ab**: Push-Text enthält Meldungsfreitext |
| Outbox/Inbox-Aufbewahrung, Dead-Letter | fehlt |
| Löschung (`EmployeeDataErasureRequested`) | fehlt überall |
| Service-Bus-Zugriff ohne Shared Access Key | nicht anwendbar (kein Service Bus) |

## 5. Offene Fragen

### Entscheidungen für dich

1. **`hr-tool-API`:** Basis für die Workforce API (Umbau) oder nur Fachreferenz für einen Neubau? Was passiert mit Dienstplan/Schichten (out of scope laut Plan) und mit `Employee`/`Workplace`/`CashierProfile`?
2. **`IRS-API`:** Umbau der bestehenden Solution (Fachlogik unangetastet) oder neues Skelett `src/Portal` und IRS-API unverändert lassen?
3. **Rechtemodell in Rollen-Rechte:** Feingranulares Katalogmodell behalten oder auf 4 grobe Entra-Rollen + Scopes reduzieren? Wird die Scope-Dimension `report` weiter gebraucht?
4. **Auth-Umstellung auf Entra:** Migrationsstrategie für bestehende User/Passwörter und für das Angular-Frontend (nutzt heute die JWT-Endpunkte). Bleibt der BC-PIN-Login (`employees/verify`) im Zielbild, und wo?
5. **Übergangsphase:** Portal, Benachrichtigungen und hr-tool sind heute angebunden. Sollen die alten synchronen Endpunkte (`/api/me/privileges`, `recipients`) parallel weiterlaufen?
6. **`report.created`:** Wohin wandern die Meldungsregeln (Kategorie, Schwere, Titel, Link, Empfängerermittlung)? Soll die IRS-API künftig selbst Empfänger bestimmen und `SendNotification` schicken? Gilt `description` im Event als Klasse-2-Verstoß (dünnes Event)?
7. **Nested `.git`-Ordner** in vier Diensten: Historie behalten (z. B. per Subtree/History-Import) oder verwerfen?
8. **Echte Personaldaten** (769 Mitarbeitende) in den hr-tool-Exporten und im Import: auf welche Umgebungen dürfen sie? (Mit dem Datenschutzbeauftragten zu klären.)
9. **Bestehende Push-Abos und Einstellungen** liegen noch in der Portal-DB und müssen übernommen werden (VAPID-Schlüsselpaar unverändert).
10. **Persönliche Daten in der IRS-API:** Bleibt die anonyme Erfassung mit `PersonnelNumber` bestehen? Soll `PersonnelNumber` auf EmployeeId umgestellt werden (Eingriff in Fachdaten und Migration)? Doctrine-Altdatenbank vs. „ein Schema pro Dienst“.

### BC-Datenmodell (an BC-Verantwortliche)

- Mitarbeiterkarte: Feldliste, Eintritts-/Austrittsdatum, Hauptfiliale, SystemId, `lastModifiedDateTime`. Bekannt ist nur `employeeName` aus `Zeiterfassung.GetStatus`.
- Filialen: bekannt `No.`, `Name` (LSC Store); unbekannt SystemId, Änderungszeitstempel, Paginierung der Codeunit.
- Abwesenheiten (`SyncAbsenceToBc`): kein Endpunkt/Feld bekannt; Upsert über externe ID, Versionsfeld, Fehlerformat bei Validierung.
- Zugang: SOAP-Codeunits vs. Standard-API/OData/API Pages; uneinheitliche Antwortformate (JSON im `return_value`, bei anderen Codeunits XML).
- Änderungserkennung: Webhooks verfügbar oder nur Polling? Gewünschtes Intervall.
- Drosselung: Liefert BC 429 mit `Retry-After`? Limits pro Sekunde?
- Genehmigungsstatus-Werte (nur „Vorgesetzter akzeptiert“ belegt); müssen `SONDERURLAUB`, `UNBEZ. U`, `URLAUBSANTRAGSZEITR` genehmigt werden?
- Urlaubsanspruch: Quelle (BC, manuell, DATEV). Positionen für Filialleitung/Bezirksleitung/HR.

### Noch nicht geklärt aus dem Plan

Wolverine-Version und Lizenz (Phase 1), Datenklassifizierung und Aufbewahrungsfristen (DSB), Betriebsrat, Arbeitstage und Feiertage/Bundesländer.

## 6. Eingesetzte NuGet-Pakete (alle Dienste, gleiche Versionen)

Hauptprojekte: `EFCore.NamingConventions 10.0.1`, `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12`, `Microsoft.AspNetCore.OpenApi 10.0.12` (außer BC-Anbindung/Benachrichtigungen), `Microsoft.EntityFrameworkCore.Design 10.0.12`, `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`. Zusätzlich: Benachrichtigungen `Lib.Net.Http.WebPush 3.3.1`; Rollen-Rechte `BCrypt.Net-Next 4.2.0`, `Konscious.Security.Cryptography.Argon2 1.3.1`. BC-Anbindung hat keine Pakete (und kein EF/Npgsql).
Tests: `xunit 2.9.3`, `xunit.runner.visualstudio 4.0.1`, `Microsoft.NET.Test.Sdk 18.10.1`, `Microsoft.AspNetCore.Mvc.Testing 10.0.12`, `Microsoft.Extensions.TimeProvider.Testing 10.10.0`, `Testcontainers.PostgreSql 4.16.0` (nicht in BC-Anbindung).
**Nicht vorhanden:** Wolverine, Azure Service Bus, `Microsoft.Identity.Web`, OpenTelemetry/Aspire, WireMock.Net, Verify, NetArchTest/ArchUnitNET, FluentAssertions/Shouldly. Für Phase 1 sind diese laut Plan („Technischer Stack“) freigegeben, Wolverine erst nach Lizenzbestätigung.

## 7. Grenzen dieser Analyse

- Nichts wurde gebaut oder ausgeführt; Testzahlen sind Attributzählungen.
- Das Logging wurde stichprobenartig geprüft (`grep` nach Logger-Nutzung), nicht vollständig.
- Aufrufer der synchronen Endpunkte stammen aus den jeweiligen `CLAUDE.md`, nicht aus einer Quelltextverfolgung über alle Dienste.
- `.env`-Dateien wurden bewusst nicht geöffnet.

**Stopp-Punkt:** Bericht liegt vor. Bitte entscheiden, welche Umbauten in Phase 1 bis 5 einfließen (insbesondere Fragen 1 bis 7 in Abschnitt 5). Bis dahin beginne ich keine Phase 1.
