# Krankmeldung mit Arbeitsunfähigkeitsbescheinigung (AU)

Dienst `Workforce`, Entscheidungen 20 und 42 (`CLAUDE.md`). Überarbeitet 2026-11-10.

Krankmeldungen gibt es in BC nicht in dieser Form, Workforce bildet sie ab. Die Person meldet sich krank, lädt die AU hoch, sobald sie vorliegt, und trägt den Zeitraum laut AU ein; HR prüft, dass wirklich eine AU hochgeladen wurde. Der Zeitraum pflegt die Abwesenheit.

## Ablauf

1. **Krankmeldung** (`POST /api/me/absences/sick`, für andere `…/employees/{id}/absences/sick`): erster und letzter Tag (Ende ist Pflicht) und die Art `First` oder `FollowUp`. Eine Folgekrankmeldung ist nur **direkt im Anschluss** an eine vorhandene Krankmeldung der Person möglich und braucht eine eigene AU. Die AU ist freiwillig dabei: Datei oder das Kennzeichen **eAU** (liegt bei HR in DATEV; nur ein Vermerk, der Stand bleibt `Missing`, keine Erinnerung).
2. **AU hochladen** (jederzeit, auch mehrfach): selbst `POST /api/me/absences/{id}/certificates`, für andere `POST /api/employees/{employeeId}/absences/{id}/certificates`. Formular mit `file` (PDF, JPEG, PNG, bis 10 MB), `certifiedFrom`, `certifiedTo`. Status `Submitted`. Ein Papier-AU entfernt das eAU-Kennzeichen wieder.
3. **HR prüft:** öffnet die Datei, bestätigt (`…/verify`, `Verified`) oder lehnt mit festem Grund ab (`…/reject`, `Rejected`; `Unreadable`, `WrongPerson`, `WrongPeriod`, `NotACertificate`, kein Freitext). Die Datei bleibt erhalten, die Person lädt neu hoch.
4. **Erinnerungen:** Fehlt die AU (`Missing`/`Rejected`, nicht bei eAU), erinnert das System frühestens am ersten Tag zurück, dann alle 2 Tage, höchstens 5. HR ändert den Rhythmus (`GET/PUT /api/certificates/reminders`, `certificate:manage-retention`). Die Erinnerung enthält keine Daten (InApp, Push generisch).
5. **Löschen:** automatisch nach Ende der Krankmeldung plus Löschfrist (HR stellt sie ein, `GET/PUT /api/certificates/retention`, Standard 365 Tage, 0 bis 3650, gilt auch für vorhandene Dateien); von Hand durch HR (`certificate:delete`) oder die hochladende Person vor der Prüfung; bei `EmployeeDataErasureRequested`. Eine geprüfte AU verliert nur die Datei, Zeitraum und Status bleiben, damit niemand erneut erinnert wird.

## Zeitraum pflegt die Abwesenheit

| Fall | Wirkung |
| --- | --- |
| AU geht über das gemeldete Ende hinaus (Folgebescheinigung) | Krankmeldung wird bis dahin verlängert |
| AU endet früher als gemeldet | Krankmeldung bleibt, „AU gültig bis“ merkt sich den Stand |
| AU beginnt bis zu 3 Tage vor der Meldung | Beginn wird vorgezogen; früher nicht erlaubt |
| AU passt nicht (anderer Zeitraum, Ende vor Beginn, mehr als ein Jahr) | abgelehnt (422) |
| Verlängerung überschneidet eine andere Krankmeldung | abgelehnt (409), nichts gespeichert |
| Verlängerung überschneidet genehmigten Urlaub | Krankheit gewinnt, die Urlaubstage werden storniert und gutgeschrieben |

Jede Änderung des Zeitraums erhöht die Version und geht per `SyncAbsenceToBc` nach BC (Typ und Zeitraum, **nie** Datei oder AU-Status). Statusänderungen der AU gehen nicht nach BC.

## Wer was sieht (Rechte einzeln in Access, standardmäßig niemand)

| Recht | Scopebar | Wirkung |
| --- | --- | --- |
| `certificate:upload-own` | nein | eigene AU hochladen (gehört in „Public User“) |
| `certificate:upload-for` | ja | AU für Personen im Bereich hochladen (z. B. Filialleitung) |
| `certificate:read-status` | ja | sieht **dass** eine AU da ist, Status und Dokumenteinträge (nie die Datei); HR-Übersicht `GET /api/sick-notes` (Filter u. a. `certificateStatus`, `electronic`, Zeitraum, Filiale, Person) |
| `certificate:review` | ja | Dateien öffnen, bestätigen, ablehnen (HR); die eigene AU prüft man nicht |
| `certificate:delete` | ja | AU von Hand löschen (HR) |
| `certificate:manage-retention` | nein | Löschfrist und Erinnerungsrhythmus einstellen (HR) |
| `certificate:read-access-log` | nein | Zugriffsprotokoll lesen (`GET /api/certificates/access-log`) |

Die **Datei** öffnen nur die hochladende Person und Inhaber von `certificate:review`. Wer den Status sehen, aber die Datei nicht öffnen darf, bekommt **403**; wer den Status nicht sehen darf (Kolleginnen, andere Filialen), bekommt **404** und erfährt nicht einmal, dass eine AU existiert. Art und eAU-Kennzeichen sehen dieselben wie den Status.

## Datenschutz

- **Eigene Dokumentklasse** (Abweichung von „Klasse 3 nur über die HR API“, Abstimmung mit dem Datenschutzbeauftragten offen): Die Datei bleibt nur in Workforce, nie auf dem Bus, nie nach BC, nie in Logs oder Benachrichtigungen. Keine Inhaltsauswertung (kein OCR).
- **Getrennter Speicher:** nicht in der Datenbank und nicht im Anwendungsverzeichnis, sondern in `Workforce:Certificates:StoragePath` (absoluter Pfad, nur für den Dienst zugänglich; der Dienst startet nicht, wenn er fehlt oder im Anwendungsverzeichnis liegt, außer in Development/Testing). Dateiname ist eine zufällige UUID ohne Endung, Person, Datum und Typ stehen nur in der Datenbank, der Originalname wird nie gespeichert. Schreiben über eine temporäre Datei mit Umbenennen; ein stündlicher Abgleich löscht Dateien ohne Dokument (älter als `OrphanGraceMinutes`). Hinter dem Port `ICertificateFileStorage` später auf Azure Blob Storage umstellbar.
- **Verschlüsselt** mit AES-256-GCM, an die Dokument-ID gebunden; Schlüssel `Workforce:Certificates:EncryptionKey` (base64, 32 Bytes, nur User Secrets/Key Vault). In Produktion ohne Schlüssel startet der Dienst nicht; **geht der Schlüssel verloren, sind die Dateien unlesbar.** In Development/Testing gilt ein flüchtiger Schlüssel.
- **Auslieferung:** Typ aus dem Inhalt, neutraler Dateiname, `Cache-Control: no-store`, `nosniff`, `CSP: sandbox`; Formulare werden im Speicher gepuffert (kein Temp-File).
- **Zugriffsprotokoll** für jeden Upload, Download, jede Prüfung und Löschung (Zeit, Person, Abwesenheit, Dokument, nie Inhalt), 365 Tage.
- **Virenprüfung:** Jeder Upload geht nach Typ- und Größenprüfung und **vor** der Verschlüsselung an ClamAV (`clamd`, `INSTREAM`); jeder Fehler gilt als „nicht sauber“ (fail closed). Konfiguration `Workforce:Certificates:Scanner:Host` (Pflicht außerhalb von Development/Testing), `Port` (3310), `TimeoutSeconds` (30). Lokal startet der AppHost `clamav/clamav`; ohne Host nutzen Development/Testing einen Platzhalter. `/health/ready` meldet `Degraded`, wenn `clamd` nicht antwortet (Uploads werden dann abgelehnt).

## Konfiguration (`Workforce:Certificates`, Standardwerte)

`MaxFileBytes` 10 MB · `MaxDocumentsPerAbsence` 5 · `BackdatingDays` 3 · `FirstReminderAfterReturnDays` 0 · `ReminderEveryDays` 2 · `MaxReminders` 5 · `MaxReminderSettingDays` 90 · `RetentionAfterEndDays` 365 · `MaxRetentionAfterEndDays` 3650 · `OrphanGraceMinutes` 60 · `AccessLogRetentionDays` 365 · `StoragePath` · `EncryptionKey`.

## Offen

- **Datenschutzbeauftragter und HR:** Standard von Löschfrist und Erinnerungsrhythmus, Rechtsgrundlage, Passung der Dokumentklasse.
- **Betrieb (`TODO(Betrieb)`):** `clamd` nur im selben Pod oder privaten Netz (die Verbindung ist unverschlüsselt), `StreamMaxLength` und `MaxScanSize` mindestens so groß wie `MaxFileBytes`, rund 1 bis 2 GB RAM, keine Cloud-Scanner; Speicherverzeichnis mit eigenem Benutzer oder Volume und getrennter Sicherung (Datenbank und Dateien zusammen wiederherstellen); bei mehreren Instanzen laufen Erinnerungen und Löschung je Instanz (harmlos, eine Sperre wäre sinnvoll); Schlüsselrotation (Format trägt eine Versionsnummer, Ablauf nicht gebaut).
- **eAU** bleibt bei DATEV und ist nur als Kennzeichen abgebildet.
