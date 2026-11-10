# Phase 0: Bestandsaufnahme (Rückblick)

Ursprünglich 2026-10-21, gekürzt 2026-11-10. Die ausführliche Fassung mit allen Ist/Soll-Tabellen und Dateizeilen steht in der Git-Historie. Die Entscheidungen, die daraus folgten, stehen in `CLAUDE.md` („Entscheidungen nach Phase 0“); dort gilt der aktuelle Stand.

## Was es gab

Fünf Bestandsdienste, die heute unter `Archiv/` liegen und per `make up` lauffähig bleiben. Der Plan nannte nur drei; die anderen zwei wurden zugeordnet.

| Bestand | Inhalt | Wurde zu |
| --- | --- | --- |
| `BC-Anbindung` (~830 Zeilen, ohne Datenbank) | SOAP-Gateway: PIN-Prüfung (`Zeiterfassung.GetStatus`) und Filialen (`GetStores`), Entra-Token für BC | `src/BcIntegration` |
| `Rollen-Rechte` (~5.400) | Login mit eigenen JWTs, feingranulares ACL (Rollen, Katalog, Scopes, Custom Privileges), BC-PIN-Login | `src/Access` |
| `Benachrichtigungen` (~1.700) | nur Web Push, Fachlogik für `report.created` | `src/Notifications` (die Meldungsregeln übernimmt das Portal, Entscheidung 41) |
| `IRS-API` (~8.000) | Meldungen, FAQ, Statistik | `src/Portal` (umbenannt) |
| `hr-tool-API` (~5.400) | überwiegend Workforce-Fachlichkeit (Dienstpläne, Abwesenheiten), kein Recruiting oder DATEV | Fachreferenz für `src/Workforce` |

Gemeinsam fehlte allen: Messaging über Service Bus, Outbox, Kopien von Mitarbeitenden und Filialen aus Events, `Shared.*`, Sensibilitätsklassen, Löschung bei `EmployeeDataErasureRequested`, `TimeProvider` (stattdessen `DateTime.UtcNow`), `TreatWarningsAsErrors`, ein Schema je Dienst, OpenTelemetry. Das Rechtemodell und alle Dienste waren synchron aneinander gekoppelt (`GET /api/me/privileges` pro Request).

## Wiederverwendet

Scope-Modell und Prüfregeln (`AclGuard`), Privilegien-Generator, Push-Adapter mit VAPID-Format, Idempotenzmuster, SOAP-Client und Token-Dienst von BC, die Testinfrastruktur. Datenschutzrisiken im Bestand: Meldungsfreitext im Push-Text, Auth-Fehlerbody im Log, Push-Endpoint-URL im Log.

## Daraus entschieden (Auswahl; vollständig in `CLAUDE.md`)

- `hr-tool-API` ist Basis der Workforce, nicht der HR API; `IRS-API` heißt `Portal` (Entscheidungen 1, 2, 10).
- Das feingranulare ACL, der BC-PIN-Login, `GET /api/me/privileges` und die Meldungsbenachrichtigung bleiben (3 bis 6, 13).
- Kein Wolverine (8), Dienst-Authentifizierung ohne Entra (18), Bestandsdienste unter `Archiv/` ohne eigene `.git`-Repos (7, 10).

## Noch offene Fragen an BC (Stand heute unverändert)

- **Mitarbeiterkarte:** Feldliste, Eintritts- und Austrittsdatum, Hauptfiliale, `lastModifiedDateTime`, getrennt Vor- und Nachname. Bekannt ist nur `employeeName` aus `GetStatus`.
- **Filialen:** bekannt `No.`, `Name` (LSC Store); unbekannt SystemId, Änderungszeitstempel, Paginierung.
- **Abwesenheiten (`SyncAbsenceToBc`):** Endpunkt, Felder, Upsert über externe ID, Versionsfeld, Fehlerformat bei Validierung.
- **Zugang:** SOAP-Codeunits oder Standard-API/OData/API Pages; uneinheitliche Antwortformate (JSON im `return_value`, bei anderen XML).
- **Änderungserkennung:** Webhooks oder nur Polling, gewünschtes Intervall (Polling zuerst, Entscheidung 25).
- **Drosselung:** Liefert BC 429 mit `Retry-After`, welche Limits?
- **Genehmigungsstatus-Werte** und ob `SONDERURLAUB`, `UNBEZ. U`, `URLAUBSANTRAGSZEITR` genehmigt werden müssen. Zeiterfassung: `docs/bc-zeiterfassung-schnittstelle.md`.

## Grenzen der damaligen Analyse

Rein lesend, nichts gebaut oder ausgeführt, Testzahlen aus gezählten Attributen, Logging nur stichprobenartig geprüft.
