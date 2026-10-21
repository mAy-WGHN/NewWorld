# Benachrichtigungen

ASP.NET-Web-API auf .NET 10 (`net10.0`): der **zentrale Dienst für Benachrichtigungen** des Unternehmensportals (Web Push, später E-Mail/Postfach). Die Anwendungen (Portal, HR-Tool, …)
melden nur noch „es ist etwas passiert“; dieser Dienst entscheidet, **wer** davon erfährt, und stellt zu. Projektdatei: `Benachrichtigungen.csproj`, Solution: `Benachrichtigungen.slnx`.
Datenbank: eigenes PostgreSQL über EF Core (Npgsql), Spalten in snake_case.

Teil von fünf Bausteinen (Repos unter `source/repos`): **Portal**, **Rollen-Rechte** (Login, Rollen, Rechte, Scopes, Filialen), **BC-Anbindung**, **Benachrichtigungen**
(dieser Dienst), HR-Tool (`hr-tool-API`). Das Konzept steht in `Portal/.claude/references/PLATTFORM-SCHNITTSTELLEN.md`.

**Stand:** Der Code stammt aus der IRS-API und ist dort entfernt. Das Portal sendet `report.created` hierher (`Sources__Keys__portal`). Die bestehenden Push-Abos der Browser müssen mit **demselben VAPID-Schlüsselpaar** weiterlaufen.

## Ordnerstruktur

```
Benachrichtigungen/
├── Authentication/     JWT-Prüfung (das Token stellt Rollen-Rechte aus; hier nur geprüft, User = Claim sub) und API-Schlüssel der Absender (Sources__Keys__<absender>)
├── Common/Api/         Response-Envelope, ApiException, Fehler-Handler, 422-Validierung
├── Contracts/          DTOs (Push, Einstellungen, Ereignisse)
├── Controllers/        Events (Eingang der Ereignisse), PushSubscriptions (/api/me), NotificationSettings (/api/me), Ping
├── Data/               NotificationsDbContext, Value-Converter, EF-Migrationen
├── Domain/             Entities (PushSubscription, UserNotificationSettings, ProcessedEvent), Enums
├── Services/           PushNotificationSender (Web Push), ReportCreatedHandler (die Regeln), RecipientDirectory (Client von Rollen-Rechte)
└── Benachrichtigungen.Tests/ xUnit: Functional/ (HTTP gegen den Dienst + Postgres), Unit/, Infrastructure/ (Fakes)
```

## Ablauf

1. Ein Absender sendet `POST /api/events` (Header `X-Api-Key` = sein Schlüssel aus `Sources__Keys__<absender>`): `{ id, type, occurredAt?, data }`. Die `id` vergibt der Absender; ein wiederholtes
   Ereignis (gleiche ID) wird ignoriert (`duplicate: true`), die Zustellung ist also „mindestens einmal“ und der Absender darf wiederholen.
2. Typ `report.created`, Daten `{ reportId, category, severity, description, locationIds[] }` (nur Fakten; Titel und Link baut dieser Dienst).
3. **Empfänger:** Kandidaten sind die User mit mindestens einem Push-Gerät. Der Dienst fragt **Rollen-Rechte** (`POST /api/apps/{appKey}/recipients`, Schlüssel `Access__ApiKey`), wer davon
   `report:read` für eine der Filialen der Meldung hat (eine genügt; nur aktive User, gültige Zuweisungen, Scopes beachtet). Dann zählen die **Einstellungen** des Users (Kategorie, Mindest-Schwere
   bei Bugs, Wochentag in Europe/Berlin) — dieselben Regeln wie im bisherigen Portal; Virus-Meldungen gehen immer raus. Keine Einstellungs-Zeile = Standardwerte (Bug ab high, alle Wochentage).
4. **Zustellung:** Web Push (VAPID, aes128gcm, Urgency high), Payload im Angular-Service-Worker-Format (nicht ändern). Abos, die der Push-Dienst mit 404/410 meldet, werden gelöscht; Versandfehler werden nur
   geloggt. Antwort: `{"data":{"duplicate":bool,"recipients":n},"success":true}`.
5. Ist Rollen-Rechte nicht erreichbar → **503**, das Ereignis wird **nicht** als verarbeitet gemerkt, der Absender sendet es später noch einmal. Ungültige Daten → 422 (Wiederholen hilft nicht).

## Schnittstellen für die Oberfläche (JWT von Rollen-Rechte)

- `GET /api/me/push-public-key`, `POST/DELETE /api/me/push-subscriptions` (wie bisher im Portal; ein bestehender Endpoint geht an den zuletzt anmeldenden User über — geteilter Browser).
- `GET/PATCH /api/me/notification-settings` (eigene Einstellungen). Das Einstellen **für andere** User (bisher über die User-Verwaltung im Portal) fehlt noch: Es braucht die
  Rechteprüfung dieses Dienstes gegen Rollen-Rechte (Katalog anmelden, `GET /api/me/privileges` mit dem Token auflösen).
- Das Token muss die User-ID im Claim `sub` tragen (stellt Rollen-Rechte seit dieser Umstellung aus); alte Symfony-Tokens ohne `sub` → 401, neu anmelden. Der Dienst hat keine Benutzer-Tabelle:
  ein deaktivierter User wird erst bei der Empfängerermittlung (nur aktive) aussortiert.

## Betrieb / Konfiguration

- Lokale Konfiguration über `.env` (Vorlage: `.env.example`), beim Start geladen; echte Umgebungsvariablen haben Vorrang.
- `Jwt__PublicKey…` bzw. `Jwt__Key` (wie bei Rollen-Rechte), `Vapid__PublicKey/PrivateKey/Subject` (**unverändert aus dem Portal**), `Frontend__BaseUrl`, `Cors__…`,
  `Access__BaseUrl/AppKey/ApiKey` (Rollen-Rechte; dort `Apps__Keys__notifications`), `Sources__Keys__<absender>`.
- Rate-Limits gibt es nicht (der Dienst ist für die eigenen Programme und die angemeldeten User gedacht; der Eingang ist durch API-Schlüssel geschützt).

## Datenbank / Migrationen

- `InitialCreate` erzeugt das Schema (neue Datenbank, lokal Port 5434): `push_subscriptions`, `user_notification_settings`, `processed_event`. User und Rollen sind nur IDs (kein FK).
- Die bestehenden Push-Abos und Einstellungen liegen noch in der Datenbank des Portals und müssen beim Umstellen übernommen werden (offen).
- Produktion: Schemaänderungen von Hand (SQL aus `dotnet ef migrations script`). Bereits gepushte Migrationen nie ändern.

## Konventionen

- Root-Namespace `Benachrichtigungen`. Envelope `{success, data}` bzw. `{success, message, errors[{field, message}]}`; Validierungsfehler 422, Texte deutsch. 401 nur für „nicht angemeldet/Schlüssel falsch“.
- Neuer Ereignistyp: Handler in `Services/`, Eintrag im `switch` des `EventsController`, Daten-DTO in `Contracts/`, Tests. Das Ereignisformat ist ein Vertrag mit den Absendern: nur abwärtskompatibel ändern.
- Ereignisse tragen Fakten und IDs, keine personenbezogenen Daten; Texte baut dieser Dienst.

## Tests

- `make test` bzw. `dotnet test Benachrichtigungen.Tests` — **Docker muss laufen** (Testcontainers startet `postgres:17-alpine`).
- Fakes: `FakePushSender` (Versand), `FakeRecipientDirectory` (Rollen-Rechte: wer darf, „nicht erreichbar“), `SettableClock` (Wochentage). Der echte Sender ist gegen einen Fake-HTTP-Handler getestet.

## Build / Deployment

- `make build`, `make run` (Port 5303), `make db-up`, `make migrate`, `make test`, `make api-up` (Container, Port 8200), `make publish`, `make deploy`. Für Updates ohne Ausfall: Deployment-Slot mit Tausch; `/health` ist der Health-Check.
