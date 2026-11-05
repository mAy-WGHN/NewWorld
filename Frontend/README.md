# NewWorld Frontend

Das globale Frontend des Mitarbeiterportals: ein Angular-Rahmen (Version 22, Standalone-Komponenten, Signals, zoneless) mit Anmeldung und einer Navigation, die sich nach den Rechten der angemeldeten Person richtet. Die Fachbereiche (Abwesenheiten, Arbeitszeitkonto, Mitarbeiterkarte, FAQ, Meldungen, Verwaltung …) sind als Platzhalterseiten angelegt und werden nacheinander gebaut.

**Oberfläche:** Angular Material (M3-Theme) und TailwindCSS v4 (PostCSS, `src/tailwind.css`, Farben aus den Material-Systemvariablen). Gebaut sind u. a. Krankmeldungen, Urlaub, Genehmigungen, Meldungen, Störung melden, Mitteilungen und Arbeitszeitkonto. Dienstpfade im Dev-Proxy liegen unter `/svc/...`, damit sie nicht mit Seiten wie `/notifications` kollidieren. Nach Änderungen an `angular.json`, Tailwind oder `proxy.conf.json` den Dev-Server neu starten.

## Starten

```bash
cd Frontend
npm install
npm start          # http://localhost:4200
```

Voraussetzung sind die Backend-Dienste (zum Beispiel über den Aspire-AppHost). Der Dev-Server leitet Aufrufe über [proxy.conf.json](proxy.conf.json) weiter:

| Pfad im Browser | Dienst | Standard-Ziel |
| --- | --- | --- |
| `/svc/access/*` | Access (Login, Rechte) | `http://localhost:5101` |
| `/svc/workforce/*` | Workforce | `http://localhost:5102` |
| `/svc/portal/*` | Portal | `http://localhost:5103` |
| `/svc/notifications/*` | Notifications | `http://localhost:5104` |

Aspire vergibt die Ports dynamisch: die Ziele in `proxy.conf.json` auf die Adressen aus dem Aspire-Dashboard anpassen. Der Proxy entfernt das Präfix, die Dienste sehen ihre normalen Pfade (`/api/...`). Die Dienste brauchen so kein CORS.

## Konfiguration im Betrieb

[public/config.json](public/config.json) wird beim Start geladen und legt fest, unter welchem Pfad der Browser jeden Dienst erreicht (zum Beispiel hinter einem Reverse Proxy). So läuft derselbe Build in jeder Umgebung.

## Anmeldung

- Personalnummer und BC-PIN gehen an `POST /api/auth/login` von Access. Die PIN wird nirgends gespeichert und nach jedem Versuch aus dem Formular entfernt.
- Das Access-Token liegt nur im Speicher. Das rotierende Refresh-Token liegt im `sessionStorage` des Tabs, damit ein Neuladen die Sitzung behält. Beim Schließen des Tabs endet sie.
- Der Interceptor hängt das Token an Aufrufe der eigenen Dienste, erneuert bei `401` einmal über `/api/auth/refresh` und wiederholt den Aufruf. Parallele Erneuerungen teilen sich einen Aufruf, weil das Refresh-Token nur einmal gilt. Schlägt die Erneuerung fehl, geht es zur Anmeldung zurück.
- Die Rechte kommen von `GET /api/me/privileges` (Access löst Wildcards schon auf, das Frontend vergleicht Schlüssel exakt).

Die Rechteprüfung im Frontend steuert nur, was angezeigt wird. Durchgesetzt wird sie von den Diensten bei jedem Aufruf.

## Rahmen und Startseite

Das Aussehen folgt dem Entwurf der Startseite (Wintergerst Mitarbeiterportal): Kopfzeile mit Suche („Was möchtest du erledigen?“), Glocke mit Zähler und der angemeldeten Person; Seitenleiste (oben Startseite, Alle Bereiche, Favoriten; darunter die Bereiche der Person nach Gruppen, jeder einen Klick entfernt und nur, was sie öffnen darf; unten Einstellungen und Hilfe; die Liste der Bereiche kommt aus `core/nav/nav-items.ts`); auf der Startseite die Begrüßung, die vier Kacheln Erledigen, Finden, Lernen und Mein Bereich, „Meine Aufgaben“, „Meine nächsten Termine“, Kennzahlen und „Aktuelles aus der Zentrale“. Es gibt ein helles und ein dunkles Schema (nach Einstellung des Geräts) und eine Handyansicht.

Die Startseite zeigt nur echte Daten, nichts Erfundenes:

| Teil | Quelle |
| --- | --- |
| Begrüßung, Name, Personalnummer | eigene Mitarbeiterkarte in Workforce (`/api/me/employee-card`); ohne Karte, etwa bei Personen nur aus BC, ohne Namen |
| Glocke und „ungelesene Mitteilungen“ | Notifications (`/api/me/notifications/unread-count`), alle 60 Sekunden aktualisiert |
| „Meine Aufgaben“ | fehlende AU der eigenen Krankmeldungen, offene Urlaubsanträge zum Entscheiden (`/api/approvals/pending`), ungelesene Mitteilungen |
| „Meine nächsten Termine“ | eigener Urlaub, der läuft oder noch kommt |
| Schnellzugriffe der Kacheln | nur Bereiche, die es gibt und die die Person öffnen darf |
| Suche | die Bereiche der Person, nach Name und gängigen Begriffen |
| Favoriten | im Browser gemerkt (Stern unter „Alle Bereiche“) |

Noch ohne Daten und deshalb als „folgt“ gekennzeichnet: Kennzahlen der Filiale, Aktuelles aus der Zentrale, Schulungen, Anleitungen und Filialordner. Auch Filialname unter dem Namen fehlt: `TODO(Backend)`, es gibt noch keinen Aufruf „Wer bin ich“ mit Name und Hauptfiliale (siehe `ProfileService`).

## Fertige Bereiche

| Bereich | Pfad | Dienst |
| --- | --- | --- |
| Meine Abwesenheiten | `/absences` | Workforce (`/api/me/absences`, `/api/me/vacation-balance`, AU-Upload) |

Meine Abwesenheiten: Urlaubskonto je Jahr, Krankmeldung (Erst- oder Folgekrankmeldung, mit Pflicht-Ende, AU gleich dabei oder eAU, siehe unten), Urlaub beantragen, Urlaub zurückziehen, offene Krankmeldung beenden und die AU hochladen (PDF, JPEG, PNG, mit dem Zeitraum laut AU). Fehler der Dienste (zum Beispiel Überschneidung oder zu wenig Urlaubstage) erscheinen als Klartext. Formulare und Aktionen sind nur mit den Rechten `absence:create-own`, `absence:update-own` und `certificate:upload-own` sichtbar. Die Dienste prüfen sie trotzdem bei jedem Aufruf.

| Meine Krankmeldungen | `/my-sick-notes` | Workforce (gleiche Aufrufe wie die Abwesenheiten) |

Meine Krankmeldungen: Der Link „Krankmeldungen“ auf der Startseite führt hierher. Oben rechts steht der Knopf „Krankmelden“ (öffnet das Formular: Art der Krankmeldung, erster und letzter Tag, AU hochladen oder „eAU“). Darunter die eigenen Krankmeldungen, neueste zuerst. Braucht die Personalabteilung noch eine AU, steht die Krankmeldung ganz oben in einem eigenen, gelb hervorgehobenen Block „AU wird noch benötigt“ und nicht noch einmal in der Liste darunter. Das gilt, wenn die AU fehlt oder von der Personalabteilung abgelehnt wurde; eine eingereichte, noch ungeprüfte oder eine geprüfte AU braucht nichts mehr. Von dort lässt sich die AU hochladen oder beenden. „AU bearbeiten“ (an jeder Krankmeldung, die Option verschwindet nie): AU nachträglich hochladen oder das Kennzeichen „eAU“ setzen und entfernen (nicht, solange eine hochgeladene AU wartet oder bestätigt ist). Ein hochgeladenes Papier-AU entfernt das Kennzeichen wieder. Die Zeilen zeigen „Erstkrankmeldung“ oder „Folgekrankmeldung“ und „eAU“.

| Mein Urlaub | `/my-vacation` | Workforce (gleiche Aufrufe wie die Abwesenheiten) |
| Meldungen | `/reports` | Portal (`/api/reports`, `/api/reports/archived`, `PATCH`, `DELETE`) |
| Störung melden | `/report-issue` | Portal (`POST /api/reports`, `GET /api/locations`) |
| Genehmigungen | `/approvals` | Workforce (`/api/approvals/pending`, `approve`, `reject`) |
| Krankmeldungen und AU (HR) | `/sick-notes` | Workforce (`/api/sick-notes`, `/api/absences/{id}/certificates`, `verify`, `reject`, `content`) |
| Benachrichtigungstexte (HR) | `/admin/templates` | Notifications (`/api/notification-templates`, Vorschau, Zurücksetzen) |
| Meine Karte | `/employee-card` | Workforce (`/api/me/employee-card`, `/api/me/employee-card/change-requests`) |
| Team-Abwesenheiten | `/team-absences` | Workforce (`/api/stores/{id}/unavailabilities`, `/api/me/unavailabilities`, `/api/unavailability-kinds`), Portal (Filialen) |
| FAQ | `/faq` | Portal (`/api/faqs`, `/api/faqs/archived`, `/api/admin/faq/images`) |
| Statistik | `/statistics` | Portal (`/api/statistics/*`) |
| Mitarbeitende | `/employees` | Workforce (`/api/employee-change-requests`, `/api/employees`, `/api/employee-lists/{kind}`) |
| Personal-Einstellungen | `/admin/hr` | Workforce (Urlaubsregeln, Arbeitszeitregeln, AU-Einstellungen, Nummernkreis, Auswahllisten, Nicht-Verfügbarkeitsarten, Feiertagsöffnung) |
| Rollen und Rechte | `/admin/access` | Access (Rollen, Katalog, Filial-Gruppen, Zuweisungen, Zuständigkeiten, Hauptfiliale) |

Alle Listenseiten folgen demselben Schema: Titel mit einem Knopf oben rechts, Übersicht der Einträge, und was Aufmerksamkeit braucht, steht ganz oben in einem gelb hervorgehobenen Block (jeder Eintrag nur einmal). Die gemeinsamen Stile liegen in `src/app/shared/_list-page.scss`.

Mein Urlaub: Knopf „Urlaub beantragen“ (öffnet das Formular), darüber das Urlaubskonto mit Jahreswechsel. Gelb stehen die Anträge, die noch auf eine Entscheidung warten („Wartet auf Genehmigung“); darunter der übrige Urlaub (genehmigt, abgelehnt, zurückgezogen). Beantragen oder bestätigten Urlaub zurückziehen, solange er nicht begonnen hat.

Störung melden: entspricht „Störung melden“ des alten Systems (irs.wintergerst-gruppe.de). Zuerst „Was möchtest du melden?“ mit den fünf Arten Fehler, Virus, Diebstahl, Sonstige Verstöße und Verbesserungsvorschlag, dann das Formular der Art. Gemeinsam sind Personalnummer\* (vorbelegt, wenn bekannt), Beschreibung\* (mit der Frage der Art) und Filiale (Suche nach Name oder Nummer, mehrere möglich, „Name (Nummer)“). Dazu kommen: bei **Fehler** der Störlevel (Standard „Mittel“); bei **Virus** „Konkreter Rechner“, „Wann bemerkt?“ (Tag und Uhrzeit), „Wie wurde es bemerkt?“ und „Was wurde davor an dem Gerät gemacht?“; bei **Diebstahl** „Datum & Uhrzeit“. Der Schweregrad ist wie im alten System je Art fest: Virus kritisch, Diebstahl hoch, Sonstige Verstöße mittel, Verbesserungsvorschlag niedrig. Prüf- und Erfolgstexte sind die des alten Systems („Bitte deine Personalnummer angeben.“, „Meldung wurde erfolgreich übermittelt.“). Erreichbar für alle Angemeldeten, ohne besonderes Recht: von der Startseite („Störung melden“), von der Navigation und mit dem Knopf oben rechts auf der Seite „Meldungen“. Noch offen: Das alte System ist ohne Anmeldung nutzbar, das neue Frontend verlangt die Anmeldung (die Schnittstelle selbst bleibt öffentlich).

Meldungen: Knopf „Störung melden“ und „Archiv“ oben rechts („Archiv“ wechselt zu den archivierten Meldungen und zurück). Gelb stehen die offenen Meldungen, die dringendsten zuerst (Reihenfolge vom Dienst); darunter die erledigten. Mit dem Recht `report:update` lassen sich Meldungen erledigen, wieder öffnen und kommentieren, mit `report:delete` archivieren; archivierte lassen sich wiederherstellen. Es wird seitenweise nachgeladen („Mehr anzeigen“). Die Reichweite (welche Filialen) bestimmt der Dienst.

Genehmigungen: Die Urlaubsanträge, die auf die Entscheidung der angemeldeten Person warten (für Zuständige laut Access, Recht `absence:approve`), stehen gelb und nach Beginn sortiert, mit Name und Zeitraum. „Genehmigen“ geht mit einem Klick, „Ablehnen“ fragt noch einmal nach („Wirklich ablehnen?“), weil es hier nicht zurückgenommen werden kann. Hat jemand anderes schon entschieden, sagt die Seite das und zeigt die aktuelle Liste. „Aktualisieren“ oben rechts lädt neu. Bereits entschiedene Anträge listet das Backend nicht; sie stehen dem Antragsteller unter „Mein Urlaub“.

Alle anderen Bereiche der Navigation sind Platzhalter.

## Aufbau

```
src/app/
  core/            Konfiguration, Auth (Service, Interceptor, Guards, Rechte), Navigation (nav-items.ts)
  layout/          Rahmen mit Kopfzeile und Navigation
  pages/           Anmeldung, Startseite, Platzhalter
  features/        Fertige Fachbereiche, je Ordner: Seite, API-Dienst, Modelle und Hilfsfunktionen
```

Neuer Bereich: Eintrag in [src/app/core/nav/nav-items.ts](src/app/core/nav/nav-items.ts) (Pfad, nötige Rechte), die Route dazu entsteht aus dem Eintrag und zeigt zunächst die Platzhalterseite. Ist der Bereich gebaut, kommt er mit seinem Pfad in `builtAreas` in `app.routes.ts` und wird bei Bedarf nachgeladen.

## Wenn eine Seite nicht öffnet

Die gebauten Bereiche (Krankmeldungen, Urlaub, Störung melden, Meldungen, Abwesenheiten) werden beim ersten Öffnen nachgeladen. Lädt ein Bereich langsam, zeigt ein dünner Balken am oberen Rand den Fortschritt. Scheitert das Nachladen (zum Beispiel weil der Dev-Server nicht mehr läuft oder ein alter Stand im Browser liegt), erscheint oben auf der Seite eine rote Meldung mit dem Grund und dem Knopf „Neu laden“, und die Seite lädt sich bei einem solchen Fehler einmal selbst neu. Ohne das bliebe die zuvor geöffnete Seite einfach stehen. Hilft Neuladen nicht: `npm start` beenden, den Ordner `Frontend/.angular` löschen (Build-Cache) und neu starten.

## Tests und Build

```bash
npm test -- --watch=false   # Vitest
npm run build               # dist/newworld-frontend
```

Krankmeldungen und AU (HR): alle Krankmeldungen der Reichweite mit dem Stand der AU, gefiltert nach „Wartet auf Prüfung“ (Standard, gelb), „AU fehlt“, „Abgelehnt“, „Geprüft“ oder „Alle“. Eine Zeile öffnet die hochgeladenen Dokumente: Datei öffnen (in neuem Tab, der Browser bekommt sie als Blob; jeder Zugriff wird im Dienst protokolliert), Bestätigen, Ablehnen mit einem festen Grund (Nicht lesbar, Falsche Person, Falscher Zeitraum, Keine AU) und Löschen nach Rückfrage. Die Knöpfe folgen den Rechten `certificate:review` und `certificate:delete`; wer die Datei öffnen darf, sagt der Dienst (`canOpen`).

Mitteilungen führen je nach Art zu einer vorhandenen Seite (`features/notifications/notification-link.ts`), nicht zu den Pfaden der Dienste; wo die Person die Seite nicht öffnen darf, öffnet sich die Mitteilungsliste.

Benachrichtigungstexte (HR, Recht `template:manage`): Alle Benachrichtigungen mit ihrem Namen und Stand („Standard“ oder „Angepasst“). „Bearbeiten“ öffnet Titel und Text je Kanal (Im Portal, Push, E-Mail). Variablen schreibt man mit Dollarzeichen (`$USER.firstName`); ein Klick auf eine Variable fügt sie am Cursor des Feldes ein, angeboten werden nur die dort erlaubten (sensible nur im Portal-Text und im E-Mail-Text, wo die Vorlage es erlaubt). Bei Benachrichtigungen mit sensiblen Daten sind Push-Text und E-Mail-Betreff fest vorgegeben (schreibgeschützt). „Vorschau“ zeigt das Ergebnis mit Beispielwerten, ohne zu speichern; „Auf Standard zurücksetzen“ fragt vorher nach. Der Dienst prüft jeden Text beim Speichern noch einmal; Fehler erscheinen als Klartext.

Meine Karte: die eigene Mitarbeiterkarte (Name, Personalnummer, Beleg-Name, Geschäfts-E-Mail, Ein- und Austritt, privat: Adresse, Telefon, E-Mail, Geburtsdatum). Personen ohne Karte (nur in BC) sehen einen Hinweis. Änderungen **beantragt** man (Straße, PLZ, Ort, Telefon, E-Mail und eine Mitteilung für alles andere), man ändert nichts selbst; nur geänderte Felder gehen raus, ein offener Antrag ist möglich (zurückziehen geht), Antworten von HR stehen mit Grund in der Liste.

Team-Abwesenheiten: Monatsraster je Filiale (Personen in Zeilen, Tage in Spalten, mit Buchstaben und Farben für Krank, Urlaub, KU, Sonstige Abwesenheit und Nicht verfügbar; es wird nur gezeigt, was der Dienst für die Person freigibt). Filialen nach dem Recht der Person, Tab "Meine" mit der eigenen Zeitleiste. Mit `unavailability:create`/`delete` (HR) lassen sich Einträge anlegen und löschen. Grenzen: Die Personenauswahl enthält nur Personen, die in dem Monat einen Eintrag haben (`TODO(Backend)`: Personenliste je Filiale); die eigene Zeitleiste hat keinen Zeitraum-Filter.

FAQ: alle sehen die Fragen mit Suche; Antworten sind Markdown, sicher ohne `innerHTML` dargestellt (nur http(s)/mailto-Links und eigene Bilder). Mit `faq:create/update/delete` neu, bearbeiten, archivieren, Reihenfolge ändern (Pfeile, nicht während einer Suche), löschen nach Rückfrage; mit `faq:read` Archiv (wiederherstellen) und Bilder (PNG, JPEG, WebP bis 5 MB per Ablagefläche, "In Antwort einfügen").

Statistik: die vier Auswertungen der Meldungen (pro Monat, pro Monat und Kategorie, pro Filiale und Schwere, pro Personalnummer) mit Von/Bis-Monat als Tabellen mit Balken.

Mitarbeitende (Tabs nach Recht): "Änderungsanträge" (`employee:decide-change`: genehmigen mit einem Klick, ablehnen mit Rückfrage und optionalem Grund), "Neue Person anlegen" (`employee:create`: alle Felder der Karte, Filialen und Auswahllisten aus den Diensten, Lohnart nur mit `employee:read-wage-type`; danach die vergebene Personalnummer) und "Karte ansehen" (`employee:read`, nur über die Mitarbeiter-ID). `TODO(Backend)`: Es gibt keine Personensuche oder -liste.

Personal-Einstellungen (Abschnitte nach Recht, jeder mit eigenen Speichern-Knöpfen und Erklärungen): Urlaub (Verfall der Zusatztage, Wartezeit, Rundung), Arbeitszeitkonto (Auszahlungsschwelle, Verfall, Minusstunden, Monatsschwelle je Arbeitsverhältnis), Krankmeldung und AU (Löschfrist, Erinnerungen, Zugriffsprotokoll), Mitarbeiterkarte (Nummernkreis mit Hinweis zur Prüfung vor dem Go-live, Auswahllisten), Nicht-Verfügbarkeitsarten und Feiertage (Filialen, die an Feiertagen geöffnet sind, mehrere auf einmal).

Rollen und Rechte (Abschnitte nach Recht): Matrix (alle Rollen als Spalten, die Rechte nach Bereich als Zeilen, davor "Alles" und je Bereich "Alles in bereich"; ein Klick ändert nur den Entwurf, "Speichern" fragt mit der Liste der Änderungen nach und speichert je geänderter Rolle; Rechte, die eine Rolle nur über ein Sammelrecht hat, sind abgeblendet und dort nicht einzeln änderbar; der Administrator ist fest, ohne `access:role:update` nur Ansicht), Rollen (Rechte aus dem Katalog mit Beschreibung wählen, Platzhalter `*` und `bereich:*`, eingebaute Rollen geschützt), Rechte-Katalog (eigene Rechte anlegen und löschen), Filial-Gruppen und "Personen" (Zuweisungen, Zuständigkeiten, Hauptfiliale je Personen-ID). Grenzen: keine Personensuche (die ID wird eingegeben), die Liste der Zuständigkeiten liefert keine IDs zum Löschen (`TODO(Backend)`).
