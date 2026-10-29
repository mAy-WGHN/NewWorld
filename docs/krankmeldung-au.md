# Krankmeldung mit Arbeitsunfähigkeitsbescheinigung (AU)

Stand: 2026-10-26 · Dienst: `Workforce` · Entscheidung 20 (siehe `CLAUDE.md`).

Krankmeldungen gibt es in BC nicht in dieser Form. Wir bilden sie in Workforce ab: Die Person meldet sich krank (ohne Datei), lädt die AU hoch, sobald sie vorliegt, und trägt dabei den Zeitraum von der AU ein. HR prüft, dass wirklich eine AU hochgeladen wurde. Der Zeitraum pflegt die Abwesenheit automatisch.

## Ablauf

1. **Krankmeldung** (`POST /api/me/absences/sick` bzw. `…/employees/{id}/absences/sick`): Zeitraum, **keine** Datei nötig. Status der AU: `Missing`.
2. **AU hochladen** (jederzeit danach, auch mehrfach für Folgebescheinigungen):
   - selbst: `POST /api/me/absences/{absenceId}/certificates`
   - für jemand anderen (z. B. Filialleitung): `POST /api/employees/{employeeId}/absences/{absenceId}/certificates`
   - Formular (`multipart/form-data`): `file` (PDF, JPEG oder PNG, bis 10 MB), `certifiedFrom`, `certifiedTo` (`yyyy-MM-dd`, erster und letzter Tag der Arbeitsunfähigkeit laut AU). Das ist der „Dialog“, den das Frontend zeigt; beide Daten sind Pflicht.
   - Status: `Submitted`.
3. **HR prüft**: öffnet die Datei, bestätigt (`…/verify`, Status `Verified`) oder lehnt mit festem Grund ab (`…/reject`, Status `Rejected`; Gründe: `Unreadable`, `WrongPerson`, `WrongPeriod`, `NotACertificate`, kein Freitext). Bei Ablehnung bleibt die Datei erhalten (HR und die hochladende Person können sie weiter öffnen) und wird wie jede andere durch die Aufbewahrungsfrist gelöscht; die Person und die Hochladende werden benachrichtigt und laden neu hoch.
4. **Erinnerungen:** Fehlt die AU (`Missing` oder `Rejected`), erinnert das System die Person (Standard: erste Erinnerung am ersten Tag zurück, also am Tag nach dem Ende der Krankmeldung, dann alle 2 Tage, höchstens 5; die Personalabteilung kann den Rhythmus ändern, aber nie vor den ersten Tag zurück legen; eine Krankmeldung ohne Ende wird nicht erinnert). Die Erinnerung enthält keine Daten, nur den Hinweis im Portal (InApp, Push mit generischem Text).
5. **Löschen:** Die Datei wird nach Ende der Krankmeldung + Löschfrist automatisch gelöscht. **Die Personalabteilung stellt die Frist selbst ein** (siehe unten, Standard 365 Tage). Außerdem kann die Personalabteilung jede AU von Hand löschen, und die hochladende Person kann einen noch ungeprüften Upload zurücknehmen. Die Metadaten und der Status bleiben, wenn eine geprüfte AU gelöscht wird.

## Zeitraum pflegt die Abwesenheit automatisch

Der eingetragene Zeitraum wird mit der Krankmeldung abgeglichen:

| Fall | Wirkung |
| --- | --- |
| Krankmeldung offen („bis auf Weiteres“) | bekommt als Ende den letzten Tag der AU |
| AU geht über das gemeldete Ende hinaus (Folgebescheinigung) | Krankmeldung wird bis dahin verlängert |
| AU endet früher als gemeldet | Krankmeldung bleibt, „AU gültig bis“ merkt sich den Stand |
| AU beginnt bis zu 3 Tage vor der Meldung (rückdatiert) | Beginn wird vorgezogen; früher ist nicht erlaubt |
| AU passt nicht zur Krankmeldung (anderer Zeitraum, Ende vor Beginn, mehr als ein Jahr) | abgelehnt (422) |
| Verlängerung überschneidet eine andere Krankmeldung | abgelehnt (409), nichts wird gespeichert |
| Verlängerung überschneidet genehmigten Urlaub | Krankheit gewinnt: betroffene Urlaubstage werden storniert und gutgeschrieben (Regel der Plan-Phase 6) |

Jede Änderung des Zeitraums erhöht die Version der Abwesenheit und geht wie bisher per `SyncAbsenceToBc` nach BC (Typ und Zeitraum, **nie** die Datei oder der AU-Status). Die Statusänderungen der AU selbst gehen nicht nach BC und erhöhen die Version nicht.

## Wer was sieht (fein granulare Rechte in Access)

Alle Rechte werden einzeln in Access vergeben (Rollen, Scopes, Laufzeit). Standard: **niemand**, bis eine Rolle sie enthält.

| Recht | Scopebar | Wirkung |
| --- | --- | --- |
| `certificate:upload-own` | nein | eigene AU hochladen (gehört in „Public User“, falls gewünscht) |
| `certificate:upload-for` | ja | AU für Personen im Bereich hochladen (z. B. Filialleitung) |
| `certificate:read-status` | ja | sieht, ob eine AU da ist, den Status (`Missing`/`Submitted`/`Verified`/`Rejected`) und die Einträge der Dokumente (nie die Datei); HR-Übersicht `GET /api/sick-notes` mit Filtern; beim Öffnen 403 statt 404 |
| `certificate:review` | ja | Dateien von Personen im Bereich öffnen, bestätigen, ablehnen (HR) |
| `certificate:delete` | ja | AU von Personen im Bereich von Hand löschen, in jedem Zustand (Personalabteilung) |
| `certificate:manage-retention` | nein | Löschfrist (Tage nach Ende der Krankmeldung) lesen und einstellen (Personalabteilung) |
| `certificate:read-access-log` | nein | Zugriffsprotokoll lesen (Datenschutz/Revision) |

**Die Datei** öffnen nur die **hochladende Person** und Inhaber von `certificate:review`. Niemand sonst bekommt sie angezeigt.

**Löschen von Hand** (`DELETE /api/absences/{id}/certificates/{docId}`): die hochladende Person, solange der Upload noch ungeprüft ist; Inhaber von `certificate:delete` für die Person in jedem Zustand (nicht für ein Dokument über sich selbst, das jemand anderes hochgeladen hat). Eine **geprüfte** AU verliert nur die Datei: Zeitraum, Zustand „geprüft“ und Status bleiben, damit niemand erneut erinnert wird (wie bei der automatischen Löschung). Jedes andere Dokument wird vollständig entfernt, der Status ergibt sich neu (ohne weitere AU wieder „fehlt“). Wer den Status sehen darf, aber nicht löschen, bekommt 403, alle anderen 404. Die Datei wird nach dem Speichern der Änderung aus dem Speicher entfernt; was bei einem Absturz dazwischen übrig bleibt, räumt der Abgleich (siehe Speicher) auf.

**Dass eine AU da ist,** sehen die Person selbst und alle mit `certificate:read-status` für sie (z. B. Filialleitung und höhere Stellen, per Rolle in Access konfigurierbar): Status und die Einträge der Dokumente (Zustand, Zeitraum, „Datei vorhanden“, „kann ich öffnen“), aber nicht, wer hochgeladen hat. Versuchen sie, die Datei zu öffnen, zu bestätigen/abzulehnen oder zu löschen, bekommen sie **403 (Zugriff verweigert)**. Wer den Status nicht sehen darf (z. B. Kolleginnen und Kollegen, andere Filialen), erfährt nicht einmal, dass eine AU existiert: dort **404**. Wer bei sich selbst `review` hat, prüft die eigene AU nicht (403).

## HR-Übersicht

`GET /api/sick-notes?certificateStatus=Missing,Rejected&from=2026-10-01&to=2026-10-31&storeId=…&employeeId=…&page=1&pageSize=50`

Liefert je Krankmeldung: Person, Filiale, Zeitraum, AU-Status, „AU gültig bis“, Anzahl und Zeitpunkt der letzten Erinnerung. Nur Personen im Bereich des Rechts `read-status` erscheinen.

## Erinnerungsrhythmus (von der Personalabteilung einstellbar)

`GET /api/certificates/reminders` und `PUT /api/certificates/reminders` mit `{"firstReminderAfterReturnDays": 0, "reminderEveryDays": 2, "maxReminders": 5}` (gleiches Recht wie die Löschfrist, `certificate:manage-retention`). Die erste Erinnerung kommt frühestens am **ersten Tag zurück** (Tag nach dem Ende der Krankmeldung) plus die eingestellten Tage; negative Werte und ein Intervall unter 1 Tag sind 422, 0 Erinnerungen schaltet sie ab. Eine Krankmeldung ohne Ende („bis auf Weiteres“) wird nicht erinnert. Die Änderung steht im Zugriffsprotokoll (`RemindersChanged`).

## Löschfrist (von der Personalabteilung einstellbar)

`GET /api/certificates/retention` und `PUT /api/certificates/retention` mit `{"retentionAfterEndDays": 30}` (Recht `certificate:manage-retention`). Bis die Personalabteilung einen Wert setzt, gilt der Standard aus der Konfiguration (365 Tage). Erlaubt sind 0 (am letzten Tag der Krankmeldung) bis 3650 Tage, sonst 422. Der Wert gilt **auch für bereits gespeicherte Dateien**: bei jeder Prüfung (stündlich) wird „Ende der Krankmeldung + Frist“ mit dem heutigen Wert verglichen. Wer den Wert wann geändert hat, steht im Zugriffsprotokoll (`RetentionChanged`) und in der Antwort. Eine Krankmeldung ohne Ende („bis auf Weiteres“) wird nie automatisch gelöscht.

## Datenschutz

- **Eigene Dokumentklasse (Entscheidung 20):** Die Datei ist ein Gesundheitsdokument. Sie bleibt ausschließlich in Workforce, geht nie auf den Bus, nie nach BC, nie in Logs oder Benachrichtigungen.
- **Getrennter Speicher:** Die Dateien liegen nicht in der Datenbank und nicht im Anwendungsverzeichnis, sondern in einem eigenen Verzeichnis (`Workforce:Certificates:StoragePath`, absoluter Pfad), auf das nur der Dienst zugreifen darf (eigener Betriebssystem-Benutzer bzw. eigenes Volume). Der Dienst startet nicht, wenn der Pfad fehlt (außer Development/Testing) oder **innerhalb des Anwendungsverzeichnisses** liegt: Was der Webserver ausliefert oder was ein Deployment überschreibt, enthält nie die Dateien. Ein Zugriff auf die Datenbank (z. B. per SQL-Injection) liefert nur Metadaten, kein einziges Byte einer AU; ein Zugriff auf das Verzeichnis liefert nur verschlüsselte Bytes ohne Zuordnung zu Person und Datum. Dateinamen sind `<UUID>` (zufällig, ohne Endung, ohne Bezug zur Dokument-ID), der ursprüngliche Dateiname wird nie gespeichert; Person, Datum und Typ stehen nur in der Datenbank. Das Verzeichnis wird nie als Pfad aus Benutzereingaben gebildet (nur aus einer GUID). Schreiben geschieht über eine temporäre Datei und Umbenennen (eine Datei erscheint ganz oder gar nicht, ein Name wird nie überschrieben), unter Linux mit Rechten 0600. Ein stündlicher Abgleich löscht Dateien ohne Dokument (z. B. nach einem fehlgeschlagenen Speichern), die älter als `OrphanGraceMinutes` (60) sind. Hinter dem Port `ICertificateFileStorage` kann der Speicher später auf Azure Blob Storage mit eigener Identität umgestellt werden (Hosting offen).
- **Verschlüsselt:** Inhalt AES-256-GCM, an die Dokument-ID gebunden. Der Schlüssel kommt aus der Konfiguration (`Workforce:Certificates:EncryptionKey`, base64, 32 Bytes; User Secrets/Key Vault). In Produktion fehlt er → der Dienst startet nicht; in Development/Testing gilt ein flüchtiger Schlüssel (Dateien überleben keinen Neustart). **Geht der Schlüssel verloren, sind die Dateien unlesbar.**
- **Kein Dateiname** wird gespeichert, der Typ kommt aus dem Inhalt (nicht aus Name oder Header). Download immer als neutrale Datei (`certificate.pdf`), `Cache-Control: no-store`, `nosniff`, `CSP: sandbox`.
- **Kein Temp-File auf der Platte:** Formulare werden im Speicher gepuffert.
- **Zugriffsprotokoll:** Jeder Upload, Download, jede Bestätigung, Ablehnung, Rücknahme und Löschung wird mit Zeit, Person, Abwesenheit und Dokument protokolliert, nie mit Inhalt. Aufbewahrung 365 Tage. Lesen nur mit `certificate:read-access-log`: `GET /api/certificates/access-log`.
- **Löschung:** nach Ende der Krankmeldung + Frist (von der Personalabteilung eingestellt, Standard 365 Tage, gilt auch für abgelehnte Dateien), von Hand durch die Personalabteilung (`certificate:delete`) oder die hochladende Person vor der Prüfung, und bei `EmployeeDataErasureRequested` (Dateien, Metadaten und Protokoll der Person; wo die Person nur hochgeladen oder geprüft hat, wird ihre ID unkenntlich gemacht).
- **Keine Inhalte lesen:** Die Datei wird nicht ausgelesen (kein OCR). Der Zeitraum wird von der Person eingetragen.

## Konfiguration

Abschnitt `Workforce:Certificates` (Standardwerte in Klammern, alles pro Umgebung änderbar):

| Einstellung | Bedeutung |
| --- | --- |
| `MaxFileBytes` (10 MB) | größte Datei |
| `MaxDocumentsPerAbsence` (5) | Dokumente je Krankmeldung (abgelehnte zählen nicht) |
| `BackdatingDays` (3) | wie weit die AU vor der Meldung beginnen darf |
| `FirstReminderAfterReturnDays` (0), `ReminderEveryDays` (2), `MaxReminders` (5), `MaxReminderSettingDays` (90) | Standard des Erinnerungsrhythmus (Tage nach dem ersten Tag zurück, Intervall, Anzahl) und Obergrenze für die Werte der Personalabteilung |
| `RetentionAfterEndDays` (365) | Standard der Löschfrist, bis die Personalabteilung einen Wert setzt |
| `MaxRetentionAfterEndDays` (3650) | größte Frist, die die Personalabteilung einstellen darf |
| `OrphanGraceMinutes` (60) | Dateien ohne Dokument werden erst danach gelöscht (laufende Uploads) |
| `StoragePath` | Verzeichnis der Dateien (siehe Datenschutz); in Development/Testing ein temporäres Verzeichnis, das mit dem Neustart verloren geht |
| `AccessLogRetentionDays` (365) | Löschfrist des Zugriffsprotokolls |
| `EncryptionKey` | siehe oben (nur User Secrets/Key Vault) |

## Offen

- **Standard der Löschfrist und Erinnerungsrhythmus:** Standardwerte; die Personalabteilung kann die Frist selbst einstellen, der Standard gilt nur, bis sie es tut. Mit dem Datenschutzbeauftragten und HR zu bestätigen. Dort auch klären, ob die Rechtsgrundlage ausreicht und ob die Dokumentklasse so passt (Abweichung von der Plan-Regel „Klasse 3 nur über die HR API“).
- **Virenprüfung (ClamAV):** Jeder Upload wird nach Typ- und Größenprüfung und **vor** der Verschlüsselung an ClamAV (`clamd`, Befehl `INSTREAM`) geschickt (`ClamAvScanner`); der Speicher sieht nur verschlüsselte Bytes und könnte nichts finden. Jeder Fehler (nicht erreichbar, Zeitüberschreitung, Größenlimit von `clamd`, unbekannte Antwort) gilt als „nicht sauber“ und lehnt die Datei ab (fail closed). Konfiguration: `Workforce:Certificates:Scanner:Host` (Pflicht außerhalb von Development/Testing, sonst startet der Dienst nicht), `Port` (3310), `TimeoutSeconds` (30). Ohne Host nutzen Development/Testing einen Platzhalter, der alles annimmt. Die Prüfung `malware-scanner` unter `/health/ready` meldet *Degraded*, wenn `clamd` nicht antwortet (Dienst bleibt oben, Uploads werden abgelehnt). Im Log stehen nur die Signaturbezeichnung eines Fundes und Fehlerhinweise, nie Inhalt oder Dateiname. `TODO(Betrieb)`, siehe unten.
  - **Betrieb von `clamd`:** nur im selben Pod/auf demselben Host (Loopback) oder in einem privaten Netz, das nur Workforce erreicht; die Verbindung ist unverschlüsselt und trägt die Datei im Klartext. `StreamMaxLength` und `MaxScanSize` mindestens so groß wie `MaxFileBytes` (10 MB). `TemporaryDirectory` auf tmpfs oder ein verschlüsseltes Volume. `freshclam` braucht ausgehend nur den Signatur-Download. Rund 1–2 GB RAM. Keine Cloud-Scanner (die AU darf die eigene Infrastruktur nicht verlassen). ClamAV erkennt nur bekannte Schadsoftware.
- **Schlüsselverwaltung und -rotation:** Format enthält eine Versionsnummer; ein Rotationsablauf ist noch nicht gebaut.
- **Speicher im Betrieb:** Verzeichnis mit eigenem Betriebssystem-Benutzer bzw. Volume anlegen, Rechte nur für den Dienst, Sicherung getrennt von der Datenbank (Datenbank und Dateien müssen zusammen wiederhergestellt werden; Dateien ohne Dokument räumt der Abgleich auf, Dokumente ohne Datei zeigen „keine Datei“). `TODO(Betrieb)`.
- **Mehrere Instanzen:** Erinnerungen und Löschung laufen je Instanz; Wiederholungen sind harmlos (feste Benachrichtigungs-ID), eine Sperre ist trotzdem sinnvoll. `TODO(Betrieb)`.
- **Frontend:** Dialog (Datei + „von/bis“), HR-Ansicht mit Filtern und Prüfung. Die API ist fertig; die Oberfläche ist nicht Teil dieses Projekts.
- **elektronische AU (eAU):** Wird weiterhin über DATEV abgeholt bzw. eingesehen und ist hier bewusst nicht abgebildet.
