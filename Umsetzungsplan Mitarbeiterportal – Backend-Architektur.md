# Umsetzungsplan Mitarbeiterportal – Backend-Architektur

Oct 20, 2026 · @Mehmet

## Auftrag und Kontext

Du baust das Backend eines Mitarbeiterportals, eines HR-Tools und der zugehörigen Integrationsdienste auf. Ziel dieses Plans ist ein lauffähiger, getesteter vertikaler Durchstich: Eine Abwesenheit wird erfasst, nach Business Central (BC) synchronisiert und die zuständige Filialleitung wird benachrichtigt. Alles, was dafür nötig ist, wird so gebaut, dass weitere Fachbereiche später nach demselben Muster ergänzt werden können.

Das Unternehmen ist ein Filialbetrieb. Microsoft Business Central ist das ERP und bleibt für Stammdaten (Filialen, Mitarbeiterkarte) der alleinige Master. Für Dienstplanung, Nichtverfügbarkeiten und Zeiterfassung wird das neue System operativ führend, alle Daten werden aber vollständig nach BC gespiegelt, damit das Tool später ersetzbar bleibt.

Es gibt bereits angefangenen Code: eine BC-Anbindung, einen Rollen-und-Rechte-Service und einen Benachrichtigungsservice. Diese werden nicht verworfen, sondern in Phase 0 analysiert und an diesen Plan angepasst.

Lies diesen Plan vollständig, bevor du die erste Datei anlegst. Die Abschnitte "Arbeitsregeln", "Verbindliche Architekturentscheidungen" und "Datenschutz-Regeln" gelten für jede Phase.

## Arbeitsregeln

Arbeite die Phasen strikt in der angegebenen Reihenfolge ab. Jede Phase endet an einem Stopp-Punkt: Du fasst zusammen, was gebaut wurde, welche Tests grün sind und welche Fragen offen sind, und wartest auf Freigabe, bevor du die nächste Phase beginnst.

### Vorgehen innerhalb einer Phase

1. Lies die Aufgaben und Abnahmekriterien der Phase.
2. Prüfe, welcher vorhandene Code betroffen ist.
3. Schreibe zuerst die Tests für die Fachlogik, dann die Implementierung.
4. Führe `dotnet build` und `dotnet test` für die gesamte Solution aus. Eine Phase gilt erst als fertig, wenn beides fehlerfrei durchläuft.
5. Hake erledigte Aufgaben ab und berichte am Stopp-Punkt.

### Nie ohne Rückfrage

- Vorhandenen Code löschen oder grundlegend umschreiben. Schlage die Änderung vor und warte auf Zustimmung.
- Von einer Entscheidung in "Verbindliche Architekturentscheidungen" oder "Datenschutz-Regeln" abweichen, auch wenn es technisch einfacher wäre.
- Neue NuGet-Pakete außerhalb der in "Technischer Stack" genannten aufnehmen.
- Felder oder Tabellen für BC erfinden. Wo das BC-Datenmodell unklar ist, arbeite mit klar markierten Platzhaltern (`// TODO(BC-Modell): ...`) und liste sie am Stopp-Punkt auf.
- Irgendetwas an der bestehenden Inventur-Anwendung ändern.

### Konventionen

- Code, Bezeichner und Commit-Messages auf Englisch, fachliche Begriffe dürfen deutsch kommentiert werden.
- Keine `DateTime.Now`/`DateTime.UtcNow`/`DateTimeOffset.UtcNow`; ausschließlich `TimeProvider`.
- Nullable Reference Types aktiv, Warnings als Fehler (`TreatWarningsAsErrors`).
- Keine Secrets, Connection Strings oder Keys im Code oder in `appsettings*.json`. Lokal über Aspire bzw. User Secrets, später Key Vault.
- Kleine, nachvollziehbare Commits pro Aufgabe.
- Wenn du dir unsicher bist, frag nach, statt eine Annahme stillschweigend umzusetzen.

## Verbindliche Architekturentscheidungen

Die APIs sind nach Fachlichkeit geschnitten, nicht nach Anwendung oder Benutzergruppe. Jede Datenart hat genau einen Besitzer; nur dieser schreibt sie. Alle anderen halten höchstens eine schreibgeschützte Kopie.

| Dienst | Besitzt (schreibt) | Hält als Kopie | Wird genutzt von |
| --- | --- | --- | --- |
| BC Integration Service | Sync-Status, ID-Mapping (BC-SystemId ↔ interne ID), Outbox, Fehlerprotokoll | – | allen anderen Diensten, nie von Frontends |
| Access Service | Filial-Scopes, Hierarchie, Vertretungen, Sonderrechte | Mitarbeitende, Filialen | allen APIs (über Events + lokalen Cache) |
| Notification Service | Vorlagen, Zustellstatus, Präferenzen, Push-Gerätetokens | – | allen Fach-APIs per Command |
| Workforce API | Dienstpläne, Schichten, Schichttausch, Stempelungen, Zeitkorrekturen, Abwesenheiten, Urlaubskonten, Monatsabschlüsse | Mitarbeitende, Filialen | Portal-Frontend, HR-Frontend, HR API |
| Portal API | News, FAQ, Glossar, Filialordner, Schulungen, Quiz, On-/Offboarding, Berichtshefte, Aufgaben, Incidents, Erfassung Kassen-/Losbestände | Mitarbeitende, Filialen, aggregierte Verkaufsstatistiken | Portal-Frontend |
| HR API | Recruiting, Lohnarten-Mapping, DATEV-Exportläufe | Mitarbeitende | HR-Frontend |

In diesem Plan werden nur BC Integration, Access, Notification und Workforce (Teilbereich Abwesenheiten) umgesetzt, dazu ein minimales Gerüst der Portal API. HR API ist außerhalb des Scopes.

### Kommunikationsregeln

1. **Events** (Service Bus Topics) melden Zustandsänderungen, mit dem minimalen Inhalt, den die freigegebenen Konsumenten brauchen. Jede Subscription ist eine explizite, dokumentierte Freigabe.
2. **Commands** (Service Bus Queues) beauftragen genau einen Empfänger, z. B. eine BC-Synchronisation oder eine Benachrichtigung.
3. **Synchrones HTTP zwischen APIs** nur, wenn eine Antwort sofort gebraucht wird und eine Kopie unpassend ist, oder um sensible Details autorisiert abzurufen. Jeder solche Endpunkt ist durch eine eigene App Role geschützt.
4. **Frontends** sprechen direkt mit der besitzenden API, nie über eine andere API als Durchreiche. Frontends haben keinen Zugang zu Service Bus.
5. **BC** wird ausschließlich über den BC Integration Service angesprochen. Keine andere API hat BC-Zugangsdaten oder BC-Client-Code.

### Zuverlässigkeit

- Zustandsänderung und ausgehende Nachricht werden in derselben DB-Transaktion gespeichert (Outbox).
- Empfänger verarbeiten idempotent (Inbox bzw. gespeicherte Nachrichten-IDs). Service Bus stellt "at least once" zu.
- BC-Schreibvorgänge sind Upserts über eine externe ID (die interne GUID), damit Wiederholungen keine Duplikate erzeugen.
- Der Besitzer einer Entität speichert deren BC-Sync-Status selbst (`Pending`, `Synced`, `Failed`) und aktualisiert ihn über `BcSyncSucceeded`/`BcSyncFailed`.

### Autorisierung

- Authentifizierung über Microsoft Entra ID. Grobe Rollen (Mitarbeiter, Filialleitung, HR, Geschäftsführung) kommen als App Roles im Token.
- Feingranulare Scopes (welche Filialen, welche Mitarbeitenden) verwaltet der Access Service. Er veröffentlicht `PermissionsChanged`; jede API hält einen lokalen Cache.
- Die Prüfung findet lokal in jeder API statt, über `Shared.Authorization`. Der Access Service ist nie im Request-Pfad. Fällt er aus, arbeiten die APIs mit dem letzten bekannten Stand weiter.
- Service-zu-Service-Aufrufe: Client Credentials mit Managed Identity, App Roles pro Fähigkeit (z. B. `Workforce.AbsenceDetails.Read`).

### Identität und Portabilität

Systemweiter Schlüssel für Personen ist ausschließlich die interne **EmployeeId**, vergeben vom BC Integration Service. Die ID des Identitätsanbieters (Entra-`oid`) wird nirgends in Fachdaten gespeichert, sondern nur im Access Service als Zuordnung "externe Identität → EmployeeId". Anbieterspezifische Claims werden zentral in `Shared.Authorization` auf interne Claims abgebildet; Token-Beschaffung läuft über ein konfigurierbares `TokenCredential`. Ein Wechsel des Identitätsanbieters darf nur Konfiguration, `Shared.Authorization` und die Zuordnungstabelle betreffen.

Für die Phasen 0 bis 7 wird keine echte Entra-Einrichtung benötigt. Lokal und in Tests gilt der Test-Auth-Handler.

### Genehmigungen

Genehmigungsketten (Urlaub, später Berichtsheft, Incidents, Schichttausch) laufen über die Bibliothek `Shared.Approvals`. Jede API bindet sie ein und speichert ihre Genehmigungen in ihrer eigenen Datenbank. Es gibt keinen eigenen Approval-Service.

## Datenschutz-Regeln

Jede Nachricht und jeder Endpunkt wird einer von drei Sensibilitätsklassen zugeordnet. Die Klasse bestimmt, was übertragen werden darf. Im Zweifel gilt die strengere Klasse.

| Klasse | Beispiele | Auf dem Bus erlaubt | Abruf von Details |
| --- | --- | --- | --- |
| 1 – Organisationsdaten | Filiale, Personalnummer, Name, Hauptfiliale, Position, Arbeitsverhältnis-Typ, Sollstunden | vollständiger Zustand im Event | – |
| 2 – Sensible Fakten | Abwesenheitsgrund (Krank/Urlaub), Zeitkorrekturen, Quizergebnisse, Incident-Inhalte | nur neutralisiert (z. B. "nicht verfügbar", ohne Grund) oder dünnes Event (nur IDs) | autorisierter HTTP-Abruf beim Besitzer mit eigener App Role |
| 3 – Hochsensibel | Gehalt, Bankverbindung, Steuer-/SV-Daten, Bewerbungsunterlagen, Diagnosen | nie | nur HR API, direkt über BC Integration bzw. DATEV |

### Konkrete Vorgaben

- Diagnosen oder medizinische Details werden nirgends gespeichert. Eine Krankmeldung besteht aus Zeitraum, Art "Krank" und optional dem Vermerk, ob eine AU vorliegt.
- Jede Nachrichten-Klasse bekommt im Contracts-Projekt ein Attribut `[Sensitivity(SensitivityClass.X)]`. Ein Architekturtest stellt sicher, dass keine Nachricht der Klasse 3 existiert und jede Nachricht klassifiziert ist.
- **Logging:** Nachrichteninhalte und Request-Bodies werden nie geloggt. Erlaubt sind Nachrichtentyp, Nachrichten-ID, Korrelations-ID, Entitäts-IDs. Das gilt auch für Messaging-Bibliothek und OpenTelemetry-Konfiguration.
- **Push und Mail:** Inhalte der Klasse 2 erscheinen nie in Push-Texten oder Mail-Betreffs. Push-Text ist generisch ("Neue Mitteilung im Portal"), der Inhalt wird nach dem Öffnen über die API geladen.
- **Outbox/Inbox:** Verarbeitete Einträge werden nach 7 Tagen gelöscht (konfigurierbar).
- **Dead-Letter-Queues:** werden in der Infrastrukturdefinition mit gleicher Zugriffsbeschränkung wie die Fachdaten versehen.
- **Kopien und Löschung:** Jede API, die Mitarbeiterdaten kopiert, reagiert auf `EmployeeDataErasureRequested` mit Löschen bzw. Anonymisieren aller personenbezogenen Daten dieser Person. Dafür gibt es pro API einen Integrationstest.
- **Service Bus Zugriff:** Kein Shared Access Key. Jeder Dienst bekommt per Managed Identity nur Senderechte auf eigene Topics/Queues und Leserechte auf die für ihn freigegebenen Subscriptions. Jede Subscription trägt in der Infrastrukturdefinition einen Kommentar mit ihrem Zweck.

Die Klassifizierung ist ein Entwurf und wird noch mit dem Datenschutzbeauftragten abgestimmt (siehe "Offene Entscheidungen"). Baue sie so, dass eine Änderung der Klasse an genau einer Stelle passiert.

## Technischer Stack und Zielstruktur

Alle Dienste laufen auf ASP.NET Core (.NET 10) mit PostgreSQL und Azure Service Bus und werden lokal über .NET Aspire orchestriert.

| Bereich | Wahl | Hinweis |
| --- | --- | --- |
| Runtime | .NET 10, ASP.NET Core Minimal APIs | Controller nur, wo vorhandener Code sie schon nutzt |
| Datenbank | PostgreSQL, EF Core mit Npgsql | ein Schema pro Dienst, keine Cross-Schema-Joins |
| Messaging | Azure Service Bus, Wolverine (Outbox/Inbox mit EF Core + PostgreSQL) | vor Einführung Lizenz und aktuelle Version prüfen und am Stopp-Punkt bestätigen lassen |
| Lokale Orchestrierung | .NET Aspire AppHost + ServiceDefaults | Service-Bus-Emulator und PostgreSQL als Container |
| Auth | Microsoft Entra ID, `Microsoft.Identity.Web` | in Tests ersetzt durch Test-Auth-Handler |
| Beobachtbarkeit | OpenTelemetry über Aspire ServiceDefaults | später Application Insights |
| Unit-Tests | xUnit, FluentAssertions oder Shouldly, `Microsoft.Extensions.TimeProvider.Testing` | vorhandene Wahl im Bestandscode übernehmen |
| Integrationstests | `WebApplicationFactory`, Testcontainers (PostgreSQL, Service-Bus-Emulator) | keine EF-In-Memory-Provider |
| BC-Simulation | WireMock.Net | Antworten als Dateien unter `tests/Fixtures/Bc` |
| Vertragstests | Verify (Snapshot-Tests) |  |
| Architekturtests | NetArchTest.Rules oder ArchUnitNET |  |

### Ordnerstruktur (Monorepo)

```
/src
  /AppHost                      Aspire AppHost
  /ServiceDefaults              Aspire ServiceDefaults
  /Shared
    Shared.Authorization        Policies, Scope-Handler, Berechtigungs-Cache
    Shared.Approvals            Genehmigungs-Engine (Bibliothek)
    Shared.Messaging            Konventionen, Outbox-Setup, Sensitivity-Attribut
    Shared.Testing              Test-Auth-Handler, Builder, Testcontainer-Fixtures
  /BcIntegration
    BcIntegration.Api
    BcIntegration.Core
    BcIntegration.Infrastructure
    BcIntegration.Contracts
  /Access          (gleiche Aufteilung)
  /Notifications   (gleiche Aufteilung)
  /Workforce       (gleiche Aufteilung)
  /Portal          (gleiche Aufteilung)
/tests
  /<Dienst>.Core.Tests
  /<Dienst>.Api.Tests
  /<Dienst>.Contracts.Tests
  /Architecture.Tests
  /Fixtures
/infra                          Bicep (nur Service-Bus-Topologie in diesem Plan)
```

### Regeln für die Projektabhängigkeiten

- `*.Core` kennt keine Infrastruktur: kein EF Core, kein Service Bus, kein HTTP-Client.
- `*.Contracts` hat keine Abhängigkeiten außer `Shared.Messaging` (für das Sensitivity-Attribut).
- Ein Dienst darf von einem anderen Dienst ausschließlich dessen `*.Contracts` referenzieren.
- `*.Core` ist intern nach Features gegliedert (z. B. `Absences/`, `ShiftPlanning/`), nicht nach technischen Schichten.

## Event- und Command-Katalog

Diese Nachrichten sind für den Plan vorgesehen. Neue Nachrichten oder neue Konsumenten werden erst nach Rückfrage ergänzt.

| Nachricht | Art | Herausgeber | Konsumenten | Klasse | Inhalt (Kern) |
| --- | --- | --- | --- | --- | --- |
| `EmployeeChanged` | Event | BC Integration | Workforce, Portal, Access | 1 | EmployeeId, Personalnummer, Name, Hauptfiliale, Position, Arbeitsverhältnis-Typ, Sollstunden, Ein-/Austrittsdatum |
| `EmployeeHired` | Event | BC Integration | Portal, Access | 1 | EmployeeId, Eintrittsdatum, Hauptfiliale |
| `EmployeeLeft` | Event | BC Integration | Workforce, Portal, Access | 1 | EmployeeId, Austrittsdatum |
| `EmployeeDataErasureRequested` | Event | BC Integration | alle Dienste mit Kopien | 1 | EmployeeId |
| `StoreChanged` | Event | BC Integration | Workforce, Portal, Access | 1 | StoreId, Nummer, Name, Adresse, Öffnungszeiten, aktiv |
| `PermissionsChanged` | Event | Access | alle APIs | 1 | EmployeeId, Scopes (Liste StoreIds), Rollen-Zusätze, Version |
| `EmployeeAvailabilityChanged` | Event | Workforce | Portal (optional, nur vorbereitet) | 2, neutralisiert | EmployeeId, Von, Bis, Status (Available/Unavailable), **kein Grund** |
| `ShiftUncovered` | Event | Workforce | – (vorbereitet) | 2, neutralisiert | ShiftId, StoreId, Von, Bis |
| `SyncAbsenceToBc` | Command | Workforce | BC Integration | 2 | AbsenceId, EmployeeId, Von, Bis, Art, Status, Version |
| `BcSyncSucceeded` | Event | BC Integration | Absender (Filter per Subscription-Regel auf `SourceService`) | 1 | SourceService, EntityType, EntityId, Version, BcSystemId |
| `BcSyncFailed` | Event | BC Integration | Absender (wie oben) | 1 | SourceService, EntityType, EntityId, Version, Fehlercode (ohne Inhalte) |
| `SendNotification` | Command | alle Fach-APIs | Notifications | je nach Daten | NotificationId, Empfänger, TemplateKey, Data, Channels, DeepLink |

### Hinweise

- `SyncAbsenceToBc` ist Klasse 2 und darf den Grund enthalten, weil BC das Archiv dafür ist. Es läuft über eine Queue, die nur BC Integration lesen darf.
- `BcSyncSucceeded`/`BcSyncFailed` gehen über ein Topic mit je einer gefilterten Subscription pro Absender, damit Workforce nur eigene Rückmeldungen sieht.
- `SendNotification.Data` darf Klasse-2-Inhalte nur enthalten, wenn `Channels` ausschließlich `InApp` ist. Das prüft der Notification Service und lehnt Verstöße ab.
- Jede Nachricht hat eine eindeutige ID (Idempotenz), eine Korrelations-ID und einen Zeitstempel aus `TimeProvider`.
- Weiterentwicklung nur additiv. Brechende Änderungen erzeugen eine neue Nachricht (`...V2`), die alte läuft parallel weiter.

## Phase 0: Bestandsaufnahme

In dieser Phase wird kein Code geändert. Ergebnis ist ein Bericht `docs/phase-0-bestandsaufnahme.md`, der zeigt, wie weit der vorhandene Code vom Zielbild entfernt ist.

### Aufgaben

- [ ] Solution-Struktur, Projekte und deren Abhängigkeiten erfassen.
- [ ] Für jeden vorhandenen Dienst (BC-Anbindung, Rollen & Rechte, Benachrichtigungen) dokumentieren: Endpunkte, Datenmodell, Datenbankzugriff, externe Abhängigkeiten, Authentifizierung, vorhandene Tests.
- [ ] BC-Anbindung im Detail: Welche BC-Endpunkte werden aufgerufen (Standard-API, OData, eigene API Pages)? Wie wird authentifiziert? Gibt es Retry, Throttling-Behandlung, Caching?
- [ ] Rollen & Rechte im Detail: Wird der Dienst von anderen Diensten synchron pro Request aufgerufen? Welche Rechte-Modelle existieren?
- [ ] Benachrichtigungen im Detail: Welche Kanäle, wie wird ausgelöst, enthält er Fachlogik anderer Bereiche?
- [ ] Verwendete NuGet-Pakete und Versionen auflisten, insbesondere Messaging, Test- und Auth-Bibliotheken.
- [ ] Abgleich mit diesem Plan: pro Abschnitt der "Verbindlichen Architekturentscheidungen" und "Datenschutz-Regeln" festhalten, was bereits passt, was abweicht und welcher Umbau nötig wäre.

### Abnahmekriterien

- Der Bericht enthält für jeden Dienst eine Tabelle "Ist / Soll / Umbauaufwand (klein, mittel, groß)".
- Jede vorgeschlagene Änderung an vorhandenem Code ist begründet.
- Offene Fragen zum BC-Datenmodell sind gesammelt.

**Stopp-Punkt:** Bericht vorlegen und auf Entscheidung warten, welche Umbauten in Phase 1 bis 5 einfließen.

## Phase 1: Grundgerüst, Aspire, Shared-Bibliotheken

Nach dieser Phase startet die gesamte Landschaft lokal mit einem Befehl, und die gemeinsamen Bausteine sind getestet. Fachlogik gibt es noch keine.

### Aufgaben

- [ ] Ordnerstruktur gemäß "Zielstruktur" anlegen bzw. vorhandene Projekte dorthin verschieben (nur wie in Phase 0 freigegeben).
- [ ] `Directory.Build.props`: .NET 10, Nullable, `TreatWarningsAsErrors`, gemeinsame Analyzer. `Directory.Packages.props` für zentrale Paketversionen.
- [ ] Aspire AppHost mit PostgreSQL (eine Instanz, je Dienst eine eigene Datenbank oder ein eigenes Schema), Service-Bus-Emulator, WireMock-Container als BC-Ersatz und allen Diensten.
- [ ] ServiceDefaults: OpenTelemetry, Health Checks (`/health/live`, `/health/ready`), Resilience für HttpClients.
- [ ] **Shared.Messaging:** `SensitivityClass`-Enum und `[Sensitivity]`-Attribut, Basis-Interface für Nachrichten (`MessageId`, `CorrelationId`, `OccurredAt`), Wolverine-Konfiguration mit EF-Core-Outbox/Inbox, Konvention für Topic- und Queue-Namen, Logging-Filter, der Nachrichteninhalte nie ausgibt, Aufräumjob für Outbox/Inbox (Standard 7 Tage).
- [ ] **Shared.Authorization:** standardbasierte JWT-Prüfung (Authority, Audience, Tenant nur aus Konfiguration), **zentrales Claims-Mapping** von anbieterspezifischen Claims (`roles`, `oid`) auf interne Claims (`app:role`, `app:employee_id`), Auflösung der externen Identität auf die interne EmployeeId über den lokalen Cache, Policies für die groben Rollen, Resource-based Handler "Benutzer darf Filiale X" und "Benutzer darf Mitarbeitenden Y", lokaler Berechtigungs-Cache (In-Memory mit Persistenz in der jeweiligen Dienst-DB), Handler für `PermissionsChanged` mit Versionsprüfung (ältere Versionen ignorieren). Für ausgehende Aufrufe ein `DelegatingHandler`, der Tokens über ein per Konfiguration wählbares `TokenCredential` holt und zwischenspeichert. Fachcode sieht ausschließlich interne Claims und die EmployeeId.
- [ ] **Shared.Approvals:** Modell für Genehmigungsanfrage, Schritte, Entscheidungen, Statusübergänge (Angefragt → in Prüfung → genehmigt / abgelehnt / zurückgezogen). Persistenz über ein von der einbindenden API bereitgestelltes EF-Core-Mapping. Noch ohne konkrete Genehmigungsketten.
- [ ] **Shared.Testing:** Test-Auth-Handler (Identität, Rollen, Scopes pro Test setzbar), PostgreSQL- und Service-Bus-Fixtures mit Testcontainers, `FakeTimeProvider`-Registrierung.
- [ ] **Architecture.Tests:** Abhängigkeitsregeln aus "Regeln für die Projektabhängigkeiten", Verbot von `DateTime.Now`/`UtcNow`, jede Nachricht trägt `[Sensitivity]`, keine Nachricht der Klasse 3, `*.Core` referenziert weder `Microsoft.Identity.*` noch `Azure.*` noch BC-Typen, außerhalb von `Shared.Authorization` kein Zugriff auf die Claims `oid` oder `roles`.

### Abnahmekriterien

- `dotnet run` im AppHost startet alle Dienste, jeder liefert `200` auf `/health/ready`.
- Unit-Tests für Approvals-Statusübergänge (inkl. unzulässiger Übergänge) und für Scope-Prüfung im Authorization-Handler sind grün.
- Ein Integrationstest beweist: Nachricht und Zustandsänderung werden in derselben Transaktion gespeichert; bei Rollback wird nichts versendet.
- Ein Test beweist, dass der Logging-Filter keine Nachrichteninhalte ausgibt.
- Architekturtests sind grün.

**Stopp-Punkt:** Zusammenfassung, gewählte Wolverine-Version und Lizenzhinweis zur Bestätigung vorlegen.

## Phase 2: Contracts

In dieser Phase entstehen alle Nachrichten aus dem Katalog als reine Records, abgesichert durch Snapshot-Tests. Danach ändern sich Contracts nur noch additiv.

### Aufgaben

- [ ] Jede Nachricht aus dem "Event- und Command-Katalog" im `*.Contracts`-Projekt des Herausgebers anlegen: `sealed record`, unveränderlich, nur primitive Typen, `DateOnly`, `DateTimeOffset`, Enums und andere Records.
- [ ] Jede Nachricht mit `[Sensitivity]` versehen.
- [ ] XML-Doku pro Nachricht: Zweck, Herausgeber, erlaubte Konsumenten.
- [ ] Enums mit explizitem `Unknown = 0` und expliziten Werten; Serialisierung als String.
- [ ] Für jede Nachricht eine Beispielinstanz in `tests/<Dienst>.Contracts.Tests` und einen Verify-Snapshot-Test der JSON-Serialisierung.
- [ ] Die Snapshot-Dateien dienen Konsumenten als Testeingabe. Lege eine Hilfsmethode in `Shared.Testing` an, die eine Beispielnachricht aus dem Snapshot lädt.
- [ ] Ein Test pro Konsument stellt sicher, dass unbekannte zusätzliche JSON-Felder ignoriert werden (Vorwärtskompatibilität).

### Beispiel

```csharp
/// Workforce meldet eine Änderung der Verfügbarkeit.
/// Enthält bewusst keinen Grund (Klasse 2, neutralisiert).
[Sensitivity(SensitivityClass.SensitiveNeutralized)]
public sealed record EmployeeAvailabilityChanged(
    Guid MessageId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    DateOnly From,
    DateOnly To,
    AvailabilityStatus Status) : IMessage;
```

### Abnahmekriterien

- Alle Nachrichten aus dem Katalog existieren, keine darüber hinaus.
- Snapshot-Tests und Architekturtests sind grün.

**Stopp-Punkt:** Liste der Contracts mit Feldern vorlegen, insbesondere alle Stellen mit `TODO(BC-Modell)`.

## Phase 3: BC Integration Service

Der BC Integration Service wird zum einzigen Zugang zu Business Central. Er übersetzt und überträgt, enthält aber keine Fachlogik anderer Bereiche. Gebaut wird gegen WireMock; eine echte BC-Sandbox kommt nur in wenigen, separat markierten Tests vor.

### Aufgaben

- [ ] **BC-Client:** typisierter HttpClient mit Entra-Token für BC, zentralem Rate Limiter (ein Limiter für den ganzen Dienst, konfigurierbar), Retry mit Beachtung von `Retry-After` bei HTTP 429, Timeouts, Circuit Breaker. Prioritäten über getrennte Queues (Workforce-Sync vor späteren Massenvorgängen).
- [ ] **Interne Gliederung nach Domänen:** `MasterData/` (Mitarbeitende, Filialen), `Workforce/` (Abwesenheiten, später Dienstpläne und Zeiten). Ein späterer Ordner `Inventory/` wird nur als leerer Platzhalter mit README angelegt.
- [ ] **Stammdaten lesen:** periodischer Abgleich über `lastModifiedDateTime` (Intervall konfigurierbar), vorbereitet für BC-Webhooks. Änderungen werden mit dem letzten bekannten Stand verglichen; nur echte Änderungen erzeugen `EmployeeChanged`/`StoreChanged`. `EmployeeHired`/`EmployeeLeft` werden aus Ein-/Austrittsdatum abgeleitet.
- [ ] **Mapping-Tabelle:** BC-SystemId ↔ interne GUID pro Entitätstyp. Die interne GUID für Mitarbeitende und Filialen wird hier vergeben und ist systemweit die ID.
- [ ] **Schreiben nach BC:** Handler für `SyncAbsenceToBc`. Upsert über die externe ID (interne AbsenceId). Versionsprüfung: ältere Versionen als die zuletzt übertragene werden verworfen. Ergebnis als `BcSyncSucceeded` oder `BcSyncFailed`.
- [ ] **Fehlerklassen:** vorübergehend (Retry), dauerhaft fachlich (z. B. Validierungsfehler von BC → `BcSyncFailed`, kein Retry), technisch dauerhaft (→ Dead Letter). Fehlercodes ohne personenbezogene Inhalte.
- [ ] **Löschung:** Endpunkt bzw. Job, der nach einer (vorerst manuell ausgelösten) Freigabe `EmployeeDataErasureRequested` veröffentlicht. Die Fristenlogik ist außerhalb des Scopes.
- [ ] **Keine öffentlichen Endpunkte für Frontends.** Interne Endpunkte nur mit Service-zu-Service-Auth.

### Tests

- WireMock-Szenarien unter `tests/Fixtures/Bc`: Erfolg, 429 mit `Retry-After`, 500, Timeout, abgelaufenes Token, BC-Validierungsfehler, leere Ergebnisliste, Paginierung.
- Stammdaten-Abgleich: unveränderte Daten erzeugen kein Event, geänderte genau eines, Austritt erzeugt `EmployeeLeft`.
- `SyncAbsenceToBc`: doppelte Nachricht erzeugt genau einen BC-Aufruf bzw. einen idempotenten Upsert; veraltete Version wird verworfen.
- Rate Limiter: bei parallelen Aufträgen wird das konfigurierte Limit nie überschritten.
- Tests gegen eine echte BC-Sandbox als `[Trait("Category", "BcSandbox")]` markieren und standardmäßig nicht ausführen.

### Abnahmekriterien

- Kein anderer Dienst enthält BC-Client-Code oder BC-Konfiguration (Architekturtest).
- Alle oben genannten Tests sind grün.

**Stopp-Punkt:** Liste der angenommenen BC-Endpunkte und Felder (alle `TODO(BC-Modell)`) zur Klärung vorlegen.

## Phase 4: Access Service

Der vorhandene Rollen-und-Rechte-Service wird zum Access Service umgebaut: Er verwaltet und verteilt Berechtigungen, prüft sie aber nicht mehr zur Laufzeit für andere Dienste.

### Aufgaben

- [ ] Kopien von Mitarbeitenden und Filialen aus `EmployeeChanged`/`StoreChanged` pflegen. **Identitätszuordnung:** Tabelle "externe Identität (Anbieter + ID) → EmployeeId", hergestellt über die Personalnummer, die im Entra-Benutzerobjekt im Feld `employeeId` gepflegt ist. Die Zuordnung wird mit `PermissionsChanged` verteilt. Nicht zuordenbare Anmeldungen werden abgewiesen und ohne personenbezogene Daten protokolliert.
- [ ] **Abgeleitete Scopes:** Filialleitung erhält Scope auf ihre Hauptfiliale; Mitarbeitende auf sich selbst. Die Ableitungsregeln stehen in einer eigenen, getesteten Klasse. Welche BC-Position als Filialleitung gilt, ist konfigurierbar (`TODO(BC-Modell)`).
- [ ] **Manuelle Zusätze:** Bezirksleitung mit mehreren Filialen, zeitlich befristete Vertretungen (mit Von/Bis), Sonderrechte. Admin-Endpunkte dafür, nur mit Rolle HR oder Geschäftsführung.
- [ ] **Vorgesetztenbeziehung** pro Mitarbeitendem (für Genehmigungsketten und Benachrichtigungen), abgeleitet aus Filiale und Position, manuell überschreibbar.
- [ ] Bei jeder Änderung der effektiven Berechtigungen einer Person: `PermissionsChanged` mit fortlaufender Version.
- [ ] Ablaufende Vertretungen werden zeitgesteuert (über `TimeProvider`) entfernt und erzeugen ebenfalls `PermissionsChanged`.
- [ ] `EmployeeLeft`: Scopes zum Austrittsdatum entziehen. `EmployeeDataErasureRequested`: personenbezogene Daten löschen.
- [ ] Endpunkt zum vollständigen Neuaufbau eines Berechtigungs-Caches ("alle aktuellen Berechtigungen"), damit eine API nach Ausfall oder Neuinstallation aufholen kann. Nur Service-zu-Service.
- [ ] Vorhandene synchrone Prüf-Endpunkte, die andere Dienste pro Request nutzen, werden als veraltet markiert; ihre Entfernung wird erst nach Rückfrage umgesetzt.

### Tests

- Ableitungsregeln: Filialleitung, Mitarbeitende, Positionswechsel, Filialwechsel.
- Vertretung beginnt und endet zum richtigen Zeitpunkt (`FakeTimeProvider`).
- Austritt entzieht Rechte erst zum Austrittsdatum, nicht beim Empfang der Nachricht.
- Versionsnummern steigen streng monoton pro Person.
- Integrationstest: Eine Konsumenten-API mit `Shared.Authorization` erhält `PermissionsChanged` und verweigert danach den Zugriff auf eine entzogene Filiale.

**Stopp-Punkt:** Ableitungsregeln und Admin-Endpunkte zur fachlichen Prüfung vorlegen.

## Phase 5: Notification Service

Der vorhandene Benachrichtigungsservice wird so umgebaut, dass er nur noch `SendNotification`-Commands verarbeitet und kein Fachwissen anderer Bereiche enthält.

### Aufgaben

- [ ] Handler für `SendNotification`, idempotent über `NotificationId`.
- [ ] Vorlagen per `TemplateKey` (Namensschema `<dienst>.<anlass>.<empfängerrolle>`), mit Platzhaltern aus `Data`. Vorlagen zunächst als Dateien im Repo.
- [ ] Kanäle: `InApp` (gespeichert, abrufbar über Endpunkt "meine Benachrichtigungen", als gelesen markierbar), `Push` und `Email` vorerst als Adapter-Interfaces mit Fake-Implementierung.
- [ ] **Schutzregel:** Enthält `Data` Werte, die die Vorlage als Klasse 2 markiert, und sind andere Kanäle als `InApp` angefordert, wird der Command abgelehnt und ohne Inhalte als Warnung geloggt. Push- und Mail-Vorlagen sind generisch und enthalten nur einen DeepLink.
- [ ] Benutzerpräferenzen pro Kanal und Benachrichtigungstyp; Pflichtbenachrichtigungen sind nicht abschaltbar (Kennzeichen in der Vorlage).
- [ ] Aufbewahrung: InApp-Benachrichtigungen nach 90 Tagen löschen (konfigurierbar). `EmployeeDataErasureRequested` löscht alles zur Person.
- [ ] Vorhandene Logik, die auf Fachevents anderer Dienste reagiert, wird aufgelistet und erst nach Rückfrage entfernt.

### Tests

- Doppelter Command erzeugt genau eine Benachrichtigung.
- Klasse-2-Daten mit Kanal `Push` werden abgelehnt; mit `InApp` angenommen.
- Push-Fake erhält nie Inhalte aus `Data`, nur generischen Text und DeepLink.
- Benutzer sieht über den Endpunkt nur eigene Benachrichtigungen (Test-Auth mit zwei Personen).

**Stopp-Punkt:** Vorlagenliste und Präferenzmodell vorlegen.

## Phase 6: Workforce API – Abwesenheiten

Die Workforce API entsteht mit dem Teilbereich Abwesenheiten (Krankmeldung und Urlaub). Dienstplanung und Zeiterfassung folgen später nach demselben Muster und sind hier nur als leere Feature-Ordner angelegt.

### Fachliche Regeln

- **Krankmeldung:** erfasst durch die Person selbst oder durch Filialleitung/HR für sie. Felder: Von, Bis (offen erlaubt, dann "bis auf Weiteres"), AU liegt vor ja/nein. Kein Freitextfeld für Gründe. Keine Genehmigung nötig.
- **Urlaubsantrag:** durch die Person selbst. Genehmigungskette über `Shared.Approvals`: direkte Vorgesetzte laut Access Service. Status: beantragt, genehmigt, abgelehnt, zurückgezogen, storniert.
- **Urlaubskonto:** Jahresanspruch (vorerst manuell gepflegt, `TODO(BC-Modell)` für späteren Import), genommen, geplant, Rest. Ein Antrag über den Rest hinaus wird mit fachlichem Fehler abgelehnt.
- **Überschneidungen:** Eine Person kann nicht zwei sich überlappende Abwesenheiten haben. Ausnahme: Krankheit während genehmigtem Urlaub; dann wird der Urlaub für die betroffenen Tage storniert und dem Konto gutgeschrieben.
- **Arbeitstage:** Urlaubstage werden anhand der Arbeitstage der Person berechnet (vorerst Mo–Sa konfigurierbar, gesetzliche Feiertage pro Bundesland als austauschbarer Dienst).
- **Versionierung:** Jede Änderung erhöht die Version der Abwesenheit. Die Version wird an BC übertragen.

### Aufgaben

- [ ] Domänenmodell und Regeln in `Workforce.Core/Absences`, vollständig ohne Infrastruktur.
- [ ] Kopien von Mitarbeitenden und Filialen aus Events pflegen; `PermissionsChanged` über `Shared.Authorization`.
- [ ] Endpunkte: eigene Abwesenheiten (lesen, Krankmeldung erfassen, Urlaub beantragen, zurückziehen); Filialleitung: Abwesenheiten der eigenen Filiale lesen, Krankmeldung für Mitarbeitende erfassen, Urlaub genehmigen/ablehnen; HR: alles, gefiltert nach Scopes.
- [ ] **Sichtbarkeit des Grundes:** Kolleg:innen derselben Filiale sehen nur "abwesend". Den Grund sehen nur die Person selbst, Vorgesetzte und HR. Das wird über die Autorisierung durchgesetzt, nicht im Frontend.
- [ ] Interner Endpunkt für Abwesenheitsdetails mit App Role `Workforce.AbsenceDetails.Read` (für spätere HR API).
- [ ] Ausgehend: `EmployeeAvailabilityChanged`, `SyncAbsenceToBc`, `SendNotification` (Krankmeldung an Filialleitung; Urlaubsantrag an Vorgesetzte; Entscheidung an Antragsteller). Alle über Outbox in derselben Transaktion.
- [ ] BC-Sync-Status pro Abwesenheit, aktualisiert über `BcSyncSucceeded`/`BcSyncFailed`, im Lese-Endpunkt für HR sichtbar.
- [ ] Reaktion auf `EmployeeLeft` (künftige Abwesenheiten nach Austritt stornieren) und `EmployeeDataErasureRequested`.

### Tests

- Unit-Tests für jede fachliche Regel inkl. Grenzfälle: Jahreswechsel, Feiertag im Zeitraum, Krankheit im Urlaub, offenes Bis-Datum, Rückzug nach Genehmigung, Antrag am Tag des Austritts.
- Berechtigungstests: Kollegin sieht Grund nicht; Filialleitung von Filiale A sieht Filiale B nicht; Vertretung sieht Filiale B nur im Vertretungszeitraum.
- Integrationstest: Krankmeldung erzeugt genau drei ausgehende Nachrichten in der Outbox, Grund nur in `SyncAbsenceToBc`, nicht in `EmployeeAvailabilityChanged` und nicht in Push-fähigen `SendNotification`-Daten.
- Löschtest: Nach `EmployeeDataErasureRequested` existieren keine personenbezogenen Daten der Person mehr.

**Stopp-Punkt:** Fachliche Regeln, offene Annahmen und Endpunktliste vorlegen.

## Phase 7: Vertikaler Durchstich

Diese Phase beweist, dass alle Teile zusammen funktionieren: Eine Krankmeldung läuft vom Aufruf bis zur Benachrichtigung und zum BC-Eintrag durch die gesamte Landschaft.

### Aufgaben

- [ ] Minimales Gerüst der Portal API: Projektstruktur, Auth, Kopien von Mitarbeitenden/Filialen, `/health`. Keine Fachfunktionen. Ziel ist nur, dass die Event-Verteilung an einen dritten Konsumenten nachweisbar ist.
- [ ] Service-Bus-Topologie als Bicep unter `/infra/servicebus.bicep`: Topics, Queues, Subscriptions mit Filtern, Rollenzuweisungen pro Managed Identity, Zweck-Kommentar pro Subscription. Lokal wird dieselbe Topologie im Emulator verwendet (Konfiguration daraus ableiten oder gleich halten und per Test vergleichen).
- [ ] End-to-End-Test im Aspire-Testprojekt (`Aspire.Hosting.Testing`), alles lokal mit WireMock-BC:
  1. WireMock liefert eine Filiale und zwei Mitarbeitende (Filialleitung, Mitarbeiterin).
  2. BC Integration gleicht ab; Workforce, Portal und Access haben die Kopien.
  3. Access veröffentlicht Berechtigungen; Workforce kennt die Filialleitung als Vorgesetzte.
  4. Die Mitarbeiterin meldet sich über die Workforce API krank.
  5. WireMock hat einen Upsert-Aufruf mit der korrekten externen ID erhalten.
  6. Workforce zeigt den Sync-Status `Synced`.
  7. Die Filialleitung hat eine InApp-Benachrichtigung; der Push-Fake hat nur generischen Text erhalten.
  8. Eine Kollegin derselben Filiale sieht über die API nur "abwesend", keinen Grund.
- [ ] Zweiter End-to-End-Test: BC antwortet zuerst mit 429, dann mit Erfolg. Ergebnis wie oben, Sync-Status zwischenzeitlich `Pending`.
- [ ] Dritter End-to-End-Test: BC antwortet mit Validierungsfehler. Sync-Status `Failed`, Fehlercode ohne personenbezogene Daten, keine Wiederholung.
- [ ] Kurze Entwicklerdoku `docs/architektur.md`: Diagramm der Dienste und Nachrichten (Mermaid), Startanleitung, wie man einen neuen Konsumenten oder eine neue Nachricht hinzufügt. Zusätzlich **Entra-Einrichtungspaket** für den Tenant-Admin: `docs/entra-setup.md` (alle App-Registrierungen, App Roles, Gruppen, Managed Identities, benötigte Zustimmungen inkl. BC `API.ReadWrite.All`, jeweils mit Begründung, plus Bitte um eine auf diese Apps beschränkte Rolle "Anwendungsadministrator") und prüfbare PowerShell-Skripte unter `infra/entra/` für die Rollenzuweisungen. Die Skripte werden **nicht** ausgeführt.

### Abnahmekriterien

- Alle drei End-to-End-Tests sind grün und laufen lokal in unter fünf Minuten.
- Die gesamte Testsuite ist grün.
- Die Doku ermöglicht einer neuen Person, die Landschaft lokal zu starten.

**Stopp-Punkt:** Abschlussbericht mit allen offenen `TODO(BC-Modell)`-Stellen, bekannten Einschränkungen und Vorschlag für die nächste Fachfunktion (Dienstplan-Einsicht oder Zeiterfassung).

## Teststrategie und Definition of Done

Getestet wird dort am dichtesten, wo Fehler am teuersten sind: Zeit- und Abwesenheitslogik, BC-Synchronisation, Berechtigungen und Datenschutzregeln.

| Ebene | Werkzeug | Was | Läuft |
| --- | --- | --- | --- |
| Unit | xUnit, `FakeTimeProvider` | Fachregeln in `*.Core`, Approvals, Scope-Ableitung | bei jedem Build |
| Integration | `WebApplicationFactory`, Testcontainers | Endpunkte, Persistenz, Outbox, Berechtigungen, Löschung | bei jedem Build |
| Vertrag | Verify | Serialisierung jeder Nachricht, Konsumenten verarbeiten Snapshots | bei jedem Build |
| Architektur | NetArchTest/ArchUnitNET | Abhängigkeiten, `TimeProvider`, Sensitivity | bei jedem Build |
| End-to-End | Aspire Testing, WireMock | vertikaler Durchstich | bei jedem Build (lokal) |
| BC-Sandbox | echte BC-Umgebung | API Pages, Upsert | nur manuell (`Category=BcSandbox`) |

### Pflichtfälle für Zeitlogik

Jede Regel mit Datums- oder Zeitbezug wird mindestens gegen diese Fälle getestet: Monats- und Jahreswechsel, Sommer-/Winterzeitumstellung, Schaltjahr (29. Februar), gesetzlicher Feiertag, Zeitraum über Mitternacht.

### Definition of Done pro Aufgabe

- [ ] Tests zuerst geschrieben, alle grün.
- [ ] `dotnet build` ohne Warnungen, `dotnet test` für die gesamte Solution grün.
- [ ] Keine Nachrichteninhalte oder personenbezogenen Daten in Logs.
- [ ] Jede neue Nachricht im Katalog, klassifiziert und mit Snapshot-Test.
- [ ] Jeder neue Endpunkt mit mindestens einem positiven und einem negativen Berechtigungstest.
- [ ] Offene Annahmen als `TODO(...)` markiert und am Stopp-Punkt gemeldet.

## Offene Entscheidungen

Diese Punkte kann die ausführende Instanz nicht selbst entscheiden. Sie arbeitet bis zur Klärung mit markierten Platzhaltern weiter und meldet sie an jedem Stopp-Punkt.

| Thema | Frage | Betrifft Phase | Klärung mit |
| --- | --- | --- | --- |
| BC-Stammdaten | Exakte Feldliste der Mitarbeiterkarte und Filialen, die übernommen werden | 2, 3 | BC-Verantwortliche |
| BC-Datenmodell Workforce | Tabellen und API Pages für Abwesenheiten (später Dienstpläne, Zeiten) in der AL-Extension; Upsert über externe ID möglich? | 3, 6 | BC-Verantwortliche |
| BC-Änderungserkennung | Webhooks verfügbar oder nur Polling? Gewünschtes Intervall | 3 | BC-Verantwortliche |
| Positionen | Welche BC-Positionen gelten als Filialleitung, Bezirksleitung, HR? | 4 | HR |
| Urlaubsanspruch | Woher kommt der Jahresanspruch (BC, manuell, DATEV)? | 6 | HR |
| Arbeitstage und Feiertage | Standard-Arbeitstage, relevante Bundesländer | 6 | HR |
| Datenklassifizierung | Bestätigung der drei Klassen und der Zuordnung, Aufbewahrungsfristen | alle | Datenschutzbeauftragte:r |
| Mitbestimmung | Einbindung des Betriebsrats (sofern vorhanden) | vor Go-live | Geschäftsführung |
| Messaging-Bibliothek | Wolverine-Version und Lizenz bestätigt? | 1 | Entwickler |
| Entra-Einrichtung | Termin mit Tenant-Admin auf Basis des Einrichtungspakets; app-beschränkte Admin-Rolle möglich? | nach 7 | Tenant-Admin, ggf. über Geschäftsführung |
| Entra-Lizenzen | Entra ID P1 für Gruppenzuweisung vorhanden? Haben alle Filialmitarbeitenden ein Konto (ggf. Frontline-Lizenzen)? | vor Go-live | IT, Geschäftsführung |
| Personalnummer in Entra | Wird das Feld `employeeId` im Entra-Benutzerobjekt gepflegt, und von wem? | 4 | IT, HR |

## Außerhalb des Scopes

Diese Themen werden in diesem Plan bewusst nicht bearbeitet. Wenn eine Aufgabe sie berührt, wird nur eine Schnittstelle oder ein Platzhalter vorbereitet.

- Inventur-Anwendung und deren Umzug auf den BC Integration Service (Hochphase der Inventuren; nur leerer Ordner `Inventory/`).
- Deployment, CI/CD-Pipelines, Azure-Ressourcen außer der Service-Bus-Topologie in Bicep.
- Frontends (Angular, Mobile, Desktop).
- HR API (Recruiting, DATEV-Export).
- Dienstplanung, Zeiterfassung, Schichttausch und Umplanung in der Workforce API.
- Alle Fachfunktionen der Portal API (Inhalte, Schulungen, Berichtsheft, Incidents, Kassen-/Losbestände, Statistiken).
- Echte Push- und Mail-Zustellung (nur Adapter mit Fake).
- Automatische Fristenlogik für Löschungen.
