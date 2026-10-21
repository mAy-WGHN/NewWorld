# TODO: HrTool-API

## ⏸ Pausiert (2026-10-20): hier geht es weiter

Zuletzt gearbeitet an **Block A** (A1–A4 aus der Reihenfolge „Sofort machbar“). **Der Code ist fertig und getestet (169 Tests grün, Docker nötig); offen ist nur noch die Dokumentation.**

**Fertig (Code und Tests)**
- **A1** Die 24 Nichtverfügbarkeitsarten stehen als Stamm im Repo (`Data/Seed/absence-types.json`) und werden beim Start angelegt; nur `URLAUB` hat `RequiresApproval`. Die Hintergrundaufgabe heißt jetzt `ReferenceDataMaintenance` (vorher `WorkplaceMaintenance`; Konfiguration `ReferenceData__MaintenanceEnabled`).
- **A2** Controller und CRUD-Rechte: `employee` (lesen, ändern, deaktivieren; nur HR-Felder änderbar, Anlegen nur per Import), `absence_type`, `shift_pattern` (`/api/employees`, `/api/absence-types`, `/api/shift-patterns`).
- **A3** `absence` (`/api/absences`): Genehmigungsstatus und Ablehnungsgrund nur bei Arten mit `RequiresApproval` (sonst 422); „Erfasst von“ kommt aus dem Token; Genehmigen ist ein `absence:update` (keine Zusatzrechte, wie gewünscht).
- **A4** Import der BC-Exporte: `make import` bzw. `dotnet run --project HrTool-API.csproj -- import [ordner] [--dry-run]` (Standard `docs/spreadsheets`). Eigener xlsx-Leser ohne Zusatzbibliothek. Wiederholbar, in einer Transaktion, Meldungen nennen nur Nummern und Codes, nie Namen. Mitarbeiter-Stammdaten aus BC werden aktualisiert; Arbeitsort, Rolle, Vertrag und Region setzt der Import nur, solange sie leer sind; Pläne werden nur angelegt.
- **An den echten Exporten geprüft** (Wegwerf-Datenbank, Container wieder entfernt, nichts bleibt zurück): 769 Mitarbeiter (3 ohne Namen übersprungen), 717 Kassenprofile (61 Kassierer-IDs ohne Mitarbeiter übersprungen), 529 Schichtmuster, 2293 Pläne (54 Entwurf, 2239 bestätigt), 81 Beschäftigungsverhältnisse (nur `VERKAUF` ohne Stunden), 4 Rollen. Zweiter Lauf: nichts neu.

**Noch zu tun, bevor Block A als abgeschlossen gilt (nur Dokumentation)**
1. `CLAUDE.md`: Abschnitt „Noch offen“ (erster Punkt ist veraltet: Controller, Rechte und Import stehen für Mitarbeiter, Abwesenheiten, Arten, Schichtmuster, Arbeitsorte) bereinigen. Die Routen von `/api/employees`, `/api/absences`, `/api/absence-types`, `/api/shift-patterns` und den Import-Befehl (inkl. Verhalten bei Wiederholung, `--dry-run`, Warnungen) im Abschnitt „Schnittstelle“ ergänzen. Im Datenmodell vermerken: Kassenprofile haben noch keine API.
2. `README.md`: `make import` und den Befehl in „Befehle“ aufnehmen; Hinweis, dass die Exporte Personaldaten enthalten und nicht ins Repo gehören.
3. Diese Datei: die Punkte unter 3.1 (employee, absence, absence_type, shift_pattern) und 3.2 (Import) als erledigt markieren; offen bleiben `cashier_profile` und das Urlaubscockpit. Den Stand-Block „1. IST-Stand“ (Entities/Routen/Rechte/Tests) aktualisieren (169 Tests).

**Offene Fragen an euch (unverändert, blockieren Block B)**
- Weitere Genehmigungsstatus des Urlaubsantrags (belegt ist nur `Vorgesetzter akzeptiert`) und ob `SONDERURLAUB`, `UNBEZ. U`, `URLAUBSANTRAGSZEITR` auch genehmigt werden müssen.
- Berechtigungskonzept (wer sieht/entscheidet was) für Zusatzrechte und das Urlaubscockpit.
- Wo die Mitarbeiter-Exporte laufen dürfen (nur lokal oder auch Test/Produktion); `BS`/`SABA` ohne Filiale; ob `HO` mit Store-Nr. `95` bei Rollen-Rechte existiert.

**Danach (Block B/C)**: Urlaubscockpit, Zusatzrechte, Schichten/Schichtdefinitionen aus BC, „eigene Daten“, Ereignisse, Betrieb (siehe unten).

**Hinweise zum Stand**
- Die Migration `InitialCreate` wurde mehrfach neu erzeugt und ist nirgends angewendet außer in Tests und einer Wegwerf-Datenbank. Auf einer lokalen Datenbank, auf der eine frühere Fassung lief, vorher aufräumen.
- Im Temp-Ordner der Sitzung liegen TSV-Auszüge der Exporte (mit Personaldaten); sie gehören nicht ins Repo und können gelöscht werden.
- `git` ist hier nicht eingerichtet; es gibt keine Commits. Die Änderungen liegen nur im Arbeitsverzeichnis.

Stand: 2026-10-20. Grundlage: der Code in diesem Repo, die Repos `Portal`, `Rollen-Rechte`, `BC-Anbindung`, `Benachrichtigungen` und die Exporte in `docs/spreadsheets`.
Die Exporte sind **Spaltenköpfe plus Beispieldaten**; was daraus abgeleitet ist und was nicht belegt ist, steht jeweils dabei.

## 1. IST-Stand

### Fertig und getestet (101 Tests, Docker nötig)

**Plattform-Anbindung (nach dem Muster des Portals)**
- [x] JWT-Prüfung (Token von Rollen-Rechte, RS256 oder HS256-Fallback), 401/403/503-Verhalten, Rechte und Scope über `GET /api/me/privileges` (10 s Cache)
- [x] Rechtekatalog wird aus den Entities generiert und beim Start per `PUT /api/apps/hr/catalog` angemeldet (Wiederholung alle 30 s)
- [x] Filialverzeichnis von Rollen-Rechte (`GET /api/apps/hr/locations`, 60 s Cache, bei Ausfall letzte Liste)
- [x] Envelope `{success, data}`, 422-Validierung, Fehler-Handler, Rate-Limit, CORS, Forwarded-Headers, `/health`, OpenAPI (nur Development)
- [x] Dockerfile, docker-compose (Postgres 5435, API 8300), Makefile, `.env.example`, `CLAUDE.md`, `README.md`

**Daten (EF Core, snake_case, Migration `InitialCreate`)**
- [x] 10 Entities: `Workplace` (Stamm mit 42 Arbeitsorten), `PlanningRole`, `EmploymentContract`, `Employee`, `CashierProfile`, `ShiftPattern`, `AbsenceType`, `Absence`, `Schedule`, `Shift`
- [x] Scope-Filter nach Filiale (über den Arbeitsort, `Workplace.LocationId`) auf `Employee`, `CashierProfile`, `Absence`, `Schedule`, `Shift`; leerer Scope sieht nichts
- [x] Eindeutigkeiten (Personalnummer, Plannummer, Plan je Arbeitsort und Monat, Arbeitsort ↔ Filiale 1:1), Löschverhalten, `UpdatedAt` automatisch

**API**
- [x] `GET /api/ping`, `GET /health`
- [x] `/api/schedules`: Liste (Filter, Paging), Detail, Anlegen, Ändern (Status, Budget), Löschen (nur Entwurf)
- [x] `/api/schedules/{id}/shifts`: Liste (Filter), Anlegen, Ändern, Löschen
- [x] `GET /api/schedules/{id}/matrix`: Dienstplan-Ansicht mit Rand, Soll, Geplant, Geplant %, „anderswo eingeteilt“ (`away`)
- [x] Stundenrechnung `ScheduleCalculator` (Pause > 6 h = 30 min, über Mitternacht, Abwesenheit mit Zeiten zählt, Soll = Wochenstunden × Tage / 7)

**Berechtigungen**
- [x] `schedule:read/create/update/delete` (scopbar) und `workplace:read/update` (Stammdaten). Schichten haben kein eigenes Recht (lesen = `schedule:read`, ändern = `schedule:update`).
- [x] Tests prüfen für jede geschützte Route: ohne Token 401, ohne Recht 403 mit Meldung, mit anderem Recht 403, mit genau dem Recht kein 401/403; Katalog und Routen stimmen überein

### Nicht vorhanden
- Keine Controller und keine Rechte für Mitarbeiter, Abwesenheiten, Schichtmuster, Nichtverfügbarkeitsarten, Kassenprofil
- Kein Import aus BC oder aus den Exporten; **befüllt sind nur die Arbeitsorte** (Stamm beim Start), alle anderen Tabellen sind leer
- Keine Anbindung an BC (BC-Anbindung bietet bisher nur `employees/verify` und `stores`)
- Keine Ereignisse an Benachrichtigungen, keine Outbox
- Keine Oberfläche für „eigene Daten“ (Mitarbeiter sieht seinen eigenen Dienstplan)
- Kein Deployment, keine Produktions-Konfiguration

## 2. Blocker: Auskünfte und Daten, die ich von euch brauche

- [x] **Kürzel ↔ Filiale**: erledigt über `Arbeitsorte.xlsx` (42 Arbeitsorte mit Store-Nr., Kostenstelle, Region; eingebettet und von `ReferenceDataMaintenance` angelegt). **Noch offen:** `BS` (Berufsschule) und `SABA` (Samurai Bar) haben keine Store-Nr. (`SABA` hat einen Plan), `HO` hat Store-Nr. `95` (in Rollen-Rechte prüfen, ob dort eine Filiale `95` existiert), und die Filialen werden erst aufgelöst, wenn Rollen-Rechte sie über `store_code` kennt.
- [ ] **BC-Webdienste freigeben**: Dienstpläne (Liste und Matrix), Schichtmuster (samt Zeilen), Nichtverfügbarkeitsarten, Schichtdefinitionen. Veröffentlicht sind heute nur `CS_Employees`, `StaffList`, `EmplNonAvailability`, `GetStaff`/`GetStaffV2`, `GetWorkShift`/`SendWorkShift`, `Zeiterfassung`.
- [ ] **Weitere Genehmigungsstatus**: Belegt ist nur `Vorgesetzter akzeptiert` (HR Urlaubscockpit). Genehmigung gibt es nur für Urlaub; alle anderen Nichtverfügbarkeiten gelten automatisch als genehmigt. Es fehlen die übrigen Werte (z. B. offen, genehmigt, abgelehnt) und wie sie heißen, bevor `ApprovalStatus` ein Enum werden kann.
- [ ] **Schichtdefinitionen** hinter Codes wie `VKA1` (Zeiten, Rolle, Pause). Ohne sie zählt ein Schichtcode ohne Zeiten 0 Stunden.
- [ ] **Schichtmuster-Zeilen** (Tage/Zeiten) und ihre Zuordnung zu Mitarbeitern. Der Export enthält nur Code, Beschreibung, Musterart, Wochenzahl.
- [ ] **Unklare Zellen im Dienstplan-Export**: `0/1`, `2/1`, `5/1` (nur bei Mitarbeitern ohne Stunden), `OK`, `U per al…`; die ca. 46 Mitarbeiter anderer Filialen unter dem Trennstrich `--------`.
- [ ] **Gehören die Rand-Schichten (6 Tage vor/nach dem Monat) wirklich zu den Nachbarplänen?** Die API setzt das voraus (Schichten nur im Monat des Plans). Falls BC sie am selben Plan führt, muss die Datumsprüfung und der Import angepasst werden.
- [ ] **Pause über 9 h** (die API nimmt 45 min, das gesetzliche Minimum; im Export gibt es keine solche Schicht).
- [ ] **Berechtigungskonzept**: Wer darf was sehen? (Voraussetzung für Zusatzrechte, siehe 4.)

## 3. Nächste Schritte (Reihenfolge)

### 3.1 Controller und Rechte für die übrigen Entities (nur CRUD)
Je Bereich: Attribut `[PrivilegeResource]` an der Entity, Controller mit `[HasPrivilege<…>]`, Tests (Katalog ↔ Routen laufen automatisch mit).
- [ ] `employee` (read, update, deaktivieren; Anlegen kommt per Import). **Voraussetzung für Namen im Plan**: Heute liefert die Matrix Namen der Mitarbeiter des Plans, die Schicht-Routen nur IDs.
- [ ] `absence` (read, create, update, delete) samt Prüfungen (von ≤ bis, Zeiten nur zusammen, Tage/Urlaubstage plausibel)
- [ ] **HR Urlaubscockpit** (`GET` der Urlaubsanträge nach Filiale/Zeitraum, Standard nur Status `Vorgesetzter akzeptiert`, Option „alle Status“; Entscheidung genehmigen/ablehnen mit `RejectionReason`): braucht die vollständige Statusliste und das Berechtigungskonzept (wer entscheidet?). Die Spalten des Cockpits (Mitarbeiter, Arbeitsort, Region, von/bis, Arbeitstage, Urlaubstage, Status, beantragt am, Bemerkung, Ablehnungsgrund) sind im Modell abgedeckt.
- [ ] `AbsenceType.RequiresApproval` beim Import der Arten setzen (nur `URLAUB`); noch offen: gilt das auch für `SONDERURLAUB`, `UNBEZ. U`, `URLAUBSANTRAGSZEITR`?
- [ ] `absence_type`, `shift_pattern` (Stammdaten, `DefinesReach = false`, nicht scopbar)
- [x] `workplace` (`GET`/`PATCH /api/workplaces`, Filiale zuordnen) erledigt
- [ ] `cashier_profile` (eigener Bereich wegen sensibler Kassenrechte)
- [ ] `planning_role`, `employment_contract`: kein Recht, nur Import

### 3.2 Import
- [ ] Import-Weg festlegen: BC-Anbindung (neue Endpunkte) oder einmalig aus den Exporten
- [ ] Reihenfolge: Arbeitsorte (erledigt, Stamm eingebettet) → Rollen, Verträge (Wochenstunden aus dem Code lesen: `TZ 15,25H`, `TZ 21,25`, `TZ 25,50 H`) → Mitarbeiter (+ Kassenprofil) → Nichtverfügbarkeitsarten → Schichtmuster → Pläne → Schichten
- [ ] Zellen der Dienstplan-Matrix zerlegen: Zeiten `9-19`, `0530-1430`, `14-1715`, `16-0` (Ende 0 = Mitternacht); Absence-Code/Kürzel (`URLAUB 4-9`, `U`, `FT`); Freitext in `Remark`; `YY …`-Zellen verwerfen (sind in BC abgeleitet)
- [ ] Monatsnamen `April  2021` → Jahr/Monat; Excel-Seriennummern (`Zuletzt geändert`, `Periodenanfang`) → Datum
- [ ] Idempotent (Schlüssel: Personalnummer, Plannummer, Code) und mit Protokoll der nicht lesbaren Zeilen
- [ ] Personenbezogene Exporte nicht ins Repo/in die Logs übernehmen

### 3.3 Dienstplan nachschärfen
- [ ] Liste: `plannedHours` je Plan (heute nur im Einzelplan), ggf. aggregiert in der DB
- [ ] Status-Wechsel `draft` → `confirmed`: Regeln festlegen (ab wann nicht mehr bearbeitbar?). Heute: bestätigte Pläne bleiben bearbeitbar, nur Entwürfe sind löschbar.
- [ ] Doppelte Schichten desselben Mitarbeiters am selben Tag verhindern oder zulassen (heute ohne Prüfung, nur Index)
- [ ] Überschneidungen und Ruhezeiten als `warnings` berechnen (Spalte „Warnungen“ im Export; dort überall 0)
- [ ] Beträge (Total Geplant/Tatsächlich in der Übersicht) und tatsächliche Stunden: kommen aus Lohnsätzen und Zeiterfassung in BC, nicht aus diesem Dienst
- [ ] PATCH kann Felder nicht auf `null` setzen (Konvention); für Schichten ggf. eigene Endpunkte zum Leeren

## 4. Später (auf eure Entscheidung)
- [ ] **Zusatzrechte** erst nach dem Berechtigungskonzept (bewusst noch nicht vorhanden): `publish`, `approve`, Abwesenheitsgründe lesen. Bis dahin sieht jeder mit `absence:read` auch Krank/Mutterschutz/Reha (Gesundheitsdaten, DSGVO Art. 9), also nur an passende Rollen vergeben.
- [ ] **Eigene Daten**: Ein Mitarbeiter soll seinen eigenen Dienstplan/seine Abwesenheiten sehen. Entweder Scope-Dimension `employee` (braucht eine Änderung in Rollen-Rechte, `ScopeDimensions` kennt nur `location` und `report`) oder „Meine …“-Routen, die nur die Anmeldung verlangen und über `Employee.UserId` filtern.
- [ ] **Ereignisse** `schedule.published` / `schedule.changed`: Handler in Benachrichtigungen (kennt heute nur `report.created`), hier ein Client nach dem Muster `HttpReportEvents` des Portals, später Outbox und `GET /api/changes?since=`
- [ ] **Dienstplan-Anzeige im Portal**: Das Portal ruft `GET /api/schedules…` mit dem Token des Users; Abstimmung der Antwortformate
- [ ] Schichttausch-Anträge (im Export gibt es nur die Freigabe `Employee.ShiftSwapEnabled`)
- [ ] `GET /api/access/me?app=…` statt `/api/me/privileges`, sobald Rollen-Rechte es anbietet; gemeinsames Rechte-Paket statt kopiertem `Authentication/` (derzeit identisch mit dem Portal, Änderungen dort mitziehen)

## 5. Betrieb und Aufräumen
- [ ] In Rollen-Rechte den Schlüssel `Apps__Keys__hr` setzen und hier `Access__ApiKey` eintragen; öffentlichen JWT-Schlüssel (`Jwt__PublicKey`) hinterlegen
- [ ] Nach dem ersten Start prüfen, dass die HR-Rechte in der Rollen-Matrix erscheinen, und Rollen zuweisen
- [ ] Produktions-Datenbank, Connection String, Azure-App (`APP_NAME`/`RESOURCE_GROUP` im Makefile prüfen), Migration von Hand (`dotnet ef migrations script`), Deployment-Slot
- [ ] `InitialCreate` wurde mehrfach neu erzeugt und ist **nirgends außer in den Tests angewendet** (soweit mir bekannt). Auf einer Datenbank, auf der eine frühere Fassung lief, vorher aufräumen. Ab dem ersten echten Einsatz nur noch Folgemigrationen.
- [ ] Lokale `.env` enthält noch alte `BusinessCentral__*`-Werte (werden nicht mehr gelesen) und kann bereinigt werden; ggf. dort Secrets rotieren, falls sie je außerhalb des Rechners lagen
- [ ] `Portal/CLAUDE.md` und `PLATTFORM-SCHNITTSTELLEN.md` führen das HR-Tool noch als „nicht angebunden“
- [ ] `docs/spreadsheets` enthält echte Personaldaten (git-ignoriert); prüfen, ob sie dort liegen bleiben dürfen
- [ ] `docs/BC_LOGIN_AUTHENTIFIZIERUNG.md` ist überholt (Hinweis steht oben), kann archiviert werden
- [ ] Frontend: Anmeldung bei Rollen-Rechte, Dienstplan-Routen beim HR-Tool (Port 5129 lokal)

## 6. Bekannte Einschränkungen und Risiken
- `Infrastructure/ProbeController` (nur in den Tests) existiert, weil es bisher wenige geschützte Routen gab; kann entfallen, sobald genug echte Routen die Rechteprüfung abdecken
- Die Matrix zeigt Namen von Mitarbeitern anderer Filialen, die im Plan eingeteilt sind, auch ohne `employee:read`; und `away` zeigt Datum, Zeit und Filiale ihrer Einsätze anderswo (ohne Scope-Filter). Das entspricht der BC-Ansicht, sollte fachlich bestätigt werden.
- Die Matrix lädt alle Schichten des Monats samt Rand in den Speicher (ein Monatsplan hat einige hundert); bei sehr großen Filialen Last beobachten
- Antworten für Schichten und Matrix haben kein Paging
- Rate-Limit und Rollen-Rechte-Cache liegen im Speicher je Instanz
- Datenmodell stützt sich auf **Exporte mit Beispieldaten**; Wertebereiche (Rollen, Regionen, Beschäftigungscodes, Musterarten, Genehmigungsstatus) können in BC mehr Werte haben
