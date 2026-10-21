# Rollen-Rechte

ASP.NET-Web-API auf .NET 10 (`net10.0`) für **Anmeldung, Benutzer, Rollen, Rechte, Scopes und Filialen** des Unternehmensportals. Die anderen
Anwendungen (Portal = `Portal`, HR-Tool = `hr-tool-API`) melden hier ihren Rechtekatalog an und lösen die Rechte ihrer User auf. BC-Zugriff läuft
ausschließlich über den Dienst `BC-Anbindung`. Projektdatei: `Rollen-Rechte.csproj`, Solution: `Rollen-Rechte.slnx`. Datenbank: eigenes PostgreSQL über
EF Core (Npgsql), Spalten in snake_case. Das Konzept der vier Bausteine steht in `Portal/.claude/references/PLATTFORM-SCHNITTSTELLEN.md`.

**Stand:** Der Code stammt aus der IRS-API und ist dort entfernt. Das Portal ist angebunden: Es prüft die Tokens dieses Dienstes, löst Rechte und Reichweite über `GET /api/me/privileges` auf, meldet seinen Katalog an und holt die Filialen über `GET /api/apps/portal/locations`.

## Ordnerstruktur

```
Rollen-Rechte/
├── Authentication/     JWT (RS256 bzw. HS256-Fallback) + Refresh-Token, bcrypt/argon2id, Login-Throttling, BC-Anmeldung (Personalnummer + PIN, verschlüsselt),
│                       Rechte: Katalog, Prüfung, Scope, Schutzregeln (AclGuard), Registrierung der Anwendungen (AppKey)
├── Commands/           CLI-Befehle (create-user, assign-role, sync-stores, decrypt-pin)
├── Common/             Api (Envelope, Fehler-Handler, Validierung, Paging), RateLimiting
├── Contracts/          DTOs
├── Controllers/        Auth, Me, Users, Roles, Scopes, Privileges, CustomPrivileges, Apps, Locations (+Archived), Ping
├── Data/               AccessDbContext, Value-Converter, AdminSeeder, EF-Migrationen
├── Domain/Entities/    User, AclRole, AclUserRole(+Scope), Scope(+Member), CustomPrivilege, AppPrivilege, Location, RefreshToken
├── Serialization/      JSON-Optionen (camelCase, ISO-8601)
├── Services/           HttpBcGateway (Client der BC-Anbindung), Filialabgleich (BcStoreSync)
└── Rollen-Rechte.Tests/ xUnit: Functional/ (HTTP gegen den Dienst + Postgres), Unit/, Infrastructure/ — per <Compile Remove> vom Haupt-Build ausgeschlossen
```

## Konventionen

- Root-Namespace `Rollen_Rechte`. Antworten unter `/api` im Envelope `{success, data}` bzw. `{success, message, errors[{field, message}]}`; `204` ohne Body.
  Ausnahme: Login/Refresh antworten ohne Envelope (`token`, `refresh_token`).
- Validierungsfehler → `422` (`[Required(ErrorMessage = ...)]`, deutsche Meldungen); fachliche Fehler per `ApiException`. PATCH ist partiell (fehlend/`null` = unverändert).
- **401 nur für „nicht angemeldet/Token ungültig/User deaktiviert“** — nie für etwas anderes, sonst loggt das Frontend aus. Fehlende Berechtigung ist **403** mit den
  vordefinierten Meldungen aus `AuthMessages` (einsehen/erstellen/bearbeiten/löschen).
- Soft-Delete (`active`/`archived`); UUIDv7 für IDs. Zeitstempel UTC im Code, `timestamp(0) without time zone` in der DB.
- Das JWT trägt `username`, `sub` (die User-ID, damit Anwendungen ohne eigene Benutzer-Tabelle den User erkennen), `exp`, `iat` (ohne `roles`); Rechte stehen nie im Token, sondern kommen aus `GET /api/me/privileges` (sonst wären sie bis zum Ablauf veraltet).
  Der User wird bei jedem Request per `username` geladen (Aktiv-Prüfung).
- Keine DB-Defaults: Defaults stehen nur in den C#-Entities (neue Non-Nullable-Spalten prüfen, der Generator setzt `""`/`false`/`0`).

## Rechte (ACL)

- Jede Controller-Route ist ausdrücklich `[AllowAnonymous]` (Login/Refresh, Ping, öffentliche Filialliste, `PUT /api/apps/{appKey}/catalog` mit eigenem Schlüssel),
  `[Authorize(Policy = AuthPolicies.Authenticated)]` (`/api/me/*`) oder `[HasPrivilege<Entity>(CrudAction.X)]`. Ein globaler Filter verlangt für Routen ohne Angabe die Anmeldung.
- **Rechte** haben das Format `ressource:aktion`; in Rollen sind zusätzlich `*` und `bereich:*` erlaubt. **Keine Rolle = keine Rechte.** Rechte eines Users = Vereinigung der Rechte
  aller **gültigen** Zuweisungen (`acl_user_role`, mit `valid_from`/`valid_to`), bei jedem Request frisch aus der DB.
- **Katalog (generiert):** Jede Entity mit `[PrivilegeResource("Label")]` im `AccessDbContext` bekommt automatisch `read/create/update/delete` (Key = Klassenname in snake_case,
  `Key =`/`Actions =`/`DeleteLabel =`/`ScopedActions =`/`DefinesReach =` steuern es; `[PrivilegeAction]` ergänzt Zusatzaktionen). Neue Entity = Attribut setzen, Routen mit
  `[HasPrivilege<Entity>(…)]` versehen. Die Tests lesen die Routen aus den Endpunkt-Metadaten und verlangen, dass Katalog und Routen einander genau entsprechen.
- **Rechte der Anwendungen:** Portal und HR-Tool melden ihren Katalog beim Start an (`PUT /api/apps/{appKey}/catalog`, Schlüssel `Apps__Keys__<appKey>`, Header `X-Api-Key`):
  Bereiche, Rechte, `scopable`, `definesReach`. Er ersetzt den bisherigen dieser Anwendung (Tabelle `app_privilege`), ein Recht gehört genau einer Anwendung, Rechte dieses Dienstes
  und eigene Rechte sind gesperrt. Durchgesetzt werden sie von der jeweiligen Anwendung; hier werden sie nur vergeben. Rechte, die nicht mehr registriert sind, wirken nicht (Rollen
  behalten den Schlüssel, `/api/me/privileges` löst ihn nicht mehr auf).
- **Empfängerermittlung:** `POST /api/apps/{appKey}/recipients` `{privilege, locationIds, reportIds, userIds}` → `{userIds}`: die aktiven User, die das Recht für eine der Filialen/Einzelobjekte haben
  (uneingeschränkte Zuweisungen immer; eingeschränkte nur bei scopbaren Rechten und wenn eine Filiale/das Objekt im Scope oder die Heimatfiliale passt; nur gültige Zuweisungen). Ohne Filialen/Objekte zählen nur
  uneingeschränkte. `userIds` schränkt die Kandidaten ein. Genutzt vom Dienst `Benachrichtigungen` (Schlüssel `Apps__Keys__notifications`), um zu wissen, wer eine Meldung sehen darf (`RecipientResolver`).
- **Eigene Rechte (Laufzeit):** `custom_privilege` (`bereich:aktion`, unveränderlich), verwaltet über `/api/custom-privileges`; `PrivilegeCatalogService` liefert den `PrivilegeSnapshot`
  (generiert + registriert + eigen, 30 s Cache, bei Änderung sofort invalidiert). Erzwingen: `[HasCustomPrivilege("bereich:aktion")]` oder die UI per `/api/me/privileges`.
- **Verwaltung (UI-Grundlage):** `GET /api/privileges` (Katalog mit `custom`/`scopable`), `GET /api/me/privileges` (Rechte, Reichweite je Recht, Rollen), `/api/roles`,
  `/api/scopes`, `GET/PUT /api/users/{id}/roles` (PUT ersetzt die Zuweisungen), `PUT /api/users/{id}/home-location`, `/api/custom-privileges`, `/api/locations`.
- **Scopes (Reichweite):** Die Rolle sagt, *was* jemand darf, der Scope, *wo*. Ein Scope ist eine benannte Menge von Mitgliedern in einer Dimension (`location`, `report`; neue Dimension =
  neuer Wert in `ScopeDimensions`). Er hängt an der **Zuweisung**: `scoped` (false = uneingeschränkt; true = nur die Scopes, bei leerer Liste nichts — fail-closed), `home_scope`
  (zusätzlich die Heimatfiliale `users.home_location_id`), `valid_from`/`valid_to`. Mehrere Zuweisungen mit demselben Recht: Vereinigung. Scopbar ist nur, was freigegeben ist
  (`ScopedActions`, `Scopable`); sonst zählen nur uneingeschränkte Zuweisungen. Die **Anwendungen filtern ihre Daten selbst** (Meldungen, Dienstpläne, …); dieser Dienst filtert Filialen
  und User (`InScope`) und liefert den Scope. Dimension `report` (Einzelmeldung): die ID wird ohne Prüfung gespeichert, die Meldungen führt das Portal.
- **Schutzregeln** (`AclGuard`, durch `SecurityTests` festgenagelt): Man vergibt/ändert/entzieht nur Rechte, Reichweite und Laufzeit, die man selbst wirksam hat; Rolle ändern/löschen
  verlangt die Rechte **uneingeschränkt**; Passwort, Deaktivieren/Löschen und Heimatfiliale eines Users nur, wenn man dessen Rechte abdeckt (sonst Account-Übernahme); Scope-Mitglieder nur
  im Rahmen der eigenen Datenreichweite; die Rolle „Administrator“ ist eingebaut (auch in anderer Schreibweise gesperrt); eine zugewiesene Rolle/ein zugewiesener Scope lässt sich
  nicht löschen; es bleibt immer ein aktiver, **dauerhafter**, uneingeschränkter Administrator.
- Noch nicht umgesetzt: Die Regel „eine Einzelmeldung der eigenen Region darf freigegeben werden“ braucht eine Rückfrage an das Portal (hier gilt nur eine exakt vorhandene Freigabe).

## Betrieb / Konfiguration

- Lokale Konfiguration über `.env` (Vorlage: `.env.example`), beim Start geladen; echte Umgebungsvariablen haben Vorrang.
- **JWT:** Das RSA-Schlüsselpaar des Symfony-Backends übernehmen (`Jwt__PrivateKey…`, `Jwt__PublicKey…`, `Jwt__Passphrase`), dann bleiben ausgestellte Tokens gültig. Ohne RSA
  gilt der HS256-Fallback `Jwt__Key` (mind. 32 Zeichen). Portal und HR-Tool validieren das Token mit dem öffentlichen Schlüssel.
- **BC-Anmeldung:** `BcGateway__BaseUrl`/`BcGateway__ApiKey` (Dienst „BC-Anbindung“) plus `Auth__PinEncryptionKey` (Base64, 32 Bytes, nie wechseln). Ablauf von `POST /api/auth/login`:
  Besteht der Username nur aus Ziffern (Personalnummer) oder ist der User BC-geführt, fragt der Dienst die BC-Anbindung. Erfolg → User wird bei Bedarf **ohne Rolle** angelegt
  (`Auth__BcAutoCreateUsers`), bcrypt-Hash und verschlüsseltes PIN aktualisiert. BC lehnt ab → BC-geführter User abgelehnt, rein lokaler User prüft lokal. BC-Anbindung/BC nicht
  erreichbar → lokale Prüfung gegen den Hash des letzten BC-Logins (Offline-Modus). Das PIN steht nie im Log; `make decrypt-pin NAME=<personalnummer>` liest es.
- **Filialen:** Stammdaten hier (`locations`, Store-Nr. = `store_code`); Abgleich über die BC-Anbindung beim Start (`Locations__SyncOnStartup`) und per `make sync-stores`. Legt an und
  benennt um, archiviert/löscht nie. Portal und HR-Tool bekommen die Filialen von hier. Anwendungen holen alle Filialen (auch archivierte) per `GET /api/apps/{appKey}/locations` (Header `X-Api-Key`, wie die Katalog-Anmeldung); öffentlich ist nur die Liste der aktiven (`GET /api/locations`).
- **CORS** (`Cors__AllowedOriginPattern`/`Cors__AllowedOrigins__N`), **Proxy** (`ForwardedHeaders__KnownProxies__N`); Rate-Limits und Login-Throttle liegen im Speicher (pro Instanz).
- Ersten Administrator anlegen: `make create-user NAME=<name>` oder beim Start per `Seed__AdminUsername`/`Seed__AdminPassword` (beide vergeben die Rolle „Administrator“).
  Bestehende User ohne Rolle: `make assign-role NAME=<name> [ROLE=<rolle>]`.

## Datenbank / Migrationen

- `InitialCreate` erzeugt das gesamte Schema (neue Datenbank, eigener Postgres auf Port 5433 lokal). Bereits gepushte Migrationen nie ändern — Folgemigration anlegen.
- Produktion: Schemaänderungen laufen von Hand (SQL aus `dotnet ef migrations script`); kein Migrationsschritt im Deploy. Die Daten der bisherigen Benutzer-Tabellen
  (Symfony-Schema/IRS-API) müssen beim Umstellen übernommen werden — offen, siehe Konzeptpapier.

## Tests

- `make test` bzw. `dotnet test Rollen-Rechte.Tests` — **Docker muss laufen** (Testcontainers startet `postgres:17-alpine`).
- `Functional/` startet den kompletten Dienst per `WebApplicationFactory<Program>` mit `FakeBcGateway` statt BC-Anbindung; jeder Test nutzt eine eigene Fake-Client-IP
  (Rate-Limiter) und legt seine Daten per ORM mit zufälligen Namen an — Tests verlassen sich nie auf leere Tabellen.
- Neues Verhalten zuerst mit einem Test festnageln. Sicherheitsrelevante Regeln gehören in `SecurityTests`.

## Build / Deployment

- `make build`, `make run` (Port 5302), `make db-up`, `make migrate`, `make test`, `make api-up` (Container, Port 8100), `make publish` (`app.zip`), `make deploy` (`az webapp deploy`;
  `RESOURCE_GROUP`/`APP_NAME` im Makefile prüfen). Für Updates ohne Ausfall: Deployment-Slot mit Tausch; `/health` ist der Health-Check.
