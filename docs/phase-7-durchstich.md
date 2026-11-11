# Vertikaler Durchstich (Rückblick Phase 7)

Ursprünglich 2026-10-23, gekürzt 2026-11-10. Die End-to-End-Tests starten die ganze Landschaft über Aspire (mit WireMock als BC) und sind grün; der gesamte Testlauf dauert lokal wenige Minuten.

## Was gebaut ist

- **Portal-Gerüst** als dritter Konsument: Kopien von Mitarbeitenden und Filialen, Löschung bei `EmployeeDataErasureRequested`, Hauptfiliale aus `PermissionsChanged`, `EmployeeHired`/`EmployeeLeft`.
- **Service-Bus-Topologie** in `infra/servicebus.bicep` (Topics, Queues, Subscriptions mit Zweck-Kommentar und Filter, Rollen je Identität; mit `az bicep build` ohne Fehler kompiliert) und `infra/servicebus-emulator.json`. Ein Test in `tests/Architecture.Tests` hält beide, den Nachrichtenkatalog und die in den Diensten registrierten Subscriptions zusammen.
- **Routing-Eigenschaft `SourceService`** für die gefilterten `BcSync*`-Subscriptions.
- **Drei End-to-End-Tests** (`tests/AppHost.Tests/AbsenceE2ETests.cs`):
  1. Krankmeldung → BC-Upsert mit der Absence-ID als externe ID → `Synced` → InApp-Nachricht an die Zuständigen → eine Kollegin sieht nur „abwesend“, die Filialleitung die Art.
  2. BC antwortet mit 429 → Status `Pending` → Erfolg beim Retry.
  3. Validierungsfehler → `Failed`, genau ein Versuch, Fehlercode ohne Namen oder BC-Text.

## Abweichungen vom Plan

- **Kein Push im Test:** Workforce fordert für Krankmeldungen nur InApp an (datenschutzkonservativ); geprüft wird die InApp-Nachricht, die Schutzregel selbst deckt ein Test in Notifications ab.
- **Kein Entra für Personen und Dienste** (Entscheidungen 13, 18); Entra-Paket und -Skripte sind entfallen.
- **Emulator:** Lebensdauer höchstens 1 Stunde, mindestens eine Subscription je Topic (deshalb gibt es das Topic für `ShiftUncovered` erst mit dem ersten Herausgeber).

## Nicht verifiziert

Bicep gegen eine echte Azure-Umgebung, Betrieb mehrerer Instanzen (die Hintergrunddienste laufen je Runde nur auf einer Instanz über eine Datenbank-Sperre, die Forwarded Headers werden aus der Konfiguration gelesen: beides getestet, aber noch nie mit zwei echten Instanzen hinter einem Proxy), Login und Sync gegen echtes BC.

## Offene `TODO(BC-Modell)`

Vollständig mit `grep -rn "TODO(" src`. Kern: Mitarbeiter- und Filialfelder (getrennt Vor- und Nachname, Filialadresse, Öffnungszeiten), Abwesenheiten (API-Seite, Art → BC-Code, Genehmigungsstatus, Fehlerformat), Spiegelung von Nicht-Verfügbarkeit und Arbeitszeitkonto. Die PIN-Prüfung ist umgesetzt (Entscheidung 19) und läuft bisher nur gegen WireMock.

## Vorschlag nächste Fachfunktion

**Dienstplan-Einsicht** (nur lesen) vor der Zeiterfassung: Sie nutzt dieselben Kopien, Rechte und Sichtbarkeitsregeln, braucht kein neues Schreibmodell nach BC und gibt `ShiftUncovered` einen echten Konsumenten. Die Zeiterfassung hat höhere Datenschutz- und BC-Spiegelungsanforderungen.
