# Umgesetzte Features (Stand 2026-11-04)

Übersicht, was in den neuen Diensten unter `src/` bereits gebaut ist. Die Regeln und Entscheidungen dahinter stehen in `CLAUDE.md`, Details je Bereich unter `docs/`.

## Querschnitt (Shared, Infrastruktur)

- **Messaging:** Service-Bus-Anbindung mit eigener Outbox und Inbox, Transport hinter einem Interface, kein Wolverine.
- **Nachrichtenkatalog:** Alle Nachrichten sind klassifiziert und mit Snapshot-Tests abgesichert.
- **Berechtigungsprüfung:** Sie läuft lokal über `Shared.Authorization`.
- **Genehmigungen:** Die Bibliothek `Shared.Approvals` ist fertig.
- **Dienst-zu-Dienst-Auth:** Schlüsselpaar je Dienst, Kurzzeit-Tokens und Service Roles, ohne Entra (Entscheidung 18).
- **Topologie:** Bicep und Emulator-Konfiguration sind vorhanden, ein Architekturtest hält beide synchron.
- **Architekturtests:** Sie sichern Abhängigkeiten, Datenschutzklassen und Rechtebeschreibungen ab.
- **Lokale Landschaft:** Der AppHost startet Postgres, Emulator und WireMock als BC-Ersatz, dazu gibt es E2E-Tests.

## BC Integration

- Es gibt den einzigen BC-Zugang, mit Rate Limiter, Retry und Circuit Breaker.
- Stammdaten werden per Polling abgeglichen, nur Änderungen erzeugen `EmployeeChanged`, `EmployeeHired`, `EmployeeLeft` und `StoreChanged`.
- Die ID-Zuordnung ist umgesetzt.
- `SyncAbsenceToBc` arbeitet als Upsert mit den Ergebnissen `BcSyncSucceeded` und `BcSyncFailed`.
- Die PIN-Prüfung läuft per SOAP wie im HR-Tool, bisher nur gegen WireMock getestet.

## Access (Berechtigungen und Login)

- Das feingranulare ACL ist fertig: Rollen, Rechte-Katalog mit Beschreibung, Scope-Mengen, Zuweisungen mit Laufzeit und Vertretungen.
- Die Hauptfiliale ist pflegbar, BC liefert nur den Vorschlag.
- Zuständigkeiten je Thema laufen über `ResponsibilitiesChanged`.
- `PermissionsChanged` ist versioniert, `GET /api/me/privileges` liefert die Rechte.
- Login mit Personalnummer und BC-PIN: eigenes JWT, rotierendes Refresh-Token, Drosselung, Offline-Gültigkeit.
- Die Rolle „Former Employee“ gilt nach dem Austritt.

## Notifications

- Verarbeitet werden `SendNotification` und `EmployeeDataErasureRequested`.
- InApp ist echt, Push und Mail laufen über Adapter mit Fake.
- Es gibt Präferenzen, Pflichtvorlagen und 90 Tage Aufbewahrung.
- Datenschutzregeln sind durchgesetzt: keine Klasse-2-Daten in Push und Betreff.
- Texte sind durch HR pflegbar, mit Variablen wie `$USER.firstName`.

## Workforce

- **Abwesenheiten:**
  - Krankmeldung ohne Genehmigung.
  - Urlaub mit Genehmigungskette.
  - Kürzung bei Krankheit im Urlaub.
  - Feiertage für Bayern, Feiertagsöffnung je Filiale, auch als Mehrfachaktion.
  - BC-Sync.
- **AU-Upload:**
  - Verschlüsselt und getrennt gespeichert.
  - HR-Prüfung und Zugriffsprotokoll.
  - Löschfristen und Erinnerungen.
- **Urlaubskonto:**
  - Jahresanspruch und Zusatztage.
  - Verfallsregeln, Wartezeit und Rundung durch HR pflegbar.
  - Ein- und Austrittsjahr anteilig.
  - Unterjährige Änderungen.
- **Mitarbeiterkarte (Schritte 1 und 2):**
  - Anlage mit Nummernkreis und Auswahllisten durch HR.
  - Lesestufen.
  - Änderungsanträge mit HR-Entscheidung.
- **Nicht-Verfügbarkeit (Schritt 3):**
  - Arten und Einträge durch HR.
  - Zeitleiste mit Sichtbarkeitsregeln.
  - Meldung von Schichten ohne Besetzung.
- **Arbeitszeitkonto (Schritt 4):**
  - Buchungen, Monatsabschluss und Überstundenschwelle.
  - Minusstunden und Verfall durch HR schaltbar.
  - Auszahlung.
  - Erinnerung an HR bei Austritt.

## Portal

- Gerüst: Kopien von Mitarbeitenden und Filialen, Hauptfiliale aus Access, Ein- und Austritt.
- FAQ mit Bildern, Meldungen mit Filial-Reichweite, Archiv und Verweisen, Statistik.
- Benachrichtigung je Meldungskategorie ohne Meldungsinhalt.
- Das Angular-Frontend bleibt kompatibel.
- Öffentlich bleiben Meldungsformular und FAQ-Lesen, mit Rate Limit.

## Frontend (`Frontend/`, Angular)

- Rahmen mit Anmeldung (Personalnummer und PIN gegen Access), Sitzungserneuerung und Navigation nach den Rechten der Person.
- Rahmen und Startseite nach dem Entwurf: Kopfzeile mit Suche, Glocke mit Zähler und Person, Seitenleiste, vier Kacheln, Aufgaben und nächste Termine aus echten Daten; helles und dunkles Schema, Handyansicht.
- Alle Bereiche (mit Rechteübersicht) und Favoriten.
- Kennzahlen, Neuigkeiten, Schulungen und Anleitungen sind als „folgt“ gekennzeichnet, nichts ist erfunden.
- **Meine Abwesenheiten:** Urlaubskonto je Jahr, Krankmeldung, Urlaubsantrag, Zurückziehen, Krankmeldung beenden, AU hochladen.
- Alle weiteren Bereiche der Navigation sind Platzhalter.

## Nicht gebaut

- **Außerhalb des Scopes:** HR API, Dienstplanung und Zeiterfassung, Frontends, echter Push- und Mailversand.
- **BC-Übertragung offen:** Mitarbeiterkarte (Schritt 5), Spiegelung der Nicht-Verfügbarkeit, Spiegelung des Arbeitszeitkontos.
- **Portal:** Zweitmeinung und Import der Altdaten fehlen.
- **Workforce:** Abgeltung von Resturlaub beim Austritt, Feiertagsöffnung je Datum und Arbeitstage pro Person.
