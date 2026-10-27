# Phase 6 – Workforce API (Abwesenheiten)

Stand: 2026-10-23. Gebaut: `Workforce.Core/Absences` (Regeln, ohne Infrastruktur), `Workforce.Infrastructure` (Kopien, Handler, Outbox, Migration), `Workforce.Api` (Endpunkte). Tests: Core 129, Api 24, Contracts 11; gesamte Solution grün, Build ohne Warnungen.

## Fachregeln
- **Krankmeldung:** von, bis (offenes Ende erlaubt), „AU liegt vor“ ja/nein, kein Freitext, keine Genehmigung. Offene Krankmeldung kann beendet werden; Austritt beendet sie zum Austrittsdatum.
- **Urlaub:** Antrag → Genehmigungskette über `Shared.Approvals` (Vorgesetzte laut Access, Thema `absence.approval`); Rücknahme auch nach Genehmigung (nur vor Beginn).
- **Urlaubskonto:** Anspruch pro Jahr manuell (HR), Antrag über Rest → fachlicher Fehler (422); Zeiträume über den Jahreswechsel werden je Jahr gezählt.
- **Keine Überschneidungen** (409). Ausnahme: Krankheit im genehmigten Urlaub → Urlaub für die betroffenen Tage storniert und gutgeschrieben.
- **Arbeitstage** Mo–Sa konfigurierbar, **Feiertage** als austauschbarer Dienst (bundesweit + Land).
- **Version:** jede Änderung erhöht `Version`; sie geht in `SyncAbsenceToBc`. Sync-Status (`Pending`/`Synced`/`Failed`) pro Absence, Aktualisierung durch `BcSyncSucceeded`/`BcSyncFailed` (nur aktuelle Version zählt).
- **Sichtbarkeit des Grundes** (Autorisierung, nicht Frontend): Kolleginnen/Kollegen mit `absence:read-availability` sehen nur „Absent“ (Krankheit und genehmigter Urlaub sehen gleich aus, offene Anträge gar nicht); Typ/Status nur mit `absence:read-reason` (Person selbst, Vorgesetzte, HR).
- **Nachrichten je Krankmeldung:** genau drei Outbox-Einträge – `SyncAbsenceToBc` (mit Grund), `EmployeeAvailabilityChanged` (neutralisiert), `SendNotification` (nur InApp, ohne Grund).
- **Löschung:** `EmployeeDataErasureRequested` entfernt Abwesenheiten, Ansprüche, Genehmigungen, Kopien, Berechtigungskopie und Outbox-Einträge zur Person.

## Endpunkte
| Methode / Pfad | Recht |
| --- | --- |
| `GET /api/me/absences`, `GET /api/me/vacation-balance` | `absence:read-own` |
| `POST /api/me/absences/sick`, `POST /api/me/absences/vacation` | `absence:create-own` |
| `POST /api/me/absences/{id}/withdraw`, `…/end` | `absence:update-own` (fremde Id = 404) |
| `GET /api/stores/{storeId}/absences` | `read-availability` / `read-reason` (Store-Scope) |
| `GET /api/employees/{id}/absences`, `…/vacation-balance` | `absence:read-reason` (Personen-Scope); Sync-Status mit `absence:read-sync` |
| `POST /api/employees/{id}/absences/sick` | `absence:create` |
| `GET /api/approvals/pending` | `absence:approve` |
| `POST /api/absences/{id}/approve` / `reject` | `absence:approve` auf die Person **und** Mitglied der Kette |
| `PUT /api/employees/{id}/vacation-entitlements/{year}` | `absence:manage-entitlement` (vertraglicher Jahresanspruch) |
| `GET/POST /api/employees/{id}/vacation-extra-days`, `DELETE …/{extraId}` | `absence:manage-entitlement` (Zusatztage, siehe `urlaubsberechnung.md`) |
| `GET/PUT /api/vacation-rules` | `absence:manage-vacation-rules` (Verfall der Zusatztage, Wartezeit, Rundung; Standard: nie / keine / auf halbe Tage auf) |
| `GET/POST /api/employees/{id}/vacation-entitlement-changes`, `DELETE …/{changeId}` | `absence:manage-entitlement` (Änderung des Jahresanspruchs im Jahr) |
| `GET/PUT /api/stores/{id}/holiday-opening`, `PUT /api/stores/holiday-opening` | `absence:manage-holiday-opening` (Store-Scope) |
| `GET /internal/absences/{id}` | Service Role `Workforce.AbsenceDetails.Read` |

Fehlerabbildung: NotFound/UnknownPerson 404, InvalidPeriod/AfterExit/NoEntitlement/InsufficientBalance 422, Overlap/NoApprover/InvalidState/AlreadyStarted 409, NotAllowed 403.

## Offene Annahmen / TODOs
1. **Bundesland** für Feiertage: derzeit ein Standard (`Workforce:HolidayState`), nicht pro Filiale – `TODO(Entscheidung)`.
2. **Urlaubsanspruch** manuell gepflegt – Herkunft aus BC offen, `TODO(BC-Modell)`; ebenso das BC-Datenmodell für `SyncAbsenceToBc`.
3. **Genehmigungskette:** mehrere Genehmiger einer Stufe sind Alternativen (einer genügt) – bitte bestätigen.
4. **Krankheit im beantragten (noch nicht genehmigten) Urlaub** storniert den Antrag vollständig.
5. **Verlängerung einer Krankmeldung** nicht gesondert modelliert (nur Beenden einer offenen).
6. Empfänger der Krankmeldungs-Benachrichtigung = Genehmiger des Themas `absence.approval`.
7. Das Recht `absence:read` entfiel zugunsten von `read-availability`/`read-reason` (eine Zwischenstufe würde über den Status „gemeldet“ den Typ verraten).
8. Rolle „Public User“ braucht die Own-Rechte per Verwaltung in Access.
9. Lokaler AppHost-Lauf (damals mit Fake-Token, seit Entscheidung 18 mit signierten Tokens).
