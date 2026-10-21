# Portal

ASP.NET-Web-API auf .NET 10 (`net10.0`): das **Portal** des Unternehmensportals — Meldungen (Incident Reports), Statistik und FAQ. Ersatz für das Symfony-Backend; das Angular-Frontend
und die Datenbank der Meldungen bleiben. Projektdatei: `Portal.csproj`, Solution: `Portal.slnx`. Datenbank: PostgreSQL über EF Core (Npgsql).

Das Portal ist **einer von fünf Bausteinen** (Repos unter `source/repos`, jeweils ASP.NET 10, eigene Datenbank). Es kennt keine Benutzer, Rollen oder Filialen selbst:

| Baustein | Aufgabe | Das Portal nutzt es für |
|---|---|---|
| **Rollen-Rechte** | Anmeldung, Benutzer, Rollen, Rechte, Scopes, Filialen | Token ausstellen/prüfen, Rechte und Reichweite auflösen, Filialen (Namen, Archiv-Status), Rechtekatalog anmelden |
| **BC-Anbindung** | einziger Weg zu Business Central | indirekt (über Rollen-Rechte) |
| **Benachrichtigungen** | Web Push, Empfänger, Einstellungen | meldet dorthin „neue Meldung“ (`POST /api/events`) |
| **hr-tool-API** | HR-Tool | noch nicht angebunden |

Das Konzept steht in `.claude/references/PLATTFORM-SCHNITTSTELLEN.md` (Abschnitt 10: Stand der Umsetzung).

## Ordnerstruktur

Diese Struktur ist verbindlich und wird bei neuem Code eingehalten.

```
Portal/
├── Authentication/     JWT-Prüfung (Token stammt von Rollen-Rechte), Rechteprüfung gegen Rollen-Rechte (AccessDirectory, PrivilegeHandler),
│                       Rechtekatalog (generiert aus Entities) und dessen Anmeldung bei Rollen-Rechte (CatalogRegistration), Scope
├── Common/
│   ├── Api/            Response-Envelope, ApiException, Fehler-Handler, 422-Validierung, Paging-Helfer
│   └── RateLimiting/   Rate-Limit-Policies (global, report_creation)
├── Contracts/          DTOs für Requests/Responses (inkl. Projektions-Expressions)
├── Controllers/        Reports (+Archived), Statistics, Faqs, FaqImages, Ping
├── Data/               DbContext, Value-Converter, EF-Migrationen
├── Domain/
│   ├── Entities/       Report, ReportLocation, Faq, FaqImage
│   └── Enums/          Meldungs-Enums
├── Serialization/      zentrale JSON-Optionen (camelCase, Lowercase-Enums, ISO-8601 mit Offset)
├── Services/           Meldungs-Abfragen, Filialverzeichnis (LocationDirectory), Ereignisse an Benachrichtigungen (ReportEvents), FAQ-Bildablage
├── Portal.Tests/       xUnit: Functional/ (HTTP gegen echte API + Postgres), Unit/, Infrastructure/ (Fakes für Rollen-Rechte und Benachrichtigungen)
│                       — per <Compile Remove> vom Haupt-Build ausgeschlossen
├── Dockerfile          Multi-Stage-Image (Port 8080); docker-compose.yaml: Postgres + Profil "app" (API auf :8000)
├── tmp/                lokale Referenzdokumente zum Original (git-ignoriert)
└── .claude/references/ Konzept-/Kontrakt-Dokumente (Kontext, keine 1:1-Spezifikation)
```

## Konventionen

- Root-Namespace: `Portal` (Unterordner = Unternamespace, z. B. `Portal.Controllers`).
- Alle Antworten unter `/api` im Envelope `{success, data}` bzw. `{success, message, errors[{field, message}]}`; `204` ohne Body.
- Validierungsfehler → `422`; Pflichtfelder per `[Required(ErrorMessage = ...)]` mit deutscher Meldung
  (implizite Pflicht für Non-Nullable ist abgeschaltet). Fachliche Fehler per `ApiException`.
- **401 nur für „nicht angemeldet/Token ungültig/User deaktiviert“** — nie für etwas anderes, sonst loggt das Frontend den Admin aus.
- PATCH ist partiell: fehlend/`null` = unverändert. Controller erben von `ApiControllerBase`.
- Soft-Delete (`archived`) bei Meldungen, hartes Löschen bei `Faq` und `FaqImage`.
- `FaqImage` nutzt bewusst UUIDv4 (die ID ist die Zugriffskontrolle), alle anderen UUIDv7.
- Zeitstempel: UTC im Code, `timestamp(0) without time zone` in der DB (Converter in `Data/`).
  `noticedAt` wird wie im Original **ohne Umrechnung** gespeichert (Wanduhrzeit des Clients) — nicht „verbessern“.
- Enums liegen als `VARCHAR(255)` mit Kleinbuchstaben-Wert in der DB (wie im bestehenden Schema).
- Ein Dienst, der ausfällt, darf das Portal nicht mitreißen: Öffentliche Routen (Meldung anlegen, FAQ lesen) laufen weiter; geschützte Routen antworten bei nicht erreichbarem
  Rollen-Rechte mit **503** (fail-closed, nie „erlaubt“).

## Entscheidungen gegenüber dem Symfony-Original

- `GET /api/reports` und `GET /api/reports/{id}` sind **geschützt** (im Original öffentlich).
- `POST /api/reports` setzt `source` serverseitig immer auf `employee`.
- Doppelte `locationIds` werden dedupliziert (Original: 404).
- `GET /api/reports` filtert Archiviertes weiterhin nur bei `archived=false`.
- Ein ungültiges/abgelaufenes Token auf einer **öffentlichen** Route führt hier nicht zu 401 (die Route läuft anonym weiter);
  das Original liefert dort vermutlich 401 (ungeprüft). Das Frontend sendet den Token an alle API-URLs.
- JSON ohne Unicode-Escapes (`+`, Umlaute lesbar); `Retry-After` ist bei 429 immer mindestens 1 und wird per CORS freigegeben.
- Kein `UseHttpsRedirection`: TLS terminiert die Plattform (Azure), ein Redirect würde Cross-Origin-POSTs stören.
- **Ausgelagert:** Anmeldung, Benutzer, Rollen, Rechte, Scopes, Filialen (→ Rollen-Rechte) und Push/Benachrichtigungen (→ Benachrichtigungen) liegen nicht mehr hier. Das Frontend ruft
  diese Endpunkte bei den jeweiligen Diensten auf (die Pfade `/api/auth`, `/api/users`, `/api/roles`, `/api/locations`, `/api/me/…` gibt es im Portal nicht mehr).

## Betrieb / Konfiguration

- Lokale Konfiguration über `.env` (Vorlage: `.env.example`), wird beim Start in `Program.cs` geladen; echte Umgebungsvariablen haben Vorrang.
- **JWT:** Das Token stellt Rollen-Rechte aus; das Portal prüft nur die Signatur: `Jwt__PublicKey`/`Jwt__PublicKeyPath` (RS256, PEM) bzw. Fallback `Jwt__Key` (HS256, mind. 32 Zeichen, gleicher
  Wert wie dort). Issuer/Audience sind nicht gesetzt. Ob der User noch aktiv ist und was er darf, klärt Rollen-Rechte (siehe „Rechte“).
- **Rollen-Rechte:** `Access__BaseUrl`, `Access__AppKey` (Default `portal`), `Access__ApiKey` (dort: `Apps__Keys__portal`), `Access__CacheSeconds` (Default 10).
- **Benachrichtigungen:** `Notifications__BaseUrl`, `Notifications__ApiKey` (dort: `Sources__Keys__portal`). Ohne beide wird nichts gemeldet (Warnung im Log).
- **CORS:** `Cors__AllowedOriginPattern` (Regex, im Original `CORS_ALLOW_ORIGIN`) und/oder `Cors__AllowedOrigins__N`. Auch Fehlerantworten tragen die Header.
- **Proxy:** `X-Forwarded-*` wird ausgewertet; ohne `ForwardedHeaders__KnownProxies__N` wird jedem Proxy vertraut (ForwardLimit 1).
  Rate-Limits liegen im Speicher (pro Instanz); bei mehreren Instanzen wäre ein geteilter Store nötig.
- Uploads (FAQ-Bilder) liegen unter `Uploads__FaqPath` (Default `var/uploads/faq`, außerhalb des Webroots). Im Azure App Service
  nach `/home/...` legen (persistent); bestehende Bilder des Original-Backends dorthin kopieren, sonst 404.
- OpenAPI-Dokument: `GET /api/doc.json` (nur in Development), Security-Scheme `bearerAuth`.

## Rechte (Durchsetzung hier, Vergabe bei Rollen-Rechte)

- Jede Controller-Route ist ausdrücklich `[AllowAnonymous]` (`POST /api/reports`, `GET /api/faqs`, FAQ-Bilder, Ping) oder `[HasPrivilege<Entity>(CrudAction.X)]` / `[HasPrivilege(Privileges.X)]`.
  Ein globaler Filter verlangt für Routen ohne Angabe mindestens die Anmeldung; bewusst keine `FallbackPolicy` (sie machte aus unbekannten Routen 401 statt 404).
- **Ablauf je geschützter Anfrage:** Das Token wird lokal auf Signatur geprüft (kaputt/fehlend → 401, ohne Rollen-Rechte zu fragen). Dann fragt der `PrivilegeHandler` Rollen-Rechte
  (`GET /api/me/privileges` mit dem Token des Users, 10 s je Token zwischengespeichert): hat der User das Recht, und in welcher Reichweite (Scope)? Lehnt Rollen-Rechte das Token ab (User
  deaktiviert) → 401; nicht erreichbar → 503; Recht fehlt → 403 mit vordefinierter Meldung je Aktion (`AuthMessages`). Ein Recht ohne gelieferte Reichweite gilt als nicht vergeben (fail-closed).
  Ein Entzug wirkt damit nach spätestens `Access__CacheSeconds`.
- **Katalog (generiert):** `Authentication/PrivilegeCatalog.cs` erzeugt die Rechte dieses Dienstes aus den Entities: Eine Entity mit `[PrivilegeResource("Label")]`, die als `DbSet` im
  `IrsDbContext` steht, bekommt `<key>:read`, `:create`, `:update`, `:delete` (Key = Klassenname in snake_case; `Actions =` schränkt ein, `DeleteLabel =` benennt „Löschen“ um,
  `ScopedActions =` gibt Aktionen für Scopes frei, `[PrivilegeAction("x", "Label")]` ergänzt Zusatzaktionen). Bereiche ohne Entity (`statistic`) stehen in `CustomAreas`.
  **Neue Entity = Attribut setzen**, dann die Routen mit `[HasPrivilege<Entity>(…)]` versehen. Die Tests lesen die Routen aus den Endpunkt-Metadaten und verlangen, dass Katalog und Routen
  einander genau entsprechen. Rechte aktuell: `report:read/update/delete` (Anlegen ist öffentlich), `faq:read/create/update/delete`, `statistic:read`.
- **Anmeldung des Katalogs:** `CatalogRegistration` meldet den Katalog beim Start per `PUT /api/apps/portal/catalog` an Rollen-Rechte (`Access__ApiKey`), damit man die Rechte dort in
  Rollen vergeben kann. Läuft im Hintergrund und versucht es alle 30 s wieder; das Portal startet auch ohne Rollen-Rechte. Ohne Anmeldung kennt Rollen-Rechte die Portal-Rechte nicht
  (eine Rolle mit nur `*` hat sie trotzdem erst nach der Anmeldung).
- **Scope (Reichweite):** Der Scope zum verlangten Recht (`ScopeContext`) filtert Meldungen und damit die Statistik über einen globalen EF-Filter im `IrsDbContext`
  („eine Filiale genügt“; Einzelmeldungen für Zweitmeinungen). Ein Datensatz außerhalb des Scopes liefert **403** mit der vordefinierten Meldung (existiert er nicht: 404); in Listen fehlt er.
  `FindAsync` wendet keine Filter an — für Meldungen deshalb `FirstOrDefaultAsync`. Außerhalb jeder Anfrage (anonyme Meldung) ist kein Scope gesetzt = uneingeschränkt.
  Meldungen mit mehreren Filialen ändert man eingeschränkt nur in seinem Teil (fremde Filialen bleiben erhalten). Vergeben, befristen und delegieren (Heimatfiliale, Vertretung, Schutzregeln) macht Rollen-Rechte.
- **Filialen:** Das Portal speichert an Meldungen nur die Filial-IDs (`report_location`, ohne Fremdschlüssel). Namen, Store-Code und Archiv-Status holt `LocationDirectory` von Rollen-Rechte
  (`GET /api/apps/portal/locations`, 60 s zwischengespeichert; bei Ausfall gilt die zuletzt bekannte Liste, ohne Liste → 503). Beim Anlegen/Ändern einer Meldung wird eine unbekannte ID einmal
  frisch nachgeschlagen (höchstens alle 5 s), bevor es 404 gibt — eine Filiale kann gerade neu angelegt worden sein.
- **Benachrichtigungen:** Nach dem Speichern einer Meldung sendet `HttpReportEvents` `report.created` (`reportId`, `category`, `severity`, `description`, `locationIds`) an Benachrichtigungen; die
  Ereignis-ID ist die Meldungs-ID (erneutes Senden ist unschädlich). Zwei Versuche, Fehler werden nur geloggt: **ohne Zwischenablage geht das Ereignis bei längerem Ausfall verloren** (Outbox wäre der nächste Schritt).
  Wer benachrichtigt wird, entscheidet Benachrichtigungen (Empfänger über Rollen-Rechte, Einstellungen, Wochentage).

## Datenbank / Migrationen

- `InitialCreate` + `AlignEnumColumnLengths` erzeugen das Schema für **neue** Datenbanken (Dev/Test/Container); die Migrationen bis `AddScopesAndAssignmentValidity` legten auch Benutzer,
  Rollen, Filialen und Push-Tabellen an, die inzwischen anderen Diensten gehören. `RemoveIdentityAndNotificationTables` räumt sie wieder weg und entfernt den Fremdschlüssel
  `report_location → locations`. **ACHTUNG Datenverlust:** In der Produktions-DB (Doctrine-Schema) erst anwenden, wenn Benutzer, Filialen und Push-Abos in Rollen-Rechte/Benachrichtigungen
  übernommen sind; bis dahin nur den Fremdschlüssel von Hand entfernen (`ALTER TABLE report_location DROP CONSTRAINT <name>;`) und die Tabellen stehen lassen.
- **Bestehende Produktions-DB (Doctrine-Schema) nicht migrieren:** vor dem ersten Start die Migrationen als angewendet
  markieren (Zeilen in `__EFMigrationsHistory`), solange Symfony die Rückfallebene ist. Die Doctrine-Tabelle `doctrine_migration_versions` bleibt unangetastet.
  Schemaänderungen nur mit passender EF-Migration + Handarbeit.
- Bereits gepushte Migrationen nie ändern — stattdessen eine Folgemigration anlegen.
- Keine DB-Defaults: Defaults stehen nur in den C#-Entities (neue Non-Nullable-Spalten prüfen, der Generator setzt `""`/`false`/`0`).

## Tests

- `make test` bzw. `dotnet test Portal.Tests` — **Docker muss laufen** (Testcontainers startet `postgres:17-alpine`;
  unter Windows wird der Docker-Desktop-Pipe automatisch erkannt). Bei laufender lokaler API (sperrt `bin/Debug`) mit `--configuration Release` testen.
- `Functional/` startet die komplette API per `WebApplicationFactory<Program>`; jeder Test nutzt eine eigene Fake-Client-IP
  (`X-Forwarded-For`), damit sich die Rate-Limiter nicht überlappen. Hilfsdaten werden direkt per ORM angelegt, mit zufälligen
  Namen — Tests verlassen sich nie auf leere Tabellen. Statistik-Tests nutzen eigene Monate (2001–2004).
- Die anderen Dienste sind ersetzt: `FakeAccessDirectory` (Rechte/Scopes je Token, „Token unbekannt“, „nicht erreichbar“), `FakeLocationDirectory` (Filialen), `FakeReportEvents` (gemeldete
  Ereignisse). Die echten HTTP-Clients sind in `Unit/ServiceClientTests` gegen einen Fake-HTTP-Handler getestet. Tokens sind echt (HS256) signiert (`NewToken`).
- Neues Verhalten zuerst mit einem Test festnageln; Verhalten des Originals nicht stillschweigend „verbessern“.

## Build / Deployment

- `make build`, `make run`, `make test`, `make db-up`, `make migrate`.
- `make api-up` startet die API als Container auf Port 8000 (Profil `app` in `docker-compose.yaml`, Dev-Werte). Die anderen Dienste müssen erreichbar sein (`Access__BaseUrl`, `Notifications__BaseUrl`).
- `make publish` erzeugt `app.zip` (`dotnet publish -c Release`), `make deploy` lädt es per `az webapp deploy` hoch
  (`RESOURCE_GROUP`/`APP_NAME` im Makefile prüfen). Kein Migrationsschritt im Deploy.
- Achtung: Das alte PHP-Projekt (`incident-report-database-1`) belegt ggf. Port 5432 und 8000; `make db-up`/`make api-up` scheitern dann.
