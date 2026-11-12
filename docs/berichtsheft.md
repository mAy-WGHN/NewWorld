# Berichtsheft (Wochenbericht der Filialleitung) – Konzept

Konzept vom 2026-11-11, **freigegeben am 2026-11-11 (Entscheidung 49 in `CLAUDE.md`), noch kein Code**. Neues Thema im Portal (`src/Portal`), ohne BC und ohne Bestandsdaten. Im Plan steht „Berichtshefte“ bereits als Besitz des Portal API.

## Ziel

Die Bezirksleitung soll besser über Lage, Sorgen und Probleme der Filialen informiert sein. Die Filialleitung füllt einmal pro Woche ein digitales Formular aus, die Regionalleitung nimmt sich der Probleme an, holt bei Bedarf die Bezirksleitung dazu und kann an fehlende Berichte erinnern.

Kürzel: **FL** Filialleitung, **RL** Regionalleitung, **BL** Bezirksleitung.

## Entschieden (Gespräch 2026-11-11)

| Thema | Entscheidung |
| --- | --- |
| Hierarchie | Wird **im Berichtsheft selbst** gepflegt (Bezirk → Region → Filialen), nicht in Access. Kein neuer Konsument von `ResponsibilitiesChanged`. Vorbild: Empfänger der Meldungen (Entscheidung 41). |
| Aufbau des Berichts | **Abschnitte** mit Einzelauswahl, Mehrfachauswahl oder Freitext. Die Abschnitte **pflegen die BLs**. |
| Kommentare | **Zwei Arten:** *Antwort* (für die Ebene darunter sichtbar) und *interner Kommentar* (nur für Leitung). |
| Nachbereitung | Die FL darf einen abgegebenen Bericht **nachbereiten** (ändern und ergänzen). Die RL sieht **ausdrücklich**, dass die FL nachbereitet hat (Stand, Vermerk und Benachrichtigung, siehe unten). |
| Stand | Vier Stände: *Neu*, *In Bearbeitung* (RL), *Nachbereitet* (FL), *Erledigt* (RL). Die BL filtert nach „offen“. |
| Mehrere FL je Filiale | Ein Bericht je Filiale, jede FL mit `logbook:write` für die Filiale darf ihn bearbeiten. |
| Antwort der BL | Die FL sieht sie **nicht** direkt, die RL leitet weiter. |
| Filiale ohne Region | Der Bericht geht direkt an die BL des Bezirks; ohne Bezirk ist die Abgabe nicht möglich (`409`). |
| Pflege der Erinnerung | Zeiten und Ein/Aus: BL (`logbook:manage-reminders`); Text: Personalabteilung über die vorhandene Textpflege (`template:manage`). Startwerte: Freitag 18:00 fällig, zwei Erinnerungen (Fälligkeitstag, Montag 09:00). |
| Erinnern | RL erinnert **manuell** per Knopf, **zusätzlich automatisch** (Hintergrunddienst). Die automatische Erinnerung ist **pflegbar**: ein/aus, wann, wie oft und der Text (siehe unten). |

## Wer sieht was

| | Berichte | Antworten | Interne Kommentare |
| --- | --- | --- | --- |
| **FL** | nur die der eigenen Filiale(n) | die der RL zu den eigenen Berichten | **keine** |
| **RL** | alle Filialen der eigenen Region | die eigenen; die der BL zu Berichten der Region | **nur die eigenen** |
| **BL** | alle Filialen des eigenen Bezirks (also alle Regionen darin) | alle | **alle** |

Im Bericht stehen rechts zwei Felder: **Kommentar der RL** und **Kommentar der BL** (die jeweils letzte Antwort, der Verlauf darunter).

Antwortweg: Die Antwort der BL geht an die RL der Region, die Antwort der RL geht an die FL der Filiale. Die FL sieht die Antworten der BL **nicht** direkt (siehe offene Punkte).

## Nachbereitung und Stand

Nach der Abgabe bleibt der Bericht für die FL **änderbar**; jede Änderung nach der Abgabe setzt `revised_at` und steht im Änderungsprotokoll. Die RL sieht das so:

1. **Neu:** Der Bericht ist abgegeben, die RL hat noch nichts getan. Benachrichtigung an die RL.
2. **In Bearbeitung:** Die RL hat geantwortet oder den Stand gesetzt.
3. **Nachbereitet:** Die FL setzt per Knopf „Nachbereitet“, **mit kurzem Vermerk** („was wurde getan“, Freitext, sichtbar für die RL). Der Bericht erscheint bei der RL als **„Von der Filialleitung nachbereitet“** mit Datum und Vermerk und sortiert nach oben; Benachrichtigung an die RL (`portal.logbook-followed-up.regional`, ohne Inhalt).
4. **Erledigt:** Die RL schließt den Bericht ab. Antwortet die RL danach erneut oder setzt sie den Stand zurück, ist er wieder *In Bearbeitung*.

Zusätzlich zeigt die RL-Ansicht ein Kennzeichen **„seit meiner Antwort geändert“**, wenn die FL den Bericht nach der letzten Antwort der RL verändert hat, auch ohne dass sie „Nachbereitet“ gesetzt hat. Die BL sieht Stand und Kennzeichen ebenfalls.

## Datenmodell (Schema `portal`, neue Tabellen)

| Tabelle | Inhalt |
| --- | --- |
| `logbook_districts` | Bezirk: Name |
| `logbook_district_leaders` | Bezirk → Person (BL), mehrere möglich |
| `logbook_regions` | Region: Name, gehört zu einem Bezirk |
| `logbook_region_leaders` | Region → Person (RL), mehrere möglich |
| `logbook_store_regions` | Filiale → genau eine Region |
| `logbook_templates`, `logbook_sections` | Vorlage in **Versionen**: Abschnitt mit Position, Titel, Art (`SingleChoice`, `MultiChoice`, `FreeText`), Pflicht ja/nein, Auswahlmöglichkeiten. Eine Änderung erzeugt eine neue Version; Berichte behalten die Version, mit der sie ausgefüllt wurden. |
| `logbook_reports` | Filiale, Woche (Montag als `DateOnly`), Vorlagenversion, Phase (`Draft`, `Submitted`), **Stand** (`New`, `InProgress`, `FollowedUp`, `Done`), abgegeben von/am, **zuletzt geändert von/am** (`revised_at`, nur nach der Abgabe gesetzt), **nachbereitet von/am mit Vermerk**, Antworten (jsonb). **Ein Bericht je Filiale und Woche.** |
| `logbook_comments` | Bericht, Art (`Reply`, `Internal`), Ebene des Autors (`Region`, `District`), Autor, Text, Zeit |
| `logbook_escalations` | Bericht, eskaliert von/am, optionale Notiz |
| `logbook_reminders` | Filiale, Woche, gesendet am, Art (`Manual`, `Automatic`), von wem |

Personen und Filialen bleiben Kopien (Namen aus `EmployeeChanged`, Filialen aus `StoreChanged`). Das Portal führt keine Benutzer.

## Rechte (neu, melden sich mit Beschreibung bei Access an)

Die Rechte regeln das **Was**, die Hierarchie im Berichtsheft das **Wo** für RL und BL. Für die FL kommt das Wo aus der Reichweite in Access.

| Recht | Bedeutung | Reichweite |
| --- | --- | --- |
| `logbook:write` | Wochenbericht der eigenen Filiale ausfüllen und abgeben | Filialen (Access) |
| `logbook:review` | Berichte der eigenen Hierarchie lesen, antworten, intern kommentieren, erinnern, eskalieren (RL) | aus der Hierarchie |
| `logbook:manage-template` | Vorlage und Abschnitte pflegen | uneingeschränkt |
| `logbook:manage-structure` | Bezirke, Regionen, Leitungen, Filialzuordnung pflegen | uneingeschränkt |
| `logbook:manage-reminders` | Einstellungen der automatischen Erinnerung pflegen (ein/aus, Fälligkeit, Zeitpunkte, Höchstzahl); der Text über `template:manage` in Notifications | uneingeschränkt |

Ob jemand **RL oder BL** ist, ergibt sich aus der Hierarchie, nicht aus dem Recht. Eine Person, die nirgends als Leitung eingetragen ist, sieht keine Berichte, auch mit `logbook:review`.

## Endpunkte (Antwortform wie im Portal: `{success,data}`)

| Zweck | Pfad |
| --- | --- |
| Aktuelle Vorlage lesen | `GET /api/logbook/template` |
| Vorlage pflegen (neue Version) | `PUT /api/logbook/template` |
| Struktur lesen und pflegen | `GET/PUT /api/logbook/structure` (Bezirke, Regionen, Leitungen, Filialzuordnung) |
| Eigener Bericht der Woche | `GET /api/logbook/reports/mine?week=` |
| Entwurf speichern / abgeben | `PUT /api/logbook/reports/{storeId}/{week}`, `POST …/submit` |
| Berichte der Hierarchie (Liste, Filter Woche, Region, Filiale, Status, „überfällig“) | `GET /api/logbook/reports` |
| Einzelner Bericht mit Kommentaren (gefiltert nach Sichtbarkeit) | `GET /api/logbook/reports/{id}` |
| Antwort / interner Kommentar | `POST /api/logbook/reports/{id}/comments` |
| Als nachbereitet markieren (FL, mit Vermerk) | `POST /api/logbook/reports/{id}/follow-up` |
| Stand setzen (RL: in Bearbeitung, erledigt) | `PUT /api/logbook/reports/{id}/state` |
| An die BL eskalieren | `POST /api/logbook/reports/{id}/escalate` |
| FL erinnern (manuell) | `POST /api/logbook/reminders` (Filiale und Woche) |
| Einstellungen der automatischen Erinnerung | `GET/PUT /api/logbook/reminder-settings` |

Fremde Berichte außerhalb der Reichweite sind `404` (Existenz wird nicht verraten), wie bei der AU.

## Benachrichtigungen (nur über das vorhandene `SendNotification`, keine neue Nachricht)

Alle Texte **ohne Inhalt des Berichts**, nur InApp und generischer Push mit Link (Klasse 2 darf nicht in Push oder Mail).

| Anlass | Empfänger |
| --- | --- |
| Bericht abgegeben | RL der Region |
| Antwort der RL | FL der Filiale |
| Antwort der BL | RL der Region |
| Von der FL nachbereitet | RL der Region |
| Eskaliert | BL des Bezirks |
| Erinnerung (manuell oder automatisch) | FL der Filiale ohne abgegebenen Bericht |

Die Texte sind wie die anderen Vorlagen durch die Personalabteilung pflegbar.

## Automatische Erinnerung

Ein Hintergrunddienst (hinter der Sperre `IDistributedLock`, läuft nur auf einer Instanz) prüft stündlich, ob eine Filiale für die laufende Woche keinen abgegebenen Bericht hat, und erinnert die FL. Jede Erinnerung wird in `logbook_reminders` vermerkt, damit nichts doppelt kommt.

**Pflegbar in der Oberfläche** (nicht in der Konfiguration), Tabelle `logbook_reminder_settings`, Recht `logbook:manage-reminders`:

| Einstellung | Bedeutung | Standard (Vorschlag) |
| --- | --- | --- |
| Ein/Aus | Ob automatisch erinnert wird (die manuelle Erinnerung der RL bleibt immer möglich) | an |
| Fälligkeitstag | Bis wann der Bericht der Woche da sein soll (Wochentag und Uhrzeit, lokale Zeit) | Freitag 18:00 |
| Zeitpunkte | Wann erinnert wird, als Liste „Tag und Uhrzeit relativ zur Fälligkeit“ (z. B. Fälligkeit +0 Stunden, Montag 09:00) | zwei: am Fälligkeitstag, Montag 09:00 |
| Höchstzahl | Wie oft je Woche und Filiale höchstens erinnert wird | 2 |
| Text | Titel und Text der Erinnerung (InApp, Push ohne Berichtsinhalt) | „Bitte Wochenbericht ausfüllen“ |

Der **Text** ist eine Benachrichtigungsvorlage (`portal.logbook-reminder.store`) und wird wie die übrigen Texte gepflegt (Entscheidung 33: Standard in der Datei, Überschreiben je Kanal, Vorschau, Variablen `$USER.firstName`, `$DP.store`, `$DP.week`; Push-Text und Betreff nur Klasse 1). Die manuelle Erinnerung der RL nutzt dieselbe Vorlage. Jede Änderung der Einstellungen steht im Änderungsprotokoll. Beim Speichern wird geprüft (Fälligkeit und Zeitpunkte gültig, Höchstzahl 1 bis 10, Zeitpunkte nicht vor Wochenbeginn).

## Datenschutz

- Berichte sind **Klasse 2** (Lage, Personal, Probleme): bleiben im Portal, **nie auf dem Bus**, nie in Logs oder Push-Texten.
- Freitext kann Personen betreffen. Mit dem Datenschutzbeauftragten klären: Aufbewahrungsfrist, Hinweis im Formular („keine Diagnosen, keine Gesundheitsdaten“), Löschung bei `EmployeeDataErasureRequested` (Vorschlag: Leitungseinträge der Person entfernen, Autorenname in Berichten und Kommentaren anonymisieren, Inhalte bleiben).
- Das Änderungsprotokoll (Entscheidung 46) deckt Berichte, Kommentare, Vorlage und Struktur automatisch ab; Werte sieht nur, wer `portal-audit:read-values` hat.

## Umsetzung in Schritten (je Schritt Tests zuerst, Stopp-Punkt mit Bericht)

**Ergänzung 2026-11-12: Bewertung, Aufbewahrung, Jahresauswertung (Entscheidungen des Projektverantwortlichen).** (1) **Zustandsabfrage 1 bis 5:** Jeder Wochenbericht hat die Gesamtbewertung der Filiale von 1 (sehr schlecht) bis 5 (sehr gut); sie ist **Pflicht, kann nicht entfernt und nicht anders eingestellt werden**, nur Überschrift und Hinweis sind änderbar. Technisch ein Abschnitt der Art `rating` mit **fester Abschnitts-Id und fünf festen Antwort-Ids** (`LogbookRating`; der Wert ist die Position der gewählten Antwort); der Dienst erzwingt es beim Speichern der Vorlage (`422`: fehlt, geändert, eine zweite Bewertung; was der Client sonst für diesen Abschnitt schickt, wird ignoriert), die Abgabe verlangt sie wie jedes Pflichtfeld. Die Oberfläche zeigt sie gesperrt (nicht entfernbar, ohne Art, Pflicht und Antworten); eine alte Vorlage ohne sie bekommt sie vorn eingefügt. Der Vorschlag (Version 0) beginnt damit und ersetzt die frühere Frage „Wie war die Woche insgesamt?“. Die Id steht auch im Frontend (`RATING_SECTION_ID`), ein Test hält beide fest. (2) **Aufbewahrung ein Jahr:** Berichte von Wochen, die länger als 365 Tage (`Portal:Logbook:RetentionDays`) zurückliegen, werden samt Kommentaren und Erinnerungen **gelöscht** (stündliche Runde des Hintergrunddienstes, höchstens 500 je Runde). **Nicht im Änderungsprotokoll:** Berichte und Kommentare sind vom Audit ausgenommen, damit ihr Inhalt dort nicht länger lebt (wer was tat, steht im Bericht selbst). (3) **Jahresauswertung je Kalenderjahr** (`GET /api/logbook/evaluation?year=`): je Filiale, Region und Bezirk: abgegeben, **pünktlich** (bis Fälligkeitstag und -zeit der Einstellungen, **bei der Abgabe festgehalten**, spätere Änderungen der Fälligkeit ändern die Vergangenheit nicht), verspätet, **fehlend** (Wochen mit überschrittener Fälligkeit ohne abgegebenen Bericht), Quote der Pünktlichen (gegen „erwartet“ = abgegeben plus fehlend), **Ø Bewertung** (gewichtet nach Zahl der Bewertungen) und Verteilung 1 bis 5, eskaliert, beantwortet, erledigt. Sichtbarkeit nach Ebene: Filialleitung die eigenen Filialen, Regionalleitung die Filialen und die Region, Bezirksleitung alles samt Regionen und Bezirk; wer nichts erreicht, bekommt `403`. **Damit das Kalenderjahr auch nach der Löschung stimmt, bleiben je Filiale und Woche nur Kennzahlen** (Tabelle `logbook_week_stats`: pünktlich, Bewertung, eskaliert, beantwortet, erledigt; **kein Inhalt, kein Text, keine Person**) dauerhaft erhalten; das ist mit dem Datenschutzbeauftragten abzustimmen. Grenzen: Eine Woche gehört zum Jahr ihres Montags; gezählt werden die Filialen, wie sie **jetzt** in der Hierarchie stehen (keine Historie der Zuordnung), eine später aufgenommene Filiale hat für frühere Wochen „fehlend“. Tabs: „Auswertung“ im Bereich Berichtsheft. Migration `LogbookRatingAndRetention`.

**Stand 2026-11-12: Schritte 1 bis 6 gebaut.** Schritt 6 (Frontend, `Frontend/src/app/features/logbook`): Bereich **„Berichtsheft“** (`/logbook`, sichtbar mit `logbook:write` oder `logbook:review`) mit Tabs je Recht: **Mein Bericht** (je Filiale das Formular nach der Vorlage in der Version des Berichts: Einzelauswahl, Mehrfachauswahl, Freitext, Pflichtfelder markiert; Entwurf speichern, Abgeben speichert vorher mit, nach der Abgabe „Änderungen speichern" und „Als nachbereitet markieren" mit Pflichtvermerk; Wochenwahl und die letzten Wochen je Filiale), **Eingang** (Leitungen: Woche oder alle offenen aller Wochen, Filter nach Stand und Filiale, nachbereitete zuerst, Kennzeichen „Seit deiner Antwort geändert" und „Eskaliert") und **Fehlende Berichte** (mit „Erinnern"). **Bericht** (`/logbook/reports/:id`, auch der Link der Benachrichtigungen): Antworten, Antworten und interne Kommentare getrennt, Stand setzen, Eskalieren (Regionalleitung); was jemand sieht, entscheidet der Dienst. **Berichtsheft-Verwaltung** (`/admin/logbook`, je Recht ein Tab): Hierarchie (Bezirke, Regionen, Leitungen per Personensuche, Filialen zuordnen), Vorlage (Abschnitte und Antworten anlegen, ändern, sortieren, entfernen; bestehende Ids bleiben), Erinnerung (ein/aus, Fälligkeit, Zeitpunkte, Höchstzahl). Lokal: `docs/rollen.json` enthält die Rollen „Filialleitung" (`logbook:write`), „Regionalleitung" (`logbook:review`) und „Bezirksleitung" (`logbook:review`, `logbook:manage-template`), jeweils mit den Basisrechten von „Public User", sowie „Berichtsheft-Verwaltung" (mit `access:person:read` für die Personensuche); sie werden beim lokalen Start angelegt, die Zuweisung an Personen erfolgt in Access. Noch nicht in einem echten Browser gegen die laufende Umgebung angesehen. Frühere Schritte:

**Schritt 5 bis dahin:** Schritt 5 (Benachrichtigungen, Eskalieren, Erinnern): **Benachrichtigungen** nur über das vorhandene `SendNotification` (in derselben Transaktion wie die Änderung), InApp + generischer Push, **ohne Inhalt des Berichts**, feste Notification-Id je Ereignis (Wiederholung wird nicht doppelt gezeigt), wer handelt, wird nicht benachrichtigt: Abgabe → Regionalleitung (ohne Regionalleitung die Bezirksleitung), Antwort der Regionalleitung → Filialleitung, Antwort der Bezirksleitung → Regionalleitung, nachbereitet → Regionalleitung, eskaliert → Bezirksleitung, Erinnerung → Filialleitung; interne Kommentare benachrichtigen niemanden. Vorlagen in Notifications: `portal.logbook-submitted.regional`, `-reply.store`, `-reply.regional`, `-followed-up.regional`, `-escalated.district`, `-reminder.store` (Pflicht, nicht abschaltbar); **der Text ist über die Textpflege der Personalabteilung änderbar** (Entscheidung 33). **Filialleitung einer Filiale** = wer `logbook:write` nur für diese Filiale hält (nicht uneingeschränkt; dafür hat `IPermissionCache` jetzt `All()`). **Eskalieren** (`POST /api/logbook/reports/{id}/escalate`, optionaler Vermerk bis 1000 Zeichen): nur Regionalleitung, einmal je Bericht, nur wenn die Filiale eine Bezirksleitung hat; die Filialleitung sieht es nicht (Liste: `escalated`). **Erinnern von Hand** (`POST /api/logbook/reminders`, `{storeId, week?}`): Leitung der Filiale (sonst `404`), nur wenn der Bericht der Woche nicht abgegeben ist, höchstens **alle 12 Stunden** je Filiale und Woche (Annahme), nur wenn es eine Filialleitung gibt; `GET /api/logbook/missing?week=` zeigt je Filiale der Reichweite: Erinnerungen, letzte Erinnerung, ob es eine Filialleitung gibt, ob ein Entwurf angefangen ist. **Automatische Erinnerung:** Hintergrunddienst stündlich (Sperre `IDistributedLock`), pflegbar über `GET/PUT /api/logbook/reminder-settings` (Recht `logbook:manage-reminders`): ein/aus, Fälligkeitstag und -zeit, Zeitpunkte (Tage nach der Fälligkeit + Uhrzeit, höchstens 10), Höchstzahl je Filiale und Woche (1 bis 10, manuelle zählen mit). Startwerte: an, Freitag 18:00, Zeitpunkte Freitag 12:00 und Montag 09:00, höchstens 2. Zeiten in lokaler Zeit (Zeitumstellung geprüft); mehrere überfällige Zeitpunkte ergeben **eine** Erinnerung; beim **Einschalten** (und beim ersten Lauf ohne Einstellungen) zählen nur Zeitpunkte ab jetzt, es gibt also keinen Schwall für lange vergangene Wochen; eine Filiale ohne Filialleitung wird nicht markiert und beim nächsten Lauf erneut geprüft. Tabellen `logbook_reminders` (je Filiale, Woche und Zeitpunkt nur einmal), `logbook_reminder_settings`; Migration `LogbookReminders`. Noch offen: Oberfläche (Schritt 6), die Rechte in Access an Rollen vergeben, Datenschutzbeauftragter (Aufbewahrung, Hinweis im Formular).

**Schritt 4:** Schritt 4 (Lesen, Kommentare, Stand, Nachbereitung): Tabelle `logbook_comments` (Art `Reply`/`Internal`, **Ebene des Autors zur Schreibzeit**, Reihenfolge über eine fortlaufende Nummer) und drei Spalten am Bericht (`followed_up_at/by`, `follow_up_note`); Migration `LogbookReview`. Regeln im Core (`LogbookCommentRules`, `LogbookStateRules`, getestet für jede Rolle): Die **Filialleitung** sieht die Antworten der Regionalleitung, keine internen Kommentare und nicht direkt die Antwort der Bezirksleitung; die **Regionalleitung** sieht alle Antworten (auch die der anderen Regionalleitung der Region und die der Bezirksleitung) und nur die **eigenen** internen Kommentare; die **Bezirksleitung** sieht alles. Leitungen sehen nur **abgegebene** Berichte ihrer Filialen (Entwurf und fremde Berichte: `404`). Endpunkte: `GET /api/logbook/reports` (Leitungen; Filter `week`, `storeId`, `regionId`, `state`, `open=true`; ohne Woche die aktuelle, mit `open=true` alle Wochen; sortiert: nachbereitet, neu, in Bearbeitung, erledigt), `GET /api/logbook/reports/{id}` (Leitung oder Filialleitung der Filiale, mit den sichtbaren Kommentaren, `viewerLevel` und `revisedSinceMyReply`), `POST …/comments` (Recht `logbook:review`, Art `reply` oder `internal`, Text bis 4000 Zeichen), `PUT …/state` (nur `inProgress` oder `done`), `POST …/follow-up` (nur Filialleitung der Filiale, Vermerk Pflicht bis 1000 Zeichen, nicht bei erledigten oder noch nicht abgegebenen Berichten: `409`). Stand: **Neu** (abgegeben), **In Bearbeitung** (Antwort der Regionalleitung; jede weitere Antwort öffnet auch einen erledigten oder nachbereiteten Bericht wieder), **Nachbereitet** (nur durch die Filialleitung), **Erledigt** (Leitung, auch wieder zurück auf In Bearbeitung). Antworten der Bezirksleitung und interne Kommentare ändern den Stand nicht. **`revisedSinceMyReply`**: die Filialleitung hat den Bericht nach meiner letzten Antwort geändert (ohne eigene Antwort `false`). Noch nicht gebaut: Benachrichtigungen, Erinnern und Eskalieren (Schritt 5), Oberfläche (Schritt 6).

**Schritt 3:** Schritt 3 (Bericht ausfüllen und abgeben): Tabelle `logbook_reports` (ein Bericht je Filiale und Woche, eindeutig; Antworten als jsonb; Migration `LogbookReports`); Core `LogbookWeek` (Woche = Montag, in **lokaler Zeit** `Portal:Logbook:TimeZoneId`, Standard Europe/Berlin; getestet über Monats-, Jahres- und Zeitumstellungsgrenzen, Schalttag und 53-Wochen-Jahr). Endpunkte (Recht `logbook:write`, Reichweite je Filiale; außerhalb `403`, unbekannte Filiale `404`): `GET /api/logbook/my-reports?week=` (die Filialen der Reichweite mit dem Bericht der Woche, der Vorlagenversion und `canSubmit`), `PUT /api/logbook/reports/{storeId}/{week}` (Entwurf speichern und später ändern), `POST /api/logbook/reports/{storeId}/{week}/submit` (abgeben), `GET /api/logbook/stores/{storeId}/reports` (Verlauf, neueste zuerst). Regeln: ein beliebiger Tag der Woche meint die Woche; **nicht in der Zukunft und höchstens 8 Wochen zurück** (Annahme, `LogbookWeek.MaxWeeksBack`); der Bericht behält die **Vorlagenversion**, mit der er begonnen wurde; ein Entwurf darf unvollständig sein, die Abgabe verlangt die Pflichtabschnitte; eine Abgabe ist nur möglich, wenn jemand benachrichtigt würde (sonst `409`); zweimal abgeben ändert nichts; ein abgegebener Bericht bleibt änderbar, jede inhaltliche Änderung setzt `revisedAt` und `revisedBy` (gleiche Antworten sind keine Änderung) und muss die Pflichtabschnitte beantwortet lassen; alle mit dem Recht für die Filiale arbeiten am selben Bericht. Noch **nicht** gebaut: die Benachrichtigung der Regionalleitung bei der Abgabe (Schritt 5), Lesen und Kommentieren durch Regional- und Bezirksleitung (Schritt 4). Autorennamen stehen nicht im Bericht, nur Ids; die Namen kommen aus der Personenkopie und verschwinden bei einer Löschung (dann `(unbekannt)`).

**Schritt 2 (Vorlage):** Core `LogbookTemplate`, `LogbookTemplateValidator`, `LogbookAnswerValidator` (Antworten gegen die Vorlage: Entwurf darf unvollständig sein, die Abgabe verlangt die Pflichtabschnitte); Tabelle `logbook_templates` (eine Zeile je Version, Abschnitte als jsonb; Migration `LogbookTemplate`); Endpunkte `GET /api/logbook/template` (aktuelle Version), `GET /api/logbook/template/{version}` (alte Versionen, `0` = Vorschlag) und `PUT /api/logbook/template` (Recht `logbook:manage-template`). Lesen darf, wer eines der Rechte `logbook:write`, `logbook:review` oder `logbook:manage-template` hat. Bis zur ersten gespeicherten Version gilt ein **Vorschlag** (Version 0, nicht gespeichert, feste Ids): Gesamtlage, Personal, Belastungen, Was lief gut, Probleme, Unterstützung. Gleicher Inhalt ergibt keine neue Version; Abschnitte und Möglichkeiten behalten ihre Id, wenn nur der Text geändert wird; gleichzeitiges Speichern ergibt `409` statt eines kaputten Standes. Grenzen: 30 Abschnitte, 20 Möglichkeiten je Auswahl, Freitext bis 4000 Zeichen.

**Schritt 1 (Struktur):** Core: `Portal.Core/Logbook` (Struktur, Reichweite, Prüfregeln). Persistenz: Tabellen `logbook_districts`, `logbook_district_leaders`, `logbook_regions`, `logbook_region_leaders`, `logbook_store_assignments` (Migration `LogbookStructure`; beim Speichern ändert sich nur, was abweicht). Endpunkte: `GET/PUT /api/logbook/structure` (Recht `logbook:manage-structure`, als Ganzes gespeichert, Ids kommen vom Client) und `GET /api/logbook/me` (Recht `logbook:review`: Ebene und erreichbare Filialen). Abweichung vom Entwurf: Eine Filiale gehört **immer zu einem Bezirk** und **optional zu einer Region darin** (dadurch geht der Bericht einer Filiale ohne Region direkt an die Bezirksleitung). Die fünf Rechte sind im Katalog; Leitungen werden bei `EmployeeDataErasureRequested` entfernt. Offen für später: die Rechte in Access an Rollen vergeben, die Oberfläche (Schritt 6).

1. **Struktur und Rechte:** Tabellen, Hierarchie pflegen und lesen, Rechte im Katalog, Reichweitenlogik (wer sieht welche Filiale) als eigene, gut getestete Klasse.
2. **Vorlage:** Abschnitte, Versionen, Validierung der Antworten gegen die Vorlage.
3. **Bericht ausfüllen und abgeben:** Entwurf, Abgabe, ein Bericht je Filiale und Woche, Wochengrenzen (Jahreswechsel, 53. Woche, Zeitumstellung).
4. **Kommentare, Sichtbarkeit, Nachbereitung:** Antwort und interner Kommentar, die Matrix oben als Tests für jede Rolle; Stand, „Nachbereitet“ mit Vermerk und das Kennzeichen „seit meiner Antwort geändert“.
5. **Erinnern und Eskalieren:** manuell, automatisch (pflegbar), Benachrichtigungen.
6. ✔ **Frontend (gebaut 2026-11-12):** Bereich „Berichtsheft“ (FL: Formular und Verlauf; RL und BL: Eingang mit Filtern, Bericht mit Kommentaren rechts; Verwaltung: Struktur und Vorlage).

## Offene Punkte

Keine fachlichen Punkte mehr offen; die Entscheidungen stehen oben. Bei der Umsetzung noch zu klären:

- **Datenschutzbeauftragter:** Aufbewahrungsfrist der Berichte, Hinweis im Formular (keine Gesundheitsdaten), Anonymisierung bei Löschung der Person (Vorschlag siehe Datenschutz).
- **Einrichtung in Access** (Konfiguration, kein Code): Rollen mit `logbook:write` (Filialleitung, Reichweite Filialen) und `logbook:review`, Rechte für die Verwaltung; Hierarchie und Vorlage im Berichtsheft anlegen.
- **Abweichung von Entscheidung 13** (Entscheidung 49): Das Wo für RL und BL kommt aus der Hierarchie im Berichtsheft, nicht aus Access.
