# Workforce: Abwesenheiten (Regeln und Endpunkte)

Ursprünglich Phase 6 (2026-10-23), überarbeitet 2026-11-10. Code: `Workforce.Core/Absences` (Regeln ohne Infrastruktur), `Workforce.Infrastructure`, `Workforce.Api`. Berechnung des Urlaubs: `urlaubsberechnung.md`; Krankmeldung mit AU: `krankmeldung-au.md`.

## Fachregeln

- **Krankmeldung:** erster und letzter Tag (das Ende ist Pflicht), Art Erst- oder Folgekrankmeldung, kein Freitext, keine Genehmigung (Entscheidung 42). Altbestand ohne Ende kann beendet werden. Ein Austritt beendet sie zum Austrittsdatum.
- **Urlaub:** Antrag mit Genehmigungskette über `Shared.Approvals`. Genehmigen darf nur, wer für das Thema `absence.approval` zuständig ist; einer von mehreren Zuständigen genügt (Entscheidung 17). Zurückziehen auch nach Genehmigung, solange der Urlaub nicht begonnen hat.
- **Urlaubskonto:** Anspruch von HR gepflegt; ein Antrag über den Rest ist ein fachlicher Fehler (422). Zeiträume über den Jahreswechsel zählen je Jahr.
- **Keine Überschneidungen** (409). Ausnahme: Krankheit im genehmigten Urlaub storniert den Urlaub für die betroffenen Tage und schreibt ihn gut (als „KU“, Entscheidung 32). Krankheit im noch nicht genehmigten Antrag storniert den Antrag ganz.
- **Arbeitstage Mo–Sa, Feiertage** für Bayern, mit Augsburger Friedensfest und Filialen mit Feiertagsöffnung (Entscheidungen 17, 23).
- **Version:** Jede Änderung erhöht `Version`; sie geht in `SyncAbsenceToBc`. Der BC-Sync-Status (`Pending`/`Synced`/`Failed`) kommt über `BcSyncSucceeded`/`BcSyncFailed`, nur die aktuelle Version zählt.
- **Sichtbarkeit des Grundes** (per Autorisierung, nicht im Frontend): `absence:read-availability` sieht nur „abwesend“ (Krank, Urlaub und KU als solche, andere Arten nur „nicht verfügbar“); Art und Status nur mit `absence:read-reason` (die Person selbst, Zuständige, HR).
- **Nachrichten je Krankmeldung:** genau drei Outbox-Einträge: `SyncAbsenceToBc` (mit Grund), `EmployeeAvailabilityChanged` (neutralisiert), `SendNotification` (nur InApp, ohne Grund) an die Zuständigen für `absence.approval`.
- **Löschung:** `EmployeeDataErasureRequested` entfernt Abwesenheiten, Ansprüche, Genehmigungen, Kopien, Berechtigungskopie und Outbox-Einträge der Person.

## Endpunkte

| Pfad | Recht |
| --- | --- |
| `GET /api/me/absences`, `/api/me/vacation-balance`, `/api/me/holidays` | `absence:read-own` |
| `POST /api/me/absences/sick`, `/vacation` | `absence:create-own` |
| `POST /api/me/absences/{id}/withdraw`, `/end` | `absence:update-own` (fremde ID = 404) |
| `GET /api/stores/{id}/absences` | `read-availability` oder `read-reason` (Filiale) |
| `GET /api/employees/{id}/absences`, `/vacation-balance` | `absence:read-reason` (Person); Sync-Status mit `absence:read-sync` |
| `POST /api/employees/{id}/absences/sick` | `absence:create` |
| `GET /api/approvals/pending`, `POST /api/absences/{id}/approve`, `/reject` | `absence:approve` und Mitglied der Kette; `pending` liefert auch die Hauptfiliale der Person |
| `PUT /api/employees/{id}/vacation-entitlements/{year}`, `…/vacation-extra-days`, `…/vacation-entitlement-changes` | `absence:manage-entitlement` |
| `GET/PUT /api/vacation-rules` | `absence:manage-vacation-rules` |
| `GET/PUT /api/stores/{id}/holiday-opening`, `PUT /api/stores/holiday-opening` | `absence:manage-holiday-opening` (Filiale) |
| `GET /internal/absences/{id}` | Service Role `Workforce.AbsenceDetails.Read` |

Fehlerabbildung: NotFound/UnknownPerson 404; InvalidPeriod, AfterExit, NoEntitlement, InsufficientBalance, WaitingPeriod 422; Overlap, NoApprover, InvalidState, AlreadyStarted 409; NotAllowed 403.

## Offen

- BC-Datenmodell für `SyncAbsenceToBc` (`TODO(BC-Modell)`), Details in `bc-zeiterfassung-schnittstelle.md`.
- Die Rolle „Public User“ braucht die Own-Rechte (in `docs/rollen.json`, Import in Access).
- Feiertag je Datum (z. B. Ostern offen, Weihnachten zu) ist nicht gebaut; Mariä Himmelfahrt zählt nicht.
