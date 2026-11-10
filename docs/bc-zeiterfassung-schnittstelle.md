# BC-Schnittstelle „Zeiterfassung“

Ursprünglich 2026-10-26, gekürzt 2026-11-10. Nichts davon ist seither entschieden oder gebaut (außer der PIN-Prüfung, Entscheidung 19).

**Verlässlichkeit:** Alles ist aus dem **Aufruferverhalten** des HR-Tools (`wghn-hr-tool`, `src/services/*.ts`, `api/soap-proxy`) und des Archivs (`Archiv/BC-Anbindung`) abgeleitet. Die AL-Seite von BC wurde nicht gesehen, es lief nichts gegen BC. Parameter und Antwortfelder sind belastbar (so ruft das produktive HR-Tool auf), Regeln in BC, Fehlerfälle und Berechtigungsprüfung sind **offen**.

## Aufrufkonvention

- SOAP 1.1 an `…/WS/{Firma}/Codeunit/Zeiterfassung`, `SOAPAction` `urn:microsoft-dynamics-schemas/codeunit/Zeiterfassung:<Funktion>`. Antwort: `<return_value>` mit **JSON als Text**; ein SOAP-Fault ist ein Fehler.
- Daten als `YYYY-MM-DD`, ein leeres Datum ist `0001-01-01`, Wahrheitswerte teils als Text. Personen über die **Personalnummer** (`employeeNo`, `supervisorNo`, `approverNo`), nicht über eine GUID.
- Das HR-Tool hängt an **jeden** Aufruf die PIN der angemeldeten Person (`password`) an. Ob BC sie bei jeder Funktion prüft oder nur bei `GetStatus`, ist unbekannt (F1).

## Funktionen

| Funktion | Zweck | Unser Gegenstück |
| --- | --- | --- |
| `GetStatus` (`employeeNo`, `queryDate`, `password`) | Anmeldung/PIN-Prüfung, Tagesstatus | **umgesetzt** (PIN-Login) |
| `GetVacationBalance` | Urlaubskonto: Anspruch, Übertrag, genommen, beantragt, Rest | Konto in Workforce (heute von HR gepflegt) |
| `GetVacationAvailability`, `GetVacationThresholds` | Besetzungsampel und Mindestbesetzung je Filiale/Region | nicht vorhanden |
| `RequestVacation`, `GetMyVacationRequests` | Antrag stellen, eigene Anträge (Status `requested` → `supervisorApproved` → `approved` / `rejected`) | Urlaubsantrag in Workforce |
| `CheckSupervisorStatus`, `GetPendingApprovalsForSupervisor` | Ist die Person Vorgesetzte, offene Anträge | Zuständigkeit `absence.approval` in Access (manuell) |
| `ApproveVacationRequestAsSupervisor`, `RejectVacationRequest` (Grund max. 250) | Genehmigung, Ablehnung; Anträge über (Personalnummer, Startdatum) adressiert | `Shared.Approvals` |
| `GetTeamVacationCalendar` | Urlaubskalender des Teams | Team-Sicht in Workforce |
| `GetScheduleRange` | Dienstplan einer Person (Standort, Zeiten je Tag) | geplante Dienstplan-Einsicht |
| `GetTimeEntries`, `RegisterTime` (`In`, `Out`, `PauseStart`, `PauseEnd`, `TravelStart`, `TravelEnd`), `AddComment` (Freitext, max. 250) | Zeitbuchungen und Stempeln | Zeiterfassung (später) |

Im HR-Tool gibt es **keine Krankmeldung**.

## Folgen für unsere Annahmen

| Annahme | Befund |
| --- | --- |
| Abwesenheiten über eine neue API-Seite mit Upsert über externe ID | Für Urlaub gibt es Codeunit-Funktionen, adressiert über (Personalnummer, Startdatum). Upsert über unsere `AbsenceId` passt dafür nicht (D2). |
| Eine Genehmigungsstufe | BC kennt **zwei** (`supervisorApproved`, dann `approved`) (D1). |
| Urlaubsanspruch manuell pflegen | `GetVacationBalance` liefert ihn; Workforce führt ihn aber inzwischen selbst (Entscheidung 24). |
| Krankmeldung nach BC (`KRANK`) | Kein Gegenstück im HR-Tool (F4). |
| Stammdaten über REST-API-Seiten | Das HR-Tool nutzt dafür keine Funktion; Filialen kommen im Archiv über die Codeunit `GetStores` (F5 für Mitarbeitende). |

## Fragen an die BC-Verantwortlichen

- **F1 – PIN bei jedem Aufruf?** Falls ja, bräuchte BC Integration pro Person eine PIN-freie Funktion oder ein technisches Konto; wir speichern die PIN bewusst nicht im Klartext. Die wichtigste Frage für alles Weitere.
- **F2 – Berechtigungsprüfung in BC:** Darf man mit fremder `employeeNo` fremde Daten abfragen? Mit einem Dienstkonto wäre die ACL in Access die einzige Schranke.
- **F3 – Zwei Genehmigungsstufen:** Wer gibt die Endfreigabe, wer die Vorgesetztenstufe? Was bedeutet `flexible`?
- **F4 – Krankmeldung:** Funktion oder Tabelle für Krank, mit oder ohne AU-Kennzeichen, mit Ende?
- **F5 – Mitarbeiterstammdaten:** Quelle der Karte (Hauptfiliale, Funktion, Eintritt/Austritt, Sollstunden), Änderungszeitstempel?
- **F6 – Stornierung:** Es gibt keine Funktion zum Zurückziehen eines Antrags.
- **F7 – Schwellenwerte:** harte Sperre oder nur Ampel?

## Entscheidungen für dich

- **D1 – Wer führt die Genehmigung?** (a) Workforce genehmigt (eine Stufe, wie gebaut) und schreibt das Ergebnis nach BC, oder (b) BC bleibt für den Workflow führend. Das Projektziel (Workforce führend, BC gespiegelt) spricht für (a); dann muss BC die Genehmigung per Funktion übernehmen können.
- **D2 – Schlüssel der Anträge:** zusätzliche ID in BC, oder (Personalnummer, Startdatum) als BC-Schlüssel speichern und gleiche Startdaten je Person verbieten (die Überlappungsregel gibt es ohnehin).
- **D3 – Technisches Konto:** Falls F1 „ja“, von BC eine PIN-freie Funktion bzw. ein Dienstkonto verlangen. Die PIN gehört nicht in ein Token oder eine Sitzung.

## Rechte (Vorschlag, noch nicht gebaut)

Alles, was Klasse 2 ist, braucht ein eigenes, einzeln vergebbares Recht, standardmäßig an niemanden; „Public User“ erhält nur `*-own`-Rechte. Für Urlaub und Dienstplan wären das zusätzlich zu `absence:*`: `vacation:read-own-balance`/`read-balance`, `request-own`/`request-for`, `read-availability` (aggregiert), `read-team`, `approve`, `read-thresholds`/`manage-thresholds`, `schedule:read-own`/`read`/`read-locations` und später `time:*`. Wird BC Integration mit einem Dienstkonto aufgerufen (F2/D3), muss jeder Endpunkt die lokale Prüfung „Recht + Filiale/Person“ behalten. Das **Änderungsprotokoll** und die Schutzregeln gegen Rechteausweitung gibt es inzwischen (Entscheidung 46, `AclGuard`).

## Nächster Schritt

F1, F2 und F3 mit den BC-Verantwortlichen klären, dann D1 bis D3 entscheiden; erst danach den BC-Teil von Workforce (Urlaub, Konto, Dienstplan) bauen.
