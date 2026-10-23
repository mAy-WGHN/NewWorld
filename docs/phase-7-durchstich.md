# Phase 7 – Vertikaler Durchstich

Stand: 2026-10-23. Build ohne Warnungen; gesamte Solution grün (inkl. drei E2E-Tests, BC-Sandbox übersprungen). Vollständiger Testlauf lokal ca. 1,5 Minuten, die E2E-Tests (ganze Landschaft über Aspire) rund 20 Sekunden nach dem Start.

## Gebaut
- **Portal-Gerüst:** Kopien von Mitarbeitenden und Filialen (`EmployeeChanged`, `StoreChanged`), Löschung bei `EmployeeDataErasureRequested`, `EmployeeAvailabilityChanged` wird nur bestätigt. Beweist die Verteilung an einen dritten Konsumenten.
- **Service-Bus-Topologie:** `infra/servicebus.bicep` (Topics, Queues, Subscriptions mit Zweck-Kommentar und SQL-Filter, Rollen je Managed Identity) und `infra/servicebus-emulator.json`; Vergleichstest in `tests/Architecture.Tests`. Der Bicep-Text konnte ohne Azure-CLI nicht kompiliert werden (nur durch den Test strukturell geprüft).
- **Routing-Eigenschaft `SourceService`** für die gefilterten `BcSync*`-Subscriptions.
- **Lokale Dienst-Authentifizierung** (Fake-Token) und Personen-Tokens gegen Access im AppHost, Katalog-Anmeldung der Dienste bei Access, PIN-Prüfung über BC-Fake.
- **E2E (`tests/AppHost.Tests/AbsenceE2ETests.cs`):** (1) Krankmeldung → BC-Upsert mit der Absence-Id als externe ID → `Synced` → InApp-Nachricht der Filialleitung → Kollegin sieht nur „Absent“, Filialleitung die Art; Kopien im Portal. (2) 429 → Status `Pending` → Erfolg beim Retry. (3) Validierungsfehler → `Failed`, genau ein Versuch, Fehlercode ohne Namen/Personalnummer/BC-Text.
- **Doku:** `docs/architektur.md`, `docs/entra-setup.md`, `infra/entra/*.ps1` (nicht ausgeführt, mangels PowerShell nicht probeweise gelaufen).

## Abweichungen und Einschränkungen
1. **Push-Fake (Plan, E2E Schritt 7):** Workforce fordert für Krankmeldungen ausschließlich InApp an (datenschutzkonservativ); es gibt daher keine Push-Zustellung, die der Test prüfen könnte. Geprüft wird die InApp-Nachricht; die Schutzregel („nur InApp mit Klasse-2-Daten“) ist in den Notifications-Tests abgedeckt.
2. **Entra** (Entscheidung 13): keine Benutzer-App-Rollen/-Gruppen, keine Identitätszuordnung; Entra nur zwischen Diensten. Weicht vom Plan ab.
3. **Emulator:** Lebensdauer maximal 1 h, mindestens eine Subscription je Topic (daher kein `shift-uncovered`-Topic bis zum ersten Herausgeber).
4. **Portal** konsumiert `EmployeeHired`/`EmployeeLeft` noch nicht (Kopie nutzt das Austrittsdatum aus `EmployeeChanged`).
5. `Access.Permissions.Read` (Neuaufbau der Berechtigungskopie): Endpunkt vorhanden, Aufrufer-Code in den Diensten fehlt noch.
6. Nicht verifiziert: Bicep gegen echte Azure-Umgebung, PowerShell-Skripte, Betrieb mehrerer Instanzen (`TODO(Betrieb)` bei Master-Data-Sync und Forwarded Headers).

## Offene `TODO(BC-Modell)` (Auswahl nach Bereich, Vollständige Liste per `grep -rn "TODO(" src`)
- **Mitarbeiter/Filialen:** Feldliste, Vornamen/Nachnamen getrennt, Positions- und Beschäftigungsarten-Wertelisten (Filialleitung/Bezirksleitung/HR), Filialadresse, Öffnungszeiten, Quelle der Hauptfiliale.
- **Abwesenheiten:** API-Seite, Mapping Art → BC-Code (KRANK/URLAUB), Genehmigungsstatus, offenes Ende, Fehlerformat der Validierung (`BcSyncErrorCode`), Rate-Limits des Mandanten.
- **PIN-Prüfung:** Pfad/Seite statt SOAP-Codeunit des Archivs.
- **Urlaubsanspruch:** Herkunft (derzeit manuell).
- **Entscheidungen:** Bundesland je Filiale (Feiertage), Webhook vs. Polling und Intervall, weitere Rollen-Automatik über Funktion (Entscheidung 13).

## Vorschlag nächste Fachfunktion
**Dienstplan-Einsicht** (nur lesen) vor der Zeiterfassung: nutzt dieselben Kopien, Berechtigungen und Sichtbarkeitsregeln, braucht kein neues Schreibmodell nach BC und liefert `ShiftUncovered`/`EmployeeAvailabilityChanged` echte Konsumenten; die Zeiterfassung hat höhere Datenschutz- und BC-Spiegelungsanforderungen.
