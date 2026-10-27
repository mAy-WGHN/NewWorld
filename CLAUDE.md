# CLAUDE.md – NewWorld (Monorepo)

Quelle der Regeln: `Umsetzungsplan Mitarbeiterportal – Backend-Architektur.md` (im Root). Bei Widerspruch gilt der Plan, außer die „Entscheidungen nach Phase 0“ weiter unten legen etwas anderes fest. Lies ihn vollständig, bevor du neue Dateien anlegst.

**Begriff:** „Portal API“ im Plan = der neue Dienst `src/Portal`. Der Bestandsdienst liegt unter `Archiv/Portal` (früher `IRS-API`).

## Kontext

Backend eines Mitarbeiterportals, eines HR-Tools und der Integrationsdienste für einen Filialbetrieb. Ziel des Plans: vertikaler Durchstich — Abwesenheit (Krankmeldung/Urlaub) erfassen → nach Business Central (BC) synchronisieren → Filialleitung benachrichtigen. Muster muss für weitere Fachbereiche wiederverwendbar sein.

- BC ist alleiniger Master für Stammdaten (Filialen, Mitarbeiterkarte).
- Dienstplanung, Nichtverfügbarkeiten, Zeiterfassung: neues System operativ führend, aber vollständig nach BC gespiegelt (Tool bleibt ersetzbar).

### Ordner

| Ordner | Inhalt |
| --- | --- |
| `src/`, `tests/` | Neue Dienste und Shared-Bibliotheken nach Plan (`NewWorld.slnx`). Hier wird entwickelt. |
| `Archiv/` | Bestandsdienste, unverändert lauffähig (Entscheidung 10). Fachliche Referenz, keine Weiterentwicklung. |
| `docker-compose.yaml`, `Makefile` | Startet die Archiv-Dienste gemeinsam (`make up`). |
| `docs/` | Phasenberichte. |

| Archiv-Ordner | Entsprechung im Zielbild (`src/`) |
| --- | --- |
| `Archiv/BC-Anbindung` | `BcIntegration` |
| `Archiv/Rollen-Rechte` | `Access` |
| `Archiv/Benachrichtigungen` | `Notifications` |
| `Archiv/Portal` (früher `IRS-API`) | `Portal` (im Plan nur minimales Gerüst) |
| `Archiv/hr-tool-API` | `Workforce` (intern getrennt in `Workforce/` und `Hr/`); die HR API des Plans (Recruiting, DATEV) ist out of scope |

Jeder Archiv-Ordner hat ggf. eine eigene `CLAUDE.md`; sie beschreibt den Bestand und gilt nur dort.

## Entscheidungen nach Phase 0 (Stand 2026-10-21)

Diese Entscheidungen des Projektverantwortlichen gelten **vor** dem Plan, wo sie ihm widersprechen. Der Plantext selbst bleibt unverändert (nur ein Hinweis am Anfang verweist hierher); Abweichungen stehen hier und im Phase-0-Bericht.

1. **`hr-tool-API` ist die Basis der Workforce API**, nicht der HR API. Der Ordner bleibt ein Dienst, ist aber intern in `Workforce/` und `Hr/` getrennt (Namespaces `HrTool_API.Workforce` / `HrTool_API.Hr`, siehe `hr-tool-API/CLAUDE.md`). Die HR API im Sinne des Plans (Recruiting, DATEV) bleibt out of scope. Eine spätere Aufteilung in zwei Dienste ist möglich, aber nicht beschlossen.
2. **`IRS-API` ist in `Portal` umbenannt** (Ordner, Projekt, Solution, Namespace `Portal`). Unverändert bleiben bewusst Deployment- und Datenbank-Namen (z. B. `APP_NAME` im Makefile, `IrsDbContext`, Datenbank), da sie produktiv sind. Die Fachlogik (Meldungen, FAQ, Statistik) bleibt unangetastet.
3. **Rollen-Rechte behält das extrem feingranulare ACL** (Rollen mit beliebigen Privilegien, Katalog, Custom Privileges, Scopes). Es wird **nicht** auf 4 grobe Entra-App-Roles reduziert. Das weicht vom Plan ab (Abschnitt „Autorisierung“, Phase 4); wie das ACL mit Entra/`Shared.Authorization` zusammenspielt, ist noch zu entwerfen.
4. **Der BC-PIN-Login bleibt.** Zur Entlastung der BC-Anbindung soll das Ergebnis zusätzlich in den Datenbanken zwischengespeichert werden. Das Sicherheits-/Cache-Design (was genau, wie lange, wo) ist noch nicht spezifiziert und wird vor der Umsetzung abgestimmt; die PIN darf weiterhin nie im Klartext gespeichert oder geloggt werden.
5. **`GET /api/me/privileges` bleibt** (Rechte und Reichweite pro Request). Das weicht von „Access nie im Request-Pfad“ ab; die Phase-4-Aufgabe „synchrone Prüf-Endpunkte als veraltet markieren“ entfällt. Ob zusätzlich `PermissionsChanged` und ein lokaler Cache eingeführt werden, ist offen.
6. **`report.created` bleibt** (Portal → Benachrichtigungen, inkl. Meldungsregeln, Empfängerermittlung und Einstellungen). Die Phase-5-Aufgabe, diese Fachlogik zu entfernen, entfällt. Offen bleibt der Konflikt mit der Datenschutz-Regel für Push/Mail (Meldungstext `description` im Push-Text, Klasse 2) — mit dem Datenschutzbeauftragten zu klären.
7. ~~Geschachtelte `.git`-Repos bleiben vorerst erhalten.~~ **Verworfen (2026-10-21):** Die Bestandsdienste liegen ohne eigene `.git`-Repos im Monorepo unter `Archiv/` (Entscheidung 10); ihre Historie lebt im Root-Repo weiter.
8. **Kein Wolverine** (keine Lizenz vorhanden). Messaging läuft über `Azure.Messaging.ServiceBus` mit eigener, schlanker Outbox/Inbox (EF Core, PostgreSQL) in `Shared.Infrastructure` (Entscheidung 11). Der Transport steckt hinter einem Interface; ein späterer Wechsel (z. B. HTTP-Zustellung statt Broker) betrifft nur dessen Implementierung. Die Hosting-Umgebung ist noch offen. Dadurch entfällt die Wolverine-Bestätigung am Stopp-Punkt von Phase 1.
9. **Build-Konfiguration im Root, `Archiv/` abgeschirmt.** `Directory.Build.props`/`Directory.Packages.props` liegen wie im Plan im Root. `Archiv/` hat eigene, leere Varianten, die die Vererbung stoppen; die Archiv-Dienste bauen mit ihren eigenen Paketversionen und ohne `TreatWarningsAsErrors` wie bisher.
10. **Bestandsdienste liegen unter `Archiv/`** (2026-10-21). Neue Entwicklung nur unter `src/`; das Archiv ist fachliche Referenz (Regeln, Datenmodell, Erkenntnisse aus BC) und bleibt per `make up` lauffähig. Fachlogik wird bei Bedarf in die neuen Dienste übernommen, nicht im Archiv weiterentwickelt. Das ersetzt den „Umbau“-Gedanken aus Entscheidung 1 und 2: Basis der Workforce- bzw. Portal-API sind `src/Workforce` und `src/Portal`, `Archiv/hr-tool-API` und `Archiv/Portal` liefern die Fachlogik als Vorlage. Entscheidungen 3 bis 6 (ACL, PIN-Login, `/api/me/privileges`, `report.created`) gelten fachlich weiter und sind in den neuen Diensten umzusetzen.
11. **Shared-Bibliotheken nach Abhängigkeiten getrennt.** `Shared.Messaging` und `Shared.Approvals` sind paketfrei (nutzbar aus `*.Contracts` bzw. `*.Core`), `Shared.Authorization` hat keine EF-/Service-Bus-Abhängigkeit. Alles Technische (Outbox/Inbox, Transport, EF-Mappings für Genehmigungen und Berechtigungs-Cache, Datenbank-Migration beim Start in Development/Testing) liegt in `Shared.Infrastructure`, das nur von `*.Infrastructure` referenziert wird. Architekturtests sichern das ab. Das ergänzt die Zielstruktur des Plans um eine fünfte Shared-Bibliothek.
12. ~~**Filialleiter-Policy** (`StoreManager` + `Executive`).~~ Durch Entscheidung 13 gegenstandslos. **Service-Bus-Emulator:** Die Lizenz (EULA) wird in AppHost und Test-Fixture akzeptiert.
13. **Berechtigungen = feingranulares ACL im Access-Dienst** (2026-10-23, konkretisiert Entscheidungen 3 und 5). Das ersetzt den Abschnitt „Autorisierung“ und die Access-Kernregeln des Plans, wo sie abweichen:
    - **Keine groben Entra-Benutzerrollen.** Entra dient nur Service-zu-Service-Aufrufen (Managed Identity, App Role pro Fähigkeit).
    - **Das ACL ist die einzige Quelle für „was“ und „wo“:** Rollen mit Rechten `ressource:aktion` (Wildcards `*`, `bereich:*`), Katalog je Anwendung plus eigene Rechte, Scope-Mengen (Filialen) als Definitionen je Filiale/Region/Bezirk, Zuweisungen pro Person mit Laufzeit und Reichweite (uneingeschränkt oder Scopes, optional Hauptfiliale). Nicht registrierte Rechte wirken nicht.
    - **BC-Daten fließen als Kopie ein** (Hauptfiliale, Funktion, Austritt). Automatisch vergeben wird nur die Basisrolle „Public User“ an jede aktive Person; alles Weitere manuell. Weitere Automatik über Hauptfiliale + Funktion (Funktion nur bei keiner oder mehreren verknüpften Filialen) ist offen: `TODO(Entscheidung)`.
    - **Vertretungen** sind befristete ACL-Zuweisungen; Beginn und Ende lösen zeitgesteuert `PermissionsChanged` aus.
    - **Vorgesetzte/Zuständigkeiten** werden je Thema manuell gepflegt (z. B. Filialleitung für den Alltag, Revisor für Prüfanweisungen), mit Laufzeit; Verteilung über das neue Event `ResponsibilitiesChanged`.
    - **Verteilung:** Access veröffentlicht je Person die wirksamen Rechte mit Reichweite (`PermissionsChanged`, additiv erweitert); jeder Dienst hält sie in seiner eigenen Tabelle `permission_snapshots` und prüft lokal „Recht + Filiale/Person“ über `Shared.Authorization`. `GET /api/me/privileges` liefert dasselbe für Frontends.
    - **Login (Schritt 4b, umgesetzt):** Personalnummer + BC-PIN. Access fragt BC ausschließlich über BC Integration (`POST /internal/employees/verify-pin`, App Role `BcIntegration.Pin.Verify`); BC vergleicht die PIN. Access stellt ein eigenes RS256-JWT aus (nur `sub`/`employee_id`, keine Rechte; Rechte kommen aus der lokalen ACL-Kopie) plus ein rotierendes Refresh-Token (nur als SHA-256-Hash gespeichert, Wiederverwendung beendet alle Sitzungen). Andere Dienste prüfen Tokens lokal über Discovery/JWKS von Access (`Authentication:Authority` = Adresse von Access). Gespeichert werden nach jedem BC-Erfolg ein PBKDF2-SHA512-Hash und (nur mit konfiguriertem Schlüssel) die verschlüsselte PIN; bei BC-Ausfall gilt die zuletzt geprüfte PIN für `Access:Login:OfflineValidity` (Standard 30 Tage, 0 = aus). Fehlversuche werden je Konto (5) und je Client (20) in 15 Minuten gedrosselt. Die PIN steht nie im Log. Signierschlüssel und PIN-Schlüssel kommen aus User Secrets/Key Vault; Development/Testing erzeugen einen flüchtigen Signierschlüssel.
14. **Notification Service (Phase 5, neuer Dienst `src/Notifications`)** (2026-10-23): Verarbeitet nur `SendNotification` (Queue `send-notification`, nur von Notifications lesbar) und `EmployeeDataErasureRequested`; die Fachlogik des Archivs (`report.created`, Empfängerermittlung über Rollen-Rechte) bleibt gemäß Entscheidung 6 im Archiv und wird nicht übernommen. Vorlagen sind JSON-Dateien (`Templates/<key>.json`, Schlüssel `<dienst>.<anlass>.<empfängerrolle>`); Push- und Mail-Texte sind generisch und enthalten keine Platzhalter (beim Start geprüft). Schutzregel: Daten, die die Vorlage als sensibel (Klasse 2) markiert, werden nur akzeptiert, wenn `InApp` der einzige Kanal ist; nicht deklarierte Datenschlüssel werden abgelehnt (Klasse unbekannt, strengere gilt); abgelehnte Commands gehen mit Code in die Dead-Letter-Queue. Push/Mail erhalten nur `GenericMessage` (Titel, Text, DeepLink), nie Daten. DeepLinks sind relative Portalpfade. Präferenzen je Person, Vorlage (Typ) und Kanal; Pflichtvorlagen sind nicht abschaltbar. InApp-Aufbewahrung 90 Tage (`Notifications:InAppRetention`). Die Rechte `notification:read-own`, `notification:update-own`, `preference:read-own`, `preference:update-own` meldet der Dienst bei Access an (`Access.Catalog.Register`); sie gehören per Verwaltung in Access in die Rolle „Public User“.
15. **PIN-Hash mit PBKDF2 statt Argon2** (2026-10-23): Das Archiv nutzt `Konscious.Security.Cryptography.Argon2`; das Paket gehört nicht zum Stack des Plans. PBKDF2 (.NET, 210 000 Iterationen, anpassbar) braucht kein neues Paket. Wer Argon2 will, entscheidet das; die Hash-Kennung im Format erlaubt späteres Umstellen.
16. **Vertikaler Durchstich (Phase 7)** (2026-10-23): Die Service-Bus-Topologie steht in `infra/servicebus.bicep` (Azure, Rollenzuweisungen je Managed Identity, `disableLocalAuth`) und `infra/servicebus-emulator.json` (lokal, Emulator-Limit 1 h Lebensdauer); `tests/Architecture.Tests/ServiceBusTopologyTests.cs` hält beide, den Nachrichtenkatalog und die in den Diensten registrierten Subscriptions zusammen. Das Topic für `ShiftUncovered` entsteht mit dem ersten Herausgeber (der Emulator verlangt je Topic mindestens eine Subscription). `BcSyncSucceeded`/`BcSyncFailed` tragen die Routing-Eigenschaft `SourceService` (aus dem Nachrichtenfeld), die SQL-Filter der Subscriptions nutzen. Lokal setzt der AppHost die Schlüssel für die Dienst-Tokens (Entscheidung 18, ersetzt das frühere Fake-Token); Personen-Tokens prüfen die Dienste gegen Access (`Authentication:RequireHttpsMetadata=false` nur lokal). Das Portal-Gerüst hält Kopien von Mitarbeitenden/Filialen, löscht bei `EmployeeDataErasureRequested` und bestätigt `EmployeeAvailabilityChanged` nur (vorbereitet; `EmployeeHired`/`EmployeeLeft` konsumiert es noch nicht). Workforce fordert für Krankmeldungen nur InApp-Benachrichtigungen an (kein Push/Mail). Entra-Paket und -Skripte sind mit Entscheidung 18 entfallen.
17. **Feiertage und Genehmigung** (2026-10-23): Bundesland ist Bayern (`BY`) für alle Filialen; der Augsburger Friedensfest (8.8.) gilt für Filialen, deren Adresse die Stadt Augsburg nennt; Mariä Himmelfahrt ist als offen markiert und zählt nicht. Genehmigen darf nur, wer für das Thema `absence.approval` zuständig ist (andere Zuständigkeiten wie Revisor berechtigen nicht); einer von mehreren Zuständigen genügt. BC-Änderungserkennung: Webhook bevorzugt (Azure-Hosting, mit seltenem Polling als Sicherheitsnetz), Umsetzung nach Klärung, ob die BC-API-Seiten Webhooks unterstützen.
18. **Dienst-zu-Dienst-Authentifizierung ohne Entra** (2026-10-26, ersetzt die Entra-/Managed-Identity-Aussagen des Plans und der Entscheidung 13): Es gibt keinen Entra-Test-Tenant und keine App-Registrierungen für die APIs. Jeder Dienst hat ein eigenes Schlüsselpaar (ECDSA P-256, ES256) und signiert für jeden Aufruf ein kurzlebiges Token (`iss=svc:<name>`, `aud=<Ziel>`, 2 Minuten, einmalige `jti`); Empfänger kennen nur die öffentlichen Schlüssel ihrer Aufrufer und die diesen gewährten Service Roles (`ServiceAuth:Peers`, Konfiguration, nie `appsettings*.json`). Rollen je Fähigkeit bleiben im Code, die Vergabe ist Konfiguration; unbekannte Rollen/Schlüssel verhindern den Start. TLS zwischen den Diensten ist Pflicht. Details, Rotation und Grenzen (Replay-Schutz je Instanz): `docs/service-auth.md`. Entra bleibt nur dort, wo ein externer Dienst es verlangt: BC Integration → Business Central (Cloud) und, bei Azure-Hosting, der Service-Bus-Zugriff (Entscheidung 8, `infra/servicebus.bicep`).

19. **PIN-Prüfung identisch zum HR-Tool** (2026-10-26): BC Integration prüft Personalnummer und PIN genau wie das HR-Tool (`wghn-hr-tool`) und das Archiv: SOAP-Aufruf der Codeunit `Zeiterfassung`, Funktion `GetStatus` mit `employeeNo`, `queryDate` (heutiges Datum, UTC) und `password` (die PIN) an `.../WS/{Firma}/Codeunit/Zeiterfassung` (Einstellungen `Bc:SoapBaseUrl`, `Bc:CompanyName`). BC vergleicht die PIN selbst und antwortet im `return_value` mit `{"success":bool,"employeeName":...}`; angemeldet ist nur, wer `success:true` **und** einen Namen hat. Eine leere PIN geht nie an BC (BC würde sie akzeptieren). Nicht übernommen vom HR-Tool: PIN im Browser-Speicher, PIN bei jedem Aufruf, Testmodus. Noch nie gegen echtes BC gelaufen (nur WireMock); der Sandbox-Test dafür steht bereit (`BC_SANDBOX_EMPLOYEE_NO`/`_PIN`).

20. **Krankmeldung mit AU-Upload in Workforce** (2026-10-26): Krankmeldungen gibt es in BC nicht in dieser Form; Workforce bildet sie ab. Die Krankmeldung braucht **keine** Datei, die Arbeitsunfähigkeitsbescheinigung (AU) kann jederzeit nachgereicht werden (auch mehrfach, z. B. Folgebescheinigungen); beim Hochladen trägt die Person den Zeitraum laut AU ein (von/bis), der die Abwesenheit automatisch pflegt. **HR prüft** (bestätigt oder lehnt mit festem Grund ab), dass eine AU hochgeladen wurde; **die Datei öffnen nur HR (`certificate:review`) und die hochladende Person**; wer den Status sehen darf (`certificate:read-status`, z. B. Filialleitung und höhere Stellen) sieht, **dass** eine AU da ist, bekommt beim Öffnen aber 403; wer den Status nicht sehen darf, bekommt 404. HR sieht und filtert den Status (`Missing`/`Submitted`/`Verified`/`Rejected`, `GET /api/sick-notes`); Personen ohne AU werden erinnert (InApp, Push generisch, keine Daten), frühestens am ersten Tag zurück (Tag nach dem Ende der Krankmeldung; ohne Ende keine Erinnerung); der Rhythmus ist von der Personalabteilung pflegbar (gleiches Recht wie die Löschfrist). **Eigene Dokumentklasse** (weicht von der Plan-Regel „Klasse 3 nur HR API“ ab, DSB-Abstimmung offen): Datei nur in Workforce, AES-256-GCM verschlüsselt, nie auf dem Bus, nie nach BC, nie in Logs; **getrennter Speicher** (eigenes Verzeichnis `Workforce:Certificates:StoragePath` außerhalb des Anwendungsverzeichnisses statt Datenbank, hinter `ICertificateFileStorage`), Dateien nur unter zufälliger UUID ohne Endung, Person, Datum und Typ nur in der Datenbank; Zugriffsprotokoll ohne Inhalt; **Löschfrist** nach Ende der Krankmeldung von der Personalabteilung selbst einstellbar (`certificate:manage-retention`, Standard 365 Tage; auch abgelehnte Dateien bleiben bis dahin), **manuelles Löschen** durch die Personalabteilung (`certificate:delete`) oder die hochladende Person (ungeprüfter Upload) und bei Löschauftrag; keine Inhaltsauswertung (kein OCR); die eAU bleibt bei DATEV. Rechte einzeln in Access: `certificate:upload-own`, `upload-for`, `read-status`, `review`, `delete`, `manage-retention`, `read-access-log`. Details, Konfiguration und offene Punkte: `docs/krankmeldung-au.md`.

21. **Hauptfiliale pflegbar, BC-Vorgabefiliale nur Vorschlag** (2026-10-27): In BC ist „Vorgabefiliale = Ja“ in der Filialverknüpfung effektiv die Hauptfiliale, aber nicht bei jeder Person gepflegt (Prod-Beispiele: eine Person mit 7 Filialen und genau einer Vorgabe, eine ohne Zeilen). Deshalb: BC liefert den Vorschlag (`EmployeeChanged.HomeStoreId`, in der Regel richtig, wo vorhanden), **Access pflegt die Hauptfiliale manuell** (`PUT/DELETE /api/people/{id}/main-store`, Recht `access:main-store:update`, Reichweite über Person **und** Filiale, `GET` mit `access:person:read`) und diese gewinnt; BC-Änderungen überschreiben sie nie, Löschen der Pflege stellt den BC-Wert wieder her. Die wirksame Hauptfiliale (Access, sonst BC) steht additiv in `PermissionsChanged.MainStoreId`; Workforce übernimmt sie (versionsgeprüft, getrennt vom BC-Wert), Portal noch nicht (Gerüst, `TODO`). Sie bestimmt Reichweiten (`IncludeHomeStore`), Zuständigkeiten und Stadt/Feiertage.
22. **Funktion/Position zunächst manuell** (2026-10-27): Die Funktion im Unternehmen ist in BC derzeit unsauber gepflegt. Zum Release (Phase 1) werden Rollen und Zuständigkeiten ausschließlich manuell in Access vergeben; ein automatischer Import (Funktion → Rolle, Entscheidung 13 „Weitere Automatik“) kommt erst, wenn das BC-Team die Datenhaltung bereinigt hat. Es werden dafür keine BC-Positionen abgefragt.
23. **Arbeitstage und Feiertage** (2026-10-27): Es gibt keine Standard-Arbeitstage; sie hängen an der Person und ihrer Abmachung mit HR (Rahmen Mo–Sa, `WorkforceOptions.WorkingDays`; pro Person später pflegbar, offen). Gesetzliche Feiertage sind **Schließtage**; Filialen, die an Feiertagen öffnen (v. a. Bahnhofsfilialen), werden **pro Filiale** als „an Feiertagen geöffnet“ markiert (`GET/PUT /api/stores/{id}/holiday-opening`, Recht `absence:manage-holiday-opening`, scopebar, gepflegt vom zuständigen Bezirksleiter); dann zählen Feiertage für deren Mitarbeitende als Arbeitstage (Urlaubstage, Abwesenheiten). Die Einstellung wirkt auch rückwirkend auf berechnete Salden (Berechnung aus den Zeiträumen). Feiertag je Datum (z. B. Ostern offen, Weihnachten zu) ist nicht gebaut.
24. **Urlaubsanspruch in Workforce statt in BC** (2026-10-27): In BC liegt der Anspruch heute je Person in der Liste „Urlaubstage“ der Personalplanung (unsauber: z. B. Resturlaub als negativer Jahresanspruch). Workforce führt ihn neu, gepflegt von HR: (a) der **vertragliche Jahresanspruch** je Jahr (`PUT /api/employees/{id}/vacation-entitlements/{year}`, wie bisher), (b) **Zusatztage** (`/api/employees/{id}/vacation-extra-days`; Art `CarryOver` = Resturlaub aus Jahr Y, nutzbar ab 1.1.Y+1, oder `Additional` = Zusatz-/Sonderurlaub für Jahr Y, nutzbar ab 1.1.Y), Recht `absence:manage-entitlement`. Ergebnis ist der **effektive Urlaubsanspruch** (Feld `entitlement` bzw. `EffectiveDays` im Urlaubskonto). Regeln: Zusatztage werden **vor** den vertraglichen Tagen verbraucht (früher verfallende zuerst); ob und wann sie verfallen, **pflegt HR** (`GET/PUT /api/vacation-rules`, Recht `absence:manage-vacation-rules`): Monat und Tag im Jahr nach dem Bezugsjahr, z. B. 31.3. („nach dem ersten Quartal des nächsten Jahres“); **Standard: sie verfallen nie**. Eine Änderung gilt für alle bereits erfassten Zusatztage; im **Eintrittsjahr** gilt Jahresanspruch / 12 × volle Monate (Eintritt am 1. zählt den Monat mit), **aufgerundet auf halbe Tage** (2,083 → 2,5; 23,33 → 23,5). Der Eintritt kommt aus BC (`EmployeeChanged.EntryDate`); ohne Eintrittsdatum gilt der volle Anspruch. Rechenwege und Beispiele: `docs/urlaubsberechnung.md`. Die BC-Liste „Urlaubstage“ wird nicht mehr als Quelle gebraucht (Import entfällt).
25. **BC-Änderungserkennung: Polling zuerst** (2026-10-27): Es ist nicht garantiert, dass alle BC-Quellen API-v2.0-Seiten sind (nur dort gibt es Webhooks); deshalb wird zuerst über `lastModifiedDateTime` abgefragt, Webhooks kommen später als Beschleuniger (mit Polling als Sicherheitsnetz). Das Intervall ist noch offen.
26. **Feiertagsöffnung als Mehrfachaktion** (2026-10-27): Zusätzlich zu Entscheidung 23 setzt `PUT /api/stores/holiday-opening` mit `{"storeIds":[…],"opensOnHolidays":true}` viele Filialen auf einmal (alle oder keine; eine Filiale außerhalb der Reichweite verweigert alles).
27. **Rechte mit Beschreibung** (2026-10-27): Jedes Recht trägt eine Beschreibung in Klartext (Deutsch), die Access im Katalog führt (`GET /api/catalog`, Feld `description`), damit beim Zusammenstellen von Rollen klar ist, was ein Recht bewirkt. Die Dienste liefern sie bei der Registrierung mit (`CatalogPrivilege.Description`), Access für die eigenen Rechte selbst, eigene Rechte (`custom`) optional beim Anlegen. Ein Architekturtest (`PrivilegeRulesTests`) verlangt eine Beschreibung für jedes Recht jedes Dienstes. Die Vergabe der Rechte an Rollen erfolgt zum Schluss in der Oberfläche.
28. **Urlaubsregeln durch HR pflegbar** (2026-10-27): Was gilt, entscheidet die Personalabteilung unter sich; Entwickler und Projektverantwortliche stellen nichts ein. Pflegbar sind (a) die **Regeln für alle** (`GET/PUT /api/vacation-rules`, Recht `absence:manage-vacation-rules`): Verfall der Zusatztage (Standard: nie), **Wartezeit** für neue Mitarbeitende in Monaten (Standard: 0 = keine) mit Modus `Accrued` (nur bis dahin erworbene Tage, 1/12 je vollem Monat seit Eintritt) oder `Blocked` (kein Urlaub), **Rundung** anteiliger Tage (`Up` Standard, `Nearest`, `Down`, `None`; Schritt 0,5 oder 1), und (b) **Änderungen des Jahresanspruchs im Jahr** je Person (`/api/employees/{id}/vacation-entitlement-changes`, Recht `absence:manage-entitlement`), z. B. bei Wechsel der Wochentage oder Teilzeit: ab dem 1. des Monats, bei Datum mitten im Monat ab dem Folgemonat, nur im Jahr des Datums; der Jahresanspruch des Jahres wird monatsweise (1/12) aus dem jeweils geltenden Wert summiert und gerundet. Die Wartezeit prüft nur Anträge (`422` `WaitingPeriod`), sie ändert das angezeigte Konto nicht. Alle Regeln gelten rückwirkend für Berechnungen. Austritt im Jahr (anteiliger Anspruch): noch zu klären, nicht gebaut.

## Arbeitsregeln

- Phasen strikt der Reihenfolge nach (0 Bestandsaufnahme → 1 Grundgerüst/Shared → 2 Contracts → 3 BC Integration → 4 Access → 5 Notification → 6 Workforce-Abwesenheiten → 7 Durchstich). Jede Phase endet an einem **Stopp-Punkt**: zusammenfassen (gebaut, Tests grün, offene Fragen) und auf Freigabe warten.
- Vorgehen je Phase: Aufgaben/Abnahmekriterien lesen → betroffenen Bestandscode prüfen → **Tests zuerst** für die Fachlogik → Implementierung → `dotnet build` und `dotnet test` für die gesamte Solution fehlerfrei → erledigte Aufgaben abhaken, am Stopp-Punkt berichten.
- Phase 0 ändert keinen Code; Ergebnis ist `docs/phase-0-bestandsaufnahme.md` (pro Dienst Tabelle Ist / Soll / Umbauaufwand klein-mittel-groß, Änderungen begründet, offene BC-Fragen gesammelt).
- Kleine, nachvollziehbare Commits pro Aufgabe.
- Bei Unsicherheit nachfragen, keine stillschweigenden Annahmen.

### Nie ohne Rückfrage

- Vorhandenen Code löschen oder grundlegend umschreiben (Änderung vorschlagen, Zustimmung abwarten).
- Von den Architekturentscheidungen oder Datenschutz-Regeln abweichen, auch wenn es einfacher wäre.
- Neue NuGet-Pakete außerhalb des Stacks unten aufnehmen.
- Felder/Tabellen für BC erfinden. Bei unklarem BC-Modell: Platzhalter `// TODO(BC-Modell): ...`, am Stopp-Punkt auflisten.
- Irgendetwas an der bestehenden Inventur-Anwendung ändern.
- Neue Nachrichten oder neue Konsumenten über den Katalog hinaus ergänzen.
- Entfernen veralteter synchroner Prüf-Endpunkte (Access) oder von Fachevent-Logik im Notification Service.

## Konventionen

- Code, Bezeichner, Commit-Messages: Englisch. Fachbegriffe dürfen deutsch kommentiert werden.
- Kein `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.UtcNow` — ausschließlich `TimeProvider`.
- Nullable Reference Types aktiv, `TreatWarningsAsErrors`.
- Keine Secrets, Connection Strings oder Keys im Code oder in `appsettings*.json`. Lokal Aspire bzw. User Secrets, später Key Vault.
- Weiterentwicklung von Nachrichten nur additiv; brechende Änderung = neue Nachricht `...V2`, alte läuft parallel.

## Verbindliche Architekturentscheidungen

APIs nach Fachlichkeit geschnitten. **Jede Datenart hat genau einen Besitzer, nur dieser schreibt.** Andere halten höchstens eine schreibgeschützte Kopie.

| Dienst | Besitzt (schreibt) | Hält als Kopie | Genutzt von |
| --- | --- | --- | --- |
| BC Integration | Sync-Status, ID-Mapping (BC-SystemId ↔ interne ID), Outbox, Fehlerprotokoll | – | allen anderen Diensten, nie von Frontends |
| Access | Filial-Scopes, Hierarchie, Vertretungen, Sonderrechte | Mitarbeitende, Filialen | allen APIs (Events + lokaler Cache) |
| Notification | Vorlagen, Zustellstatus, Präferenzen, Push-Gerätetokens | – | allen Fach-APIs per Command |
| Workforce API | Dienstpläne, Schichten, Schichttausch, Stempelungen, Zeitkorrekturen, Abwesenheiten, Urlaubskonten, Monatsabschlüsse | Mitarbeitende, Filialen | Portal-Frontend, HR-Frontend, HR API |
| Portal API (`Portal`) | News, FAQ, Glossar, Filialordner, Schulungen, Quiz, On-/Offboarding, Berichtshefte, Aufgaben, Incidents, Kassen-/Losbestände | Mitarbeitende, Filialen, aggregierte Verkaufsstatistiken | Portal-Frontend |
| HR API | Recruiting, Lohnarten-Mapping, DATEV-Exportläufe | Mitarbeitende | HR-Frontend |

Umgesetzt werden in diesem Plan nur: BC Integration, Access, Notification, Workforce (nur Abwesenheiten) + minimales Portal-API-Gerüst.

### Kommunikation

1. **Events** (Service Bus Topics): Zustandsänderungen, minimaler Inhalt. Jede Subscription ist eine explizite, dokumentierte Freigabe.
2. **Commands** (Service Bus Queues): beauftragen genau einen Empfänger.
3. **Synchrones HTTP zwischen APIs** nur, wenn sofortige Antwort nötig und Kopie unpassend ist, oder für autorisierten Abruf sensibler Details. Jeder solche Endpunkt hat eine eigene App Role.
4. **Frontends** sprechen direkt mit der besitzenden API, nie über eine andere API als Durchreiche; kein Service-Bus-Zugang.
5. **BC** nur über den BC Integration Service. Keine andere API hat BC-Zugangsdaten oder BC-Client-Code.

### Zuverlässigkeit

- Zustandsänderung + ausgehende Nachricht in **derselben DB-Transaktion** (Outbox).
- Empfänger idempotent (Inbox / gespeicherte Nachrichten-IDs); Service Bus ist „at least once“.
- BC-Schreibvorgänge sind Upserts über externe ID (interne GUID).
- Der Besitzer einer Entität speichert deren BC-Sync-Status (`Pending`, `Synced`, `Failed`) und aktualisiert ihn über `BcSyncSucceeded`/`BcSyncFailed`.

### Autorisierung

- Personen: Access-Token und ACL (Entscheidung 13, Plan: Entra ID mit groben App Roles entfällt).
- Feingranulare Scopes (Filialen, Mitarbeitende) verwaltet der Access Service, veröffentlicht `PermissionsChanged`; jede API hält lokalen Cache.
- Prüfung lokal in jeder API über `Shared.Authorization`. Access Service nie im Request-Pfad; bei Ausfall letzter bekannter Stand.
- Service-zu-Service: signierte Kurzzeit-Tokens mit Schlüsselpaar je Dienst, Service Role pro Fähigkeit (z. B. `Workforce.AbsenceDetails.Read`), Entscheidung 18 (Plan: Managed Identity).

### Genehmigungen

Über Bibliothek `Shared.Approvals`, Daten in der DB der einbindenden API. Kein eigener Approval-Service.

## Datenschutz-Regeln

Jede Nachricht und jeder Endpunkt hat eine Sensibilitätsklasse. Im Zweifel die strengere.

| Klasse | Beispiele | Auf dem Bus | Details abrufen |
| --- | --- | --- | --- |
| 1 – Organisationsdaten | Filiale, Personalnummer, Name, Hauptfiliale, Position, Arbeitsverhältnis-Typ, Sollstunden | voller Zustand | – |
| 2 – Sensible Fakten | Abwesenheitsgrund, Zeitkorrekturen, Quizergebnisse, Incident-Inhalte | nur neutralisiert („nicht verfügbar“, ohne Grund) oder dünnes Event (nur IDs) | autorisierter HTTP-Abruf beim Besitzer mit eigener App Role |
| 3 – Hochsensibel | Gehalt, Bankverbindung, Steuer-/SV-Daten, Bewerbungsunterlagen, Diagnosen | **nie** | nur HR API, direkt über BC Integration bzw. DATEV |

- Keine Diagnosen/medizinischen Details speichern oder auswerten. Krankmeldung = Zeitraum, Art „Krank“ und AU-Status; die hochgeladene AU ist ein eigenes, besonders geschütztes Dokument (Entscheidung 20).
- Jede Nachrichten-Klasse trägt `[Sensitivity(SensitivityClass.X)]` (Contracts). Architekturtest: jede Nachricht klassifiziert, keine Nachricht der Klasse 3. Klassenänderung geschieht an genau einer Stelle.
- **Logging:** nie Nachrichteninhalte oder Request-Bodies. Erlaubt: Nachrichtentyp, Nachrichten-ID, Korrelations-ID, Entitäts-IDs — auch in Messaging-Bibliothek und OpenTelemetry-Konfiguration.
- **Push/Mail:** nie Klasse-2-Inhalte in Push-Texten oder Mail-Betreffs. Push-Text generisch („Neue Mitteilung im Portal“), Inhalt wird nach Öffnen per API geladen.
- **Outbox/Inbox:** verarbeitete Einträge nach 7 Tagen löschen (konfigurierbar).
- **Dead-Letter-Queues:** in der Infrastrukturdefinition mit gleicher Zugriffsbeschränkung wie die Fachdaten.
- **Löschung:** Jede API mit Mitarbeiterkopien reagiert auf `EmployeeDataErasureRequested` mit Löschen/Anonymisieren; pro API ein Integrationstest.
- **Service Bus:** kein Shared Access Key. Managed Identity, nur Senderechte auf eigene Topics/Queues, Leserechte nur auf freigegebene Subscriptions. Jede Subscription trägt in der Infra-Definition einen Zweck-Kommentar.
- Die Klassifizierung ist ein Entwurf (Abstimmung mit Datenschutzbeauftragten steht aus).

## Technischer Stack

.NET 10, ASP.NET Core Minimal APIs (Controller nur, wo Bestandscode sie nutzt), PostgreSQL + EF Core/Npgsql (**ein Schema pro Dienst, keine Cross-Schema-Joins**), Azure Service Bus über `Azure.Messaging.ServiceBus` mit eigener Outbox/Inbox (EF Core) in `Shared.Infrastructure` hinter einem Transport-Interface (kein Wolverine, siehe Entscheidung 8), .NET Aspire AppHost + ServiceDefaults (Service-Bus-Emulator, PostgreSQL, WireMock als Container), OpenTelemetry über ServiceDefaults.

Tests: xUnit (Bestandswahl übernehmen), FluentAssertions oder Shouldly, `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`), `WebApplicationFactory` + Testcontainers (PostgreSQL, Service-Bus-Emulator; **kein EF-In-Memory-Provider**), WireMock.Net (Antworten als Dateien unter `tests/Fixtures/Bc`), Verify (Snapshots), NetArchTest.Rules oder ArchUnitNET, `Aspire.Hosting.Testing` für E2E.

### Zielstruktur

```
/src
  /AppHost, /ServiceDefaults
  /Shared  (Shared.Authorization | Shared.Approvals | Shared.Messaging | Shared.Infrastructure | Shared.Testing)
  /BcIntegration, /Access, /Notifications, /Workforce, /Portal
     jeweils: <Dienst>.Api | .Core | .Infrastructure | .Contracts
/tests
  /<Dienst>.Core.Tests, .Api.Tests, .Contracts.Tests, /Architecture.Tests, /Fixtures
/infra   Bicep (nur Service-Bus-Topologie)
/Archiv  Bestandsdienste (Referenz, Entscheidung 10)
```

### Projektabhängigkeiten

- `*.Core`: keine Infrastruktur (kein EF Core, Service Bus, HTTP-Client); intern nach Features gegliedert (`Absences/`, `ShiftPlanning/`), nicht nach technischen Schichten.
- `*.Contracts`: keine Abhängigkeiten außer `Shared.Messaging` (Sensitivity-Attribut).
- `*.Core` darf `Shared.Messaging` und `Shared.Approvals` nutzen, nie `Shared.Infrastructure`; das ist `*.Infrastructure` vorbehalten.
- Ein Dienst darf einen anderen Dienst ausschließlich über dessen `*.Contracts` referenzieren.

## Nachrichtenkatalog (nur diese; neue erst nach Rückfrage)

| Nachricht | Art | Herausgeber | Konsumenten | Klasse |
| --- | --- | --- | --- | --- |
| `EmployeeChanged` | Event | BC Integration | Workforce, Portal, Access | 1 |
| `EmployeeHired` | Event | BC Integration | Portal, Access | 1 |
| `EmployeeLeft` | Event | BC Integration | Workforce, Portal, Access | 1 |
| `EmployeeDataErasureRequested` | Event | BC Integration | alle Dienste mit Kopien | 1 |
| `StoreChanged` | Event | BC Integration | Workforce, Portal, Access | 1 |
| `PermissionsChanged` | Event | Access | alle APIs | 1 |
| `EmployeeAvailabilityChanged` | Event | Workforce | Portal (vorbereitet) | 2, neutralisiert (kein Grund) |
| `ShiftUncovered` | Event | Workforce | – (vorbereitet) | 2, neutralisiert |
| `SyncAbsenceToBc` | Command | Workforce | BC Integration | 2 (darf Grund enthalten; Queue nur von BC Integration lesbar) |
| `BcSyncSucceeded` / `BcSyncFailed` | Event | BC Integration | Absender (gefilterte Subscription auf `SourceService`) | 1 (Fehlercode ohne Inhalte) |
| `SendNotification` | Command | alle Fach-APIs | Notifications | je nach Daten |
| `ResponsibilitiesChanged` | Event | Access | Workforce (weitere bei Bedarf) | 1 (Entscheidung 13, ergänzt 2026-10-23) |

- Jede Nachricht: eindeutige ID (Idempotenz), Korrelations-ID, Zeitstempel aus `TimeProvider` (`IMessage`: `MessageId`, `CorrelationId`, `OccurredAt`).
- Contracts: `sealed record`, unveränderlich, nur primitive Typen/`DateOnly`/`DateTimeOffset`/Enums/Records; XML-Doku (Zweck, Herausgeber, erlaubte Konsumenten); Enums mit `Unknown = 0`, explizite Werte, String-Serialisierung; Verify-Snapshot pro Nachricht; je Konsument Test, dass unbekannte JSON-Felder ignoriert werden.
- `SendNotification.Data` darf Klasse-2-Inhalte nur enthalten, wenn `Channels` ausschließlich `InApp` ist; der Notification Service lehnt Verstöße ab.
- **Umsetzung (Phase 2):** Klassen im Code: `OrganizationData` (1), `SensitiveNeutralized` (2, neutralisiert/dünn, darf auf Topics), `SensitiveFacts` (2 mit Inhalt, nur Commands an zugriffsbeschränkte Empfänger), `HighlySensitive` (3, nie). Events sind höchstens `SensitiveNeutralized` (Architekturtest). Einziges Wire-Format: `MessageJson` (`Shared.Messaging`). Commands mehrerer Absender liegen in den Contracts des Empfängers (`SendNotification` → `Notifications.Contracts`). Neben `DateOnly`/`DateTimeOffset` ist `TimeOnly` (Uhrzeit, z. B. Öffnungszeiten) erlaubt. Der Katalog steht zusätzlich als Liste in `tests/Architecture.Tests/ContractRulesTests.cs`; eine neue Nachricht erfordert dort einen bewussten Eintrag.

## Dienstspezifische Kernregeln

- **BC Integration:** einziger BC-Zugang; typisierter HttpClient mit Entra-Token für Business Central, ein zentraler Rate Limiter, Retry mit `Retry-After` bei 429, Timeouts, Circuit Breaker; Domänenordner `MasterData/`, `Workforce/`, leerer `Inventory/` (nur README). Stammdaten-Abgleich über `lastModifiedDateTime`, nur echte Änderungen erzeugen Events. Mapping-Tabelle vergibt die systemweite interne GUID. `SyncAbsenceToBc`: Upsert über externe ID, ältere Versionen verwerfen. Fehlerklassen: vorübergehend (Retry) / fachlich dauerhaft (`BcSyncFailed`, kein Retry) / technisch dauerhaft (Dead Letter); Fehlercodes ohne personenbezogene Inhalte. **Keine öffentlichen Endpunkte für Frontends**, intern nur Service-zu-Service-Auth. Echte-BC-Sandbox-Tests: `[Trait("Category", "BcSandbox")]`, standardmäßig nicht ausgeführt.
- **Access:** Kopien aus `EmployeeChanged`/`StoreChanged`; abgeleitete Scopes (Filialleitung → Hauptfiliale, Mitarbeitende → sich selbst) in eigener getesteter Klasse; manuelle Zusätze (Bezirksleitung, befristete Vertretungen, Sonderrechte) nur für HR/Geschäftsführung; Vorgesetztenbeziehung; `PermissionsChanged` mit streng monoton steigender Version pro Person; Vertretungsablauf zeitgesteuert über `TimeProvider`; Austritt entzieht Rechte erst zum Austrittsdatum; Endpunkt für vollständigen Cache-Neuaufbau (nur Service-zu-Service). Alte synchrone Prüf-Endpunkte als veraltet markieren, Entfernung nur nach Rückfrage.
- **Notification:** verarbeitet nur `SendNotification` (idempotent über `NotificationId`), kein Fachwissen anderer Bereiche; Vorlagen per `TemplateKey` (`<dienst>.<anlass>.<empfängerrolle>`, zunächst Dateien); Kanäle `InApp` (echt), `Push`/`Email` als Adapter-Interface mit Fake; Pflichtbenachrichtigungen nicht abschaltbar; InApp-Aufbewahrung 90 Tage (konfigurierbar).
- **Workforce (Abwesenheiten):** Krankmeldung ohne Freitext und ohne Genehmigung, mit nachreichbarer AU (Datei, Entscheidung 20); Urlaub mit Genehmigungskette (direkte Vorgesetzte laut Access); Urlaubskonto, Antrag über Rest → fachlicher Fehler; keine überlappenden Abwesenheiten (Ausnahme Krankheit im genehmigten Urlaub → Urlaub für betroffene Tage storniert und gutgeschrieben); Arbeitstage Mo–Sa konfigurierbar, Feiertage als austauschbarer Dienst; jede Änderung erhöht die Version (geht an BC). Grund der Abwesenheit sehen nur die Person selbst, Vorgesetzte und HR — durch Autorisierung erzwungen, nicht im Frontend. Interner Detail-Endpunkt mit App Role `Workforce.AbsenceDetails.Read`.
- **Portal API (`Portal`):** in diesem Plan nur Gerüst (Struktur, Auth, Kopien, `/health`), keine Fachfunktionen.

## Tests und Definition of Done

Dichteste Tests bei Zeit-/Abwesenheitslogik, BC-Sync, Berechtigungen, Datenschutz. Ebenen: Unit, Integration, Vertrag (Verify), Architektur, E2E (Aspire + WireMock, lokal < 5 min), BC-Sandbox (nur manuell).

Pflichtfälle für jede Regel mit Datums-/Zeitbezug: Monats- und Jahreswechsel, Sommer-/Winterzeitumstellung, Schaltjahr (29. Feb.), gesetzlicher Feiertag, Zeitraum über Mitternacht.

Definition of Done pro Aufgabe:

- [ ] Tests zuerst geschrieben, alle grün.
- [ ] `dotnet build` ohne Warnungen, `dotnet test` für die gesamte Solution grün.
- [ ] Keine Nachrichteninhalte oder personenbezogenen Daten in Logs.
- [ ] Jede neue Nachricht im Katalog, klassifiziert, mit Snapshot-Test.
- [ ] Jeder neue Endpunkt mit mindestens einem positiven und einem negativen Berechtigungstest.
- [ ] Offene Annahmen als `TODO(...)` markiert und am Stopp-Punkt gemeldet.

## Offene Entscheidungen (nicht selbst entscheiden — Platzhalter nutzen, an jedem Stopp-Punkt melden)

BC-Stammdaten-Feldliste (Phase 2, 3) · BC-Datenmodell Workforce / Upsert über externe ID (3, 6) · BC-Änderungserkennung Webhooks vs. Polling, Intervall (3) · Mariä Himmelfahrt (15.8.) in bayerischen Gemeinden (6) · Urlaub: anteiliger Anspruch bei Austritt im Jahr (6) · Anlegen neuer Mitarbeitender und Übertragung des Eintrittsdatums nach BC (welcher Dienst, neue Nachricht) · Datenklassifizierung und Aufbewahrungsfristen (alle) · Betriebsrat-Einbindung (vor Go-live) · Hosting-Umgebung (offen; Transport ist deshalb austauschbar, Entscheidung 8) · AU: Betrieb von ClamAV in Produktion (lokal im AppHost, Adapter gebaut) und Schlüsselverwaltung/-rotation (`docs/krankmeldung-au.md`). · BC-Schnittstelle Zeiterfassung: Fragen F1–F7 und Entscheidungen D1–D3 in `docs/bc-zeiterfassung-schnittstelle.md` (PIN bei jedem Aufruf, Genehmigungsstufen, Antragsschlüssel, Audit-Log).

## Außerhalb des Scopes

Inventur-Anwendung (nur leerer `Inventory/`-Ordner), Deployment/CI/CD und Azure-Ressourcen außer Service-Bus-Bicep, Frontends, HR API, Dienstplanung/Zeiterfassung/Schichttausch, alle Portal-API-Fachfunktionen, echte Push-/Mail-Zustellung, automatische Löschfristen. Wird so etwas berührt, nur Schnittstelle oder Platzhalter vorbereiten.
