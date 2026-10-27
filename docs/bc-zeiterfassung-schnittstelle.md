# BC-Schnittstelle „Zeiterfassung“ – Auswertung gegen unsere Annahmen

Stand: 2026-10-26 · Schritt 1 des Weiterentwicklungsplans (nach Phase 7).

**Quelle und Verlässlichkeit.** Alles hier ist aus dem **Aufruferverhalten** des HR-Tools (`wghn-hr-tool`: `src/services/*.ts`, `src/types/*.ts`, `api/soap-proxy`) und des Archivs (`Archiv/BC-Anbindung`) abgeleitet. Die AL-Seite von BC wurde nicht gesehen, es lief nichts gegen BC. Die Aussagen über Parameter und Antwortfelder sind daher belastbar (so ruft das produktive HR-Tool auf), alles darüber hinaus (Regeln in BC, Fehlerfälle, Berechtigungsprüfung) ist **offen** und als Frage markiert.

## 1. Aufrufkonvention (gilt für alle Funktionen)

- SOAP 1.1 an `…/WS/{Firma}/Codeunit/Zeiterfassung`, `SOAPAction` `urn:microsoft-dynamics-schemas/codeunit/Zeiterfassung:<Funktion>`, Namespace `urn:microsoft-dynamics-schemas/codeunit/Zeiterfassung`.
- Antwort: SOAP-Envelope mit `<return_value>`, darin **JSON als Text**. Ein SOAP-Fault (`faultstring`) ist ein Fehler.
- Daten als `YYYY-MM-DD`, ein leeres Datum ist `0001-01-01`. Wahrheitswerte kommen teils als Text (`"true"`/`"false"`) statt als JSON-Boolean.
- Personen werden über die **Personalnummer** (`employeeNo`, Vorgesetzte `supervisorNo`/`approverNo`) adressiert, nicht über eine GUID.
- **Das HR-Tool hängt an jeden Aufruf den Parameter `password` (die PIN der angemeldeten Person) an** (`soap-client.ts`). Ob BC die PIN bei jeder Funktion prüft oder nur bei `GetStatus`, ist unbekannt (siehe Frage F1).

## 2. Die Funktionen

Datenklasse nach Datenschutz-Regeln (Entwurf): 1 = Organisationsdaten, 2 = sensible Fakten (Abwesenheit, Zeiten). „Recht“ ist der Vorschlag für den Access-Katalog (Abschnitt 5).

| Funktion | Parameter | Antwort (Felder) | Zweck | Unser Gegenstück | Klasse | Recht (Vorschlag) |
| --- | --- | --- | --- | --- | --- | --- |
| `GetStatus` | `employeeNo`, `queryDate`, `password` | `success`, `employeeName`, `date`, `currentStatus`, `nextAction`, `isLoggedIn`, `isTravelActive`, `hasWorkToday` | Anmeldung/PIN-Prüfung und Tagesstatus der Zeiterfassung | **umgesetzt** (PIN-Login, Entscheidung 19) | 1 (Login) / 2 (Status) | – (Login) |
| `GetVacationBalance` | `employeeNo` | `success`, `employeeName`, `vacationDays`, `vacationCarried`, `vacationTaken`, `vacationPending`, `vacationRemaining`, `flexible`, `message` | Urlaubskonto | `VacationAccount` (heute manuell gepflegt) | 2 | `vacation:read-own-balance`, `vacation:read-balance` |
| `GetVacationAvailability` | `employeeNo`, `startDate`, `endDate` | `availability[]` je Tag: `date`, `availability` (`green`/`red`/`gray`), `teamCount`, `isWeekend`, `hasScheduledWork` | Ampel für Urlaubswünsche (Besetzung im Team) | Besetzungsprüfung, bei uns noch nicht vorhanden | 2 (aggregiert) | `vacation:read-availability` |
| `GetVacationThresholds` | `employeeNo` | `locationThreshold`, `regionThreshold`, `workLocation`, `workRegion` | Schwellen (Mindestbesetzung) je Filiale/Region | nicht vorhanden | 1 | `vacation:read-thresholds` |
| `RequestVacation` | `employeeNo`, `startDate`, `endDate`, `vacationComment` | `success`, `message`, `employeeName`, `daysRequested`, `startDate`, `endDate` | Urlaubsantrag stellen | `RequestVacation` in Workforce | 2 | `vacation:request-own` |
| `GetMyVacationRequests` | `employeeNo` | Liste: `dateFrom`, `dateTo`, `days`, `status` (`requested`/`supervisorApproved`/`approved`/`rejected`), `comment`, `requestedDate`, `supervisorApprovedDate`, `finalApprovedDate`, `rejectionReason`, `rejectedDate`, `flexible` | eigene Anträge | Antragsliste in Workforce | 2 | `vacation:read-own` |
| `CheckSupervisorStatus` | `employeeNo` | `isSupervisor`, `employeeCount` | Ist die Person Vorgesetzte(r) und für wie viele? | Zuständigkeit `absence.approval` in Access (manuell) | 1 | – (Quelle für Access-Vorschlag) |
| `GetPendingApprovalsForSupervisor` | `supervisorNo` | Liste: `employeeNo`, `employeeName`, `dateFrom`, `dateTo`, `days`, `actualDates[]`, `comment`, `requestedDate`, `flexible` | offene Anträge zur Genehmigung | Genehmigungsliste in Workforce | 2 | `vacation:approve` (scopebar) |
| `ApproveVacationRequestAsSupervisor` | `employeeNo`, `approverNo`, `requestDateFrom` | `success`, `message` | Genehmigung | `Shared.Approvals` in Workforce | 2 | `vacation:approve` |
| `RejectVacationRequest` | `employeeNo`, `approverNo`, `requestDateFrom`, `rejectionReason` (max. 250) | `success`, `message` | Ablehnung mit Grund | Ablehnung in Workforce | 2 | `vacation:approve` |
| `GetTeamVacationCalendar` | `supervisorNo`, `startDate`, `endDate` (`0001-01-01` = Standard) | Liste: `employeeNo`, `employeeName`, `dateFrom`, `dateTo`, `status`, `comment`, `workLocation`, `workRegion` | Urlaubskalender des Teams | Team-Sicht in Workforce | 2 | `vacation:read-team` (scopebar) |
| `GetScheduleRange` | `employeeNo`, `startDate`, `endDate` | `schedules[]`: `date`, `workLocation`, `startTime`, `endTime`, `hasSchedule` | Dienstplan einer Person | geplante „Dienstplan-Einsicht“ | 2 | `schedule:read-own`, `schedule:read` |
| `GetTimeEntries` | `employeeNo`, `queryDate` | `entries[]`: `entryTime`, `exitTime`, `duration`, `workLocation`, `isTravelTime`, `isOpen` | Zeitbuchungen eines Tages | Zeiterfassung (später) | 2 | `time:read-own`, `time:read` |
| `RegisterTime` | `employeeNo`, `direction` (`In`, `Out`, `PauseStart`, `PauseEnd`, `TravelStart`, `TravelEnd`) | `success`, `message` | Stempeln | Zeiterfassung (später) | 2 | `time:register-own` |
| `AddComment` | `employeeNo`, `entryDate`, `comment` (max. 250) | `success`, `message` | Kommentar zu einer Zeitbuchung (Freitext) | Zeiterfassung (später) | 2 (Freitext!) | `time:comment-own` |

Nicht im HR-Tool: eine **Krankmeldung**. Es gibt dort keinen Aufruf dafür.

## 3. Was das für unsere `TODO(BC-Modell)` bedeutet

| Unsere Annahme | Befund | Folge |
| --- | --- | --- |
| Abwesenheiten über eine neue API-Seite mit Upsert über externe ID (`Bc:AbsencesPath`) | Für **Urlaub** gibt es bereits die Codeunit-Funktionen (Antrag, Liste, Genehmigung, Ablehnung). Anträge werden über **Personalnummer + Startdatum** angesprochen, nicht über eine ID. | Upsert über unsere `AbsenceId` passt für Urlaub nicht. Siehe D2. |
| Genehmigungsstatus (`AbsenceStatusCodes`) | BC kennt `requested` → `supervisorApproved` → `approved` (Endfreigabe) bzw. `rejected`. Das sind **zwei Genehmigungsstufen**. | Unser Modell hat eine Stufe über `Shared.Approvals`. Siehe D1. |
| Urlaubskonto manuell pflegen, später importieren | `GetVacationBalance` liefert Anspruch, Übertrag, genommen, beantragt, Rest. | Quelle gefunden. Ersetzt die manuelle Pflege und die offene Entscheidung „Herkunft Urlaubsanspruch“, wenn BC führend sein soll. |
| Vorgesetzte pflegt man in Access von Hand | BC kennt Vorgesetzte (`CheckSupervisorStatus`, `GetPendingApprovals…`). | Optional Vorschlagsquelle für Zuständigkeiten, die Verwaltung bleibt in Access (Entscheidung 13). |
| Krankmeldung nach BC (`KRANK`) | Kein Gegenstück im HR-Tool. | Bleibt offen (F4). |
| Dienstplan nur als später | `GetScheduleRange` liefert Standort und Zeiten je Tag. | Quelle für die geplante Dienstplan-Einsicht. |
| Stammdaten (Mitarbeitende, Filialen) über REST-API-Seiten | Das HR-Tool nutzt dafür keine Funktion der Codeunit. Das Archiv liest Filialen über die Codeunit `GetStores` (LSC Store, Felder `No.`, `Name`). | Filialen: Codeunit `GetStores` statt angenommener API-Seite prüfen. Mitarbeitende: weiter unbekannt (F5). |

## 4. Entscheidungen und Fragen

**Zu klären mit den BC-Verantwortlichen**

- **F1 – PIN bei jedem Aufruf?** Prüft BC das Kennwort bei allen Funktionen oder nur bei `GetStatus`? Falls bei allen: BC Integration könnte nur mit der PIN der jeweiligen Person aufrufen. Wir speichern die PIN aber bewusst nicht im Klartext (nur Hash, optional verschlüsselt für den Offline-Login). Dann bräuchte es eine Funktion bzw. ein technisches Konto **ohne** PIN pro Person, dessen Rechte wir selbst (ACL) einschränken. Das ist die wichtigste Frage für alles Weitere.
- **F2 – Berechtigungsprüfung in BC:** Darf jede Person über `employeeNo` fremde Daten abfragen (z. B. `GetVacationBalance` mit fremder Nummer)? Im HR-Tool sorgt nur die PIN dafür, dass man „sich selbst“ ist. Mit einem Dienstkonto gäbe es **keine** Prüfung in BC mehr, die ACL in Access wäre die einzige Schranke.
- **F3 – Zwei Genehmigungsstufen:** Wer gibt in BC die Endfreigabe (`approved`) und wer die Vorgesetztenstufe (`supervisorApproved`)? Was bedeutet `flexible` (flexible Urlaubstage)?
- **F4 – Krankmeldung:** Gibt es eine Funktion oder Tabelle für Krank (KRANK), mit oder ohne AU-Kennzeichen, mit offenem Ende?
- **F5 – Mitarbeiterstammdaten:** Quelle der Mitarbeiterkarte (Hauptfiliale, Funktion, Eintritt/Austritt, Sollstunden) und ob es Änderungszeitstempel gibt.
- **F6 – Stornierung/Rückzug:** Es gibt keine Funktion zum Zurückziehen oder Stornieren eines Antrags. Wie geht das in BC?
- **F7 – Schwellenwerte:** Sind `locationThreshold`/`regionThreshold` harte Sperren oder nur Hinweise (Ampel)?

**Entscheidungen für dich**

- **D1 – Wer ist bei Urlaub führend für die Genehmigung?** (a) Workforce genehmigt (eine Stufe, wie heute gebaut) und schreibt das Ergebnis nach BC, oder (b) BC bleibt führend für den Workflow (zwei Stufen) und Workforce zeigt und stößt nur an. Das Projektziel (Workforce operativ führend, BC gespiegelt) spricht für (a), erfordert dann aber, dass BC die Genehmigung per Funktion übernehmen kann (Aufruf mit `approverNo`).
- **D2 – Schlüssel der Anträge:** Aufrufe sind über (Personalnummer, Startdatum) adressiert. Wir bräuchten dafür entweder eine zusätzliche ID in BC, oder wir speichern pro Antrag (Personalnummer, Startdatum) als BC-Schlüssel und verbieten gleiche Startdaten je Person (Überlappungsregel haben wir ohnehin).
- **D3 – Technisches Konto:** Falls F1 „ja“: Wollen wir von BC ein Dienstkonto/eine PIN-freie Funktion verlangen? Alternative wäre, nur bei aktiver Sitzung mit der Person-PIN zu rufen, was wir ablehnen sollten (PIN gehört nicht in ein Token oder eine Sitzung).

## 5. Rechtemodell (Datenschutz): Vorschlag für den Access-Katalog

Grundsatz: **Alles, was Klasse 2 ist, braucht ein eigenes, einzeln vergebbares Recht, standardmäßig an niemanden.** Access kennt dafür schon Rollen mit Rechten `ressource:aktion` (Wildcards), Scopes (Filiale/Region/Bezirk), befristete Zuweisungen pro Person und Zuständigkeiten je Thema. Neue Rechte werden von Workforce beim Start angemeldet und erst wirksam, wenn sie in einer Rolle stehen.

Bestand (`absence:*`): `read-own`, `create-own`, `update-own`, `read-availability`, `read-reason`, `read-sync`, `create`, `approve`, `manage-entitlement`.

**Zusätzlich für Urlaub und Dienstplan** (jeweils „own“ = nur eigene Daten, ohne Scope; sonst scopebar nach Filiale oder Person):

| Recht | Wirkung |
| --- | --- |
| `vacation:read-own-balance` | eigenes Urlaubskonto sehen |
| `vacation:read-balance` | Urlaubskonto anderer sehen (scopebar) |
| `vacation:request-own` | eigenen Urlaub beantragen/zurückziehen |
| `vacation:request-for` | Urlaub für andere erfassen (scopebar) |
| `vacation:read-availability` | Besetzungsampel (nur aggregiert, keine Namen) |
| `vacation:read-team` | Urlaubskalender mit Namen (scopebar) |
| `vacation:approve` | genehmigen/ablehnen (zusätzlich Zuständigkeit `absence.approval`, Entscheidung 17) |
| `vacation:read-thresholds` / `vacation:manage-thresholds` | Schwellenwerte sehen/pflegen |
| `vacation:manage-entitlement` | Anspruch und Übertrag pflegen (heute `absence:manage-entitlement`) |
| `schedule:read-own` / `schedule:read` | eigenen bzw. fremden Dienstplan sehen (scopebar) |
| `schedule:read-locations` | Standort der Schicht sehen (getrennt von Zeiten) |
| `time:read-own` / `time:read` / `time:register-own` / `time:comment-own` | später (Zeiterfassung) |

Regeln, die ich zusätzlich empfehle und die Entscheidung brauchen:

1. **Prüfung immer lokal in Workforce.** Wenn BC Integration mit einem Dienstkonto ruft (F2/D3), ist Access die einzige Schranke. Deshalb darf **kein** Endpunkt Daten abrufen, ohne die Rechteprüfung „Recht + Filiale/Person“.
2. **Kein Standardrecht für Klasse 2 an Gruppen.** Die Rolle „Public User“ erhält nur `*-own`-Rechte, nie `read-team`, `read-reason` oder `read-balance` anderer.
3. **Zugriffsprotokoll (fehlt heute komplett).** Es gibt weder ein Protokoll der ACL-Änderungen (wer hat wem welches Recht gegeben) noch der Lesezugriffe auf Klasse-2-Daten. Für die Datenschutzauflagen empfehle ich ein **Audit-Log ohne Inhalte** (wer, welches Recht, welche Entitäts-ID, wann, Ergebnis) in Access und Workforce. Das wäre ein neues Fachthema und braucht deine Freigabe (Konzept: eigene Tabelle je Dienst, Aufbewahrung konfigurierbar, nur Auditoren lesen).
4. **Rechte-Änderung nachvollziehbar und begrenzt.** Rollen verwalten dürfen nur HR/Geschäftsführung (Bestand), mit Schutzregeln gegen Rechteausweitung (`AclGuard`-Prinzip: niemand vergibt ein Recht, das er nicht selbst hat). Prüfen, ob die neuen Rechte darunter fallen.
5. **Vier-Augen für Rechte mit hoher Wirkung** (`*:manage-*`, `read-reason`, `read-balance`) als optionale Konfiguration. Nur falls gewünscht.

## 6. Empfohlener nächster Schritt

Mit den BC-Verantwortlichen **F1, F2 und F3** klären (die entscheiden über Architektur und Rechteprüfung), dann **D1 bis D3** entscheiden. Erst danach würde ich den BC-Teil von Workforce (Urlaub, Konto, Dienstplan) bauen. Unabhängig davon kann ich sofort das Rechte-Gerüst (Abschnitt 5: Katalog, Tests je Endpunkt, positive und negative Berechtigungstests, Audit-Log nach Freigabe) vorbereiten.
