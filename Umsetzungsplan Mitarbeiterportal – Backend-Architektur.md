# Umsetzungsplan Mitarbeiterportal – Backend-Architektur

Ursprünglich 20.10.2026 · @Mehmet · überarbeitet und gekürzt 2026-11-10

> **Stand:** Die Phasen 0 bis 7 sind gebaut. Dieses Dokument ist jetzt die **Begründung und die Leitplanken** hinter dem Backend, keine Bauanleitung mehr. **Verbindlich ist die Root-`CLAUDE.md`** (Entscheidungen 1 bis 47, Nachrichtenkatalog, aktuelle Regeln); bei Widerspruch gilt sie. Die ausführliche Originalfassung mit allen Phasenaufgaben steht in der Git-Historie (Commit `b5a8759`).

## Auftrag und Kontext

Backend eines Mitarbeiterportals, eines HR-Tools und der Integrationsdienste für einen Filialbetrieb. Kern ist ein **vertikaler Durchstich**: Eine Abwesenheit (Krankmeldung, Urlaub) wird erfasst, nach Business Central (BC) synchronisiert und die zuständige Filialleitung benachrichtigt. Alles ist so gebaut, dass weitere Fachbereiche nach demselben Muster folgen.

- **BC** ist alleiniger Master für Stammdaten (Filialen, Mitarbeiterkarte).
- **Dienstplanung, Nichtverfügbarkeiten, Zeiterfassung:** das neue System ist operativ führend, wird aber vollständig nach BC gespiegelt, damit das Tool ersetzbar bleibt.
- Die Bestandsdienste liegen unter `Archiv/` als fachliche Referenz. Entwickelt wird nur unter `src/`.

## Architektur

APIs nach Fachlichkeit, nicht nach Anwendung. **Jede Datenart hat genau einen Besitzer, nur dieser schreibt;** andere halten höchstens eine schreibgeschützte Kopie.

| Dienst | Besitzt | Genutzt von |
| --- | --- | --- |
| BC Integration | Sync-Status, ID-Mapping (BC-SystemId ↔ interne GUID), Fehlerprotokoll | allen anderen Diensten, nie von Frontends |
| Access | Rollen, Rechte, Scopes, Zuständigkeiten, Vertretungen | allen APIs (Events + lokale Kopie) |
| Notifications | Vorlagen, Zustellstatus, Präferenzen | allen Fach-APIs per Command |
| Workforce | Abwesenheiten, Urlaubs- und Arbeitszeitkonten, Mitarbeiterkarte, später Dienstpläne und Zeiten | Frontend, spätere HR API |
| Portal | FAQ, Meldungen, Statistik, später weitere Portalinhalte | Frontend |
| HR API (nicht gebaut) | Recruiting, Lohnarten, DATEV-Exporte | HR-Frontend |

**Kommunikation**
1. **Events** (Service-Bus-Topics) melden Zustandsänderungen mit minimalem Inhalt. Jede Subscription ist eine dokumentierte Freigabe.
2. **Commands** (Queues) beauftragen genau einen Empfänger.
3. **Synchrones HTTP zwischen Diensten** nur, wenn eine sofortige Antwort nötig und eine Kopie unpassend ist, oder für autorisierten Abruf sensibler Details. Jeder solche Endpunkt hat eine eigene Service Role.
4. **Frontends** sprechen direkt mit der besitzenden API und haben keinen Service-Bus-Zugang.
5. **BC** wird nur über BC Integration angesprochen. Kein anderer Dienst hat BC-Zugangsdaten oder BC-Client-Code.

**Zuverlässigkeit**
- Zustandsänderung und ausgehende Nachricht in **derselben DB-Transaktion** (Outbox). Empfänger verarbeiten idempotent (Inbox); Service Bus liefert „at least once“.
- BC-Schreibvorgänge sind Upserts über die interne GUID als externe ID. Ältere Versionen werden verworfen.
- Der Besitzer einer Entität speichert deren BC-Sync-Status (`Pending`, `Synced`, `Failed`) und aktualisiert ihn über `BcSyncSucceeded`/`BcSyncFailed`.

**Autorisierung** (so entschieden, abweichend vom ursprünglichen Plan; Details in `CLAUDE.md`, Entscheidungen 3, 13, 18)
- **Personen:** Anmeldung mit Personalnummer und BC-PIN, Access stellt das Token aus. Alle Rechte kommen aus dem feingranularen ACL in Access (Rolle = Rechte `ressource:aktion`, Scopes nach Filialen/Personen, Zuständigkeiten je Thema). Keine groben Entra-Rollen.
- **Verteilung:** Access veröffentlicht `PermissionsChanged`; jeder Dienst prüft **lokal** über `Shared.Authorization` und ist bei Access-Ausfall mit dem letzten Stand weiter nutzbar.
- **Dienst zu Dienst:** Schlüsselpaar je Dienst, signierte Kurzzeit-Tokens, Service Role je Fähigkeit (`docs/service-auth.md`).
- **Entra** nur dort, wo ein externer Dienst es verlangt: BC (Cloud), Service-Bus-Zugriff bei Azure-Hosting und der zweite Faktor für die Lohnabrechnung.

**Identität:** Systemweiter Schlüssel für Personen ist allein die interne **EmployeeId** (vergeben von BC Integration). IDs von Identitätsanbietern stehen nicht in Fachdaten.

**Genehmigungen:** Über die Bibliothek `Shared.Approvals`, Daten in der DB der einbindenden API. Kein eigener Approval-Service.

## Datenschutz

Jede Nachricht und jeder Endpunkt hat eine Sensibilitätsklasse; im Zweifel gilt die strengere.

| Klasse | Beispiele | Auf dem Bus | Details abrufen |
| --- | --- | --- | --- |
| 1 – Organisationsdaten | Filiale, Personalnummer, Name, Hauptfiliale, Position, Sollstunden | voller Zustand | – |
| 2 – Sensible Fakten | Abwesenheitsgrund, Zeitkorrekturen, Incident-Inhalte | nur neutralisiert (ohne Grund) oder dünnes Event (nur IDs) | autorisierter HTTP-Abruf beim Besitzer |
| 3 – Hochsensibel | Gehalt, Bank, Steuer/SV, Bewerbungen, Diagnosen | **nie** | nur HR API, über BC Integration bzw. DATEV |

- **Keine Diagnosen** oder medizinischen Details. Die hochgeladene AU ist ein eigenes, besonders geschütztes Dokument (Entscheidung 20).
- Jede Nachricht trägt `[Sensitivity(...)]`; ein Architekturtest verlangt Klassifizierung und verbietet Klasse 3.
- **Logging:** nie Nachrichteninhalte oder Request-Bodies; erlaubt sind Typ, Nachrichten-ID, Korrelations-ID, Entitäts-IDs.
- **Push und Mail:** nie Klasse-2-Inhalte in Push-Texten oder Mail-Betreffs; Push-Text generisch, Inhalt wird nach dem Öffnen per API geladen.
- **Löschung:** Jeder Dienst mit Mitarbeiterkopien reagiert auf `EmployeeDataErasureRequested` (pro Dienst ein Integrationstest).
- **Outbox/Inbox:** Verarbeitetes wird nach 7 Tagen gelöscht. Dead-Letter-Queues haben dieselbe Zugriffsbeschränkung wie die Fachdaten.
- **Service Bus:** kein Shared Access Key; Senderechte nur auf eigene Topics/Queues, Leserechte nur auf freigegebene Subscriptions, je Subscription ein Zweck-Kommentar.
- Die Klassifizierung ist ein Entwurf und noch mit dem Datenschutzbeauftragten abzustimmen.

## Stack und Struktur

.NET 10, ASP.NET Core Minimal APIs, PostgreSQL mit EF Core (**ein Schema pro Dienst, keine Cross-Schema-Joins**), Azure Service Bus über `Azure.Messaging.ServiceBus` mit eigener Outbox/Inbox in `Shared.Infrastructure` (kein Wolverine, Entscheidung 8), .NET Aspire (AppHost, ServiceDefaults), OpenTelemetry. Frontend: Angular in `Frontend/` (eigene Toolchain).

```
/src    AppHost, ServiceDefaults, Shared (Authorization, Approvals, Messaging, Infrastructure, Testing),
        BcIntegration, Access, Notifications, Workforce, Portal   je: .Api | .Core | .Infrastructure | .Contracts
/tests  je Dienst .Core.Tests, .Api.Tests, .Contracts.Tests; Architecture.Tests; AppHost.Tests; Fixtures
/infra  Bicep (nur Service-Bus-Topologie) und Emulator-Konfiguration
/Archiv Bestandsdienste (Referenz)
```

**Abhängigkeiten:** `*.Core` ohne Infrastruktur (kein EF Core, Service Bus, HTTP-Client) und gegliedert nach Features; `*.Contracts` nur mit `Shared.Messaging`; ein Dienst referenziert andere nur über deren `*.Contracts`. Architekturtests sichern das ab.

**Tests:** xUnit mit Shouldly, `FakeTimeProvider`, `WebApplicationFactory` und Testcontainers (kein EF-In-Memory), WireMock.Net als BC-Ersatz (Antworten unter `tests/Fixtures/Bc`), Verify für Nachrichtenschemas, NetArchTest, Aspire Testing für End-to-End.

## Nachrichten

Der verbindliche Katalog steht in `CLAUDE.md`. Regeln dafür:
- Jede Nachricht hat Nachrichten-ID (Idempotenz), Korrelations-ID und Zeitstempel aus `TimeProvider`.
- Contracts sind `sealed record`s mit primitiven Typen, `DateOnly`, `DateTimeOffset`, Enums (mit `Unknown = 0`, String-Serialisierung); XML-Doku und Verify-Snapshot pro Nachricht; Konsumenten ignorieren unbekannte Felder.
- Weiterentwicklung nur **additiv**; eine brechende Änderung ist eine neue Nachricht `...V2`, die alte läuft parallel.
- `SendNotification.Data` darf Klasse-2-Inhalte nur enthalten, wenn `InApp` der einzige Kanal ist.
- Neue Nachrichten oder Konsumenten nur nach Rückfrage.

## Arbeitsweise

- Phasen in Reihenfolge, jede endet an einem Stopp-Punkt (zusammenfassen: gebaut, Tests grün, offene Fragen) mit Freigabe. Kleine, nachvollziehbare Commits.
- **Nie ohne Rückfrage:** vorhandenen Code löschen oder grundlegend umschreiben, von den Architekturentscheidungen oder Datenschutzregeln abweichen, Pakete außerhalb des Stacks aufnehmen, BC-Felder oder -Tabellen erfinden (Platzhalter `// TODO(BC-Modell): ...`), an der Inventur-Anwendung etwas ändern.
- **Konventionen:** Code, Bezeichner und Commits englisch; ausschließlich `TimeProvider` (nie `DateTime.Now`/`UtcNow`); Nullable aktiv, Warnungen als Fehler; keine Secrets im Code oder in `appsettings*.json`.
- **Definition of Done:** Tests zuerst und grün; `dotnet build` ohne Warnungen und `dotnet test` für die ganze Solution grün; keine Inhalte oder personenbezogenen Daten in Logs; neue Nachricht im Katalog, klassifiziert, mit Snapshot; je neuem Endpunkt ein positiver und ein negativer Berechtigungstest; offene Annahmen als `TODO(...)` markiert und gemeldet.
- **Pflichtfälle für Zeitlogik:** Monats- und Jahreswechsel, Sommer-/Winterzeit, Schaltjahr (29. Februar), gesetzlicher Feiertag, Zeitraum über Mitternacht.

## Phasen: Stand

| Phase | Ergebnis | Stand |
| --- | --- | --- |
| 0 Bestandsaufnahme | `docs/phase-0-bestandsaufnahme.md`, Entscheidungen in `CLAUDE.md` | fertig |
| 1 Grundgerüst | AppHost, ServiceDefaults, Shared-Bibliotheken, Architekturtests | fertig |
| 2 Contracts | alle Nachrichten mit Klassifizierung und Snapshots | fertig |
| 3 BC Integration | einziger BC-Zugang, Stammdatenabgleich, Upsert, Fehlerklassen (gegen WireMock) | fertig, echte BC-Sandbox noch nicht |
| 4 Access | feingranulares ACL, Zuständigkeiten, Login, `PermissionsChanged` | fertig |
| 5 Notifications | `SendNotification`, Vorlagen, Präferenzen, InApp | fertig, Push/Mail nur als Fake |
| 6 Workforce Abwesenheiten | Krankmeldung mit AU, Urlaub mit Genehmigung, Konten | fertig und erweitert (Mitarbeiterkarte, Nicht-Verfügbarkeit, Arbeitszeitkonto) |
| 7 Vertikaler Durchstich | End-to-End-Tests mit Aspire und WireMock, Service-Bus-Topologie, `docs/architektur.md` | fertig |

Seit dem Plan ergänzt (Entscheidungen 41 bis 47): Portal-Fachlogik, Änderungsprotokoll, zweiter Faktor, Rollen-Austauschdatei, Zuständigkeit für alle.

## Offene Entscheidungen

Nicht selbst entscheiden, Platzhalter nutzen und melden. Die vollständige, aktuelle Liste steht in `CLAUDE.md`.

| Thema | Frage |
| --- | --- |
| BC-Stammdaten und Workforce-Modell | Exakte Feldliste, Tabellen und API-Pages für Abwesenheiten und weitere Daten; Upsert über externe ID möglich? Zeiterfassungs-Schnittstelle: `docs/bc-zeiterfassung-schnittstelle.md` |
| BC-Änderungserkennung | Polling zuerst (Entscheidung 25), Intervall und spätere Webhooks offen |
| Datenklassifizierung | Bestätigung der drei Klassen und Aufbewahrungsfristen durch den Datenschutzbeauftragten (alle Dienste, auch Audit-Log) |
| Hosting | Umgebung offen; Transport ist deshalb austauschbar |
| Mitarbeiterkarte | Besitzer, Passwort/PIN, Kassenfelder, Konten, Nachricht nach BC (`docs/mitarbeiterkarte.md`) |
| Feiertage | Mariä Himmelfahrt (15.8.) in bayerischen Gemeinden |
| AU | ClamAV-Betrieb in Produktion, Schlüsselverwaltung und -rotation (`docs/krankmeldung-au.md`) |

Seit dem Plan **entschieden**: kein Wolverine (8), Positionen/Rollen zunächst manuell (22), Urlaubsanspruch in Workforce (24), Feiertage und Arbeitstage (17, 23), kein Entra für Personen (13, 18), kein Betriebsrat (40). Damit entfallen im Plan die frühere Entra-Einrichtung, die Entra-Lizenzfrage und die Personalnummer im Entra-Objekt.

## Außerhalb des Scopes

Inventur-Anwendung (nur leerer Ordner `Inventory/`); Deployment, CI/CD und Azure-Ressourcen außer der Service-Bus-Topologie; die HR API (Recruiting, DATEV); Dienstplanung, Zeiterfassung und Schichttausch; weitere Portal-Fachfunktionen (News, Schulungen, Berichtsheft, Incidents, Kassen-/Losbestände); echte Push- und Mail-Zustellung; automatische Löschfristen. Wird so etwas berührt, nur Schnittstelle oder Platzhalter vorbereiten.
