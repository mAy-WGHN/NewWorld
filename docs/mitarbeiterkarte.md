# Mitarbeiterkarte (Entwurf)

Stand 2026-10-27. **Entscheidungen vom Projektverantwortlichen sind eingearbeitet (siehe „Entschieden“); gebaut wird in Schritten (unten).** Ziel: neue Mitarbeitende werden künftig im neuen Tool angelegt, mit **einer** gesammelten Mitarbeiterkarte statt zwei getrennter Listen. Danach müssen die Daten nach BC fließen; das wird jetzt nur mitgedacht, nicht gebaut.

## Quellen

| Liste in BC | Spalten (aus der Tabellendefinition der Exportdatei) |
| --- | --- |
| Mitarbeiterliste (Personalplanung) | Nr., Vollständiger Name, Vorname, Nachname, Funktion, Geschäfts Email, Suchbegriff, Bemerkung, Beschäftigungsverhältniscode, Arbeitsort, Rolle, Region |
| Kassiererübersicht | ID, Nachname, Vorname, Name auf Bon, Arbeitsverhältnis, Filialnr., Kasse Schnittstellenprofil, Kasse Menüprofil, POS Stilprofil, Manager Benutzerrechte, Berechtigungsgruppe |

Beide führen teilweise dasselbe (Nr. = ID, Vorname, Nachname); die Karte führt jede Information **einmal**. Die Spalten „Vollständiger Name“ und „Suchbegriff“ werden abgeleitet, nicht erfasst. Retail-Kassierer-ID = Personalnummer (kein zweites Feld).

## Felder der Karte

Abschnitte so, wie HR sie ausfüllt. „Q“ = Quelle: **P** Personalplanung, **K** Kassiererübersicht, **D** deine Aufzählung (nicht in den beiden Listen belegt, Herkunft in BC offen), **N** neu im Tool.

| Abschnitt | Feld | Q | Anmerkung |
| --- | --- | --- | --- |
| Identität | Personalnummer | P, K | vergibt das Tool aus dem Nummernkreis (unten), danach unveränderlich |
| | Vorname, Nachname | P, K | |
| | Name auf dem Bon | K | Standard: Nachname |
| | Geschlecht, Geburtsdatum | D | |
| | Status | D | aktiv / ausgetreten, abgeleitet aus Eintritt und Austritt, nicht frei wählbar (Annahme) |
| Kontakt | Straße, PLZ, Ort | D | |
| | private Telefonnummer, private E-Mail | D | |
| | geschäftliche E-Mail | P | |
| Beschäftigung | Hauptfiliale | K (Filialnr.) | Vorschlag aus BC-Vorgabefiliale gilt nicht mehr: neue Personen bekommen sie hier (Entscheidung 21) |
| | Region | P | aus der Filiale abgeleitet (Annahme, zu bestätigen) |
| | Arbeitsort | P | |
| | Funktion im Unternehmen | P | frei gepflegt (Entscheidung 22) |
| | Rolle | P | Bedeutung zu klären (nicht die Rolle in Access) |
| | Arbeitsverhältnis | K | |
| | Beschäftigungsverhältniscode | P | |
| | Lohnart | D | siehe Datenschutz |
| | Anstellungsdatum, Austrittsdatum | D | Anstellungsdatum steuert den anteiligen Urlaub |
| | Vorgesetzter | D | per Personalnummer; die Zuständigkeiten für Genehmigungen bleiben in Access (Entscheidung 13) |
| | Bemerkung | P | Freitext, **keine** Gesundheitsangaben (Datenschutz-Regel) |
| Beschäftigung | Berechtigungsgruppe | K, D | Auswahlliste |
| Zeiterfassung / Export | Web-Zeiterfassung erlaubt | D | Standard **ja** (das HR-Tool braucht das Flag für die Stempelung) |
| | QTrado-Export | D | Flag |
| Anmeldung | „Kennwort ändern“ | D | nur das Flag (Standard ja); das Kennwort selbst wird nie gespeichert (Entschieden 2) |

## Personalnummer und Nummernkreis

Ja, den bestehenden Nummernkreis weiterführen. Vorschlag:
- Im Tool liegt ein **Nummernkreis** (Bereich 1001 bis 99999, pflegbar) mit der **zuletzt vergebenen Nummer**, den HR von Hand pflegen kann (kein Import aus BC). Am Ende des Bereichs wird niemand mehr angelegt, bis HR den Bereich erweitert.
- Neue Nummer = letzte + 1, **atomar** (zwei gleichzeitige Anlagen bekommen nie dieselbe), nie wiederverwendet, Lücken sind erlaubt (angelegt, dann verworfen).
- Meldet BC eine höhere Nummer (jemand wurde direkt in BC angelegt), hebt das Tool die „letzte Nummer“ automatisch an, damit nichts kollidiert; HR kann sie weiterhin von Hand setzen.
- Die Nummer wird gegen alle bekannten Personen geprüft (Kopien aus BC); ein Duplikat wird abgelehnt.
- Format (nur Ziffern, Länge) ist zu bestätigen; die Testdaten verwenden fünfstellige Nummern wie 10042.

## Konten

Jede Person bekommt beim Anlegen drei Konten:

| Konto | Stand heute | Vorschlag |
| --- | --- | --- |
| **Urlaubskonto** | gebaut (Jahresanspruch, Zusatztage, Änderungen, Regeln durch HR, `docs/urlaubsberechnung.md`) | mit der Karte automatisch vorhanden; beim Anlegen Jahresanspruch erfassen |
| **Nicht-Verfügbarkeitskonto** | nicht gebaut | Was zählt es? Je Nichtverfügbarkeitsart Tage/Stunden? Die Arten stehen in der Liste „Nichtverfügbarkeitsarten“; Abgrenzung zu Krank und Urlaub offen |
| **Überstundenkonto** | nicht gebaut | Saldo in Stunden mit **Anfangsbestand** (HR) und Buchungen; die Zeiterfassung selbst ist laut Plan nicht Teil dieses Vorhabens, also zunächst nur Konto und manuelle Buchung |

## Besitz, Datenschutz, Fluss

- **Besitzer der Karte: Workforce** (eigener Bereich `Employees/`). Workforce hält schon Kopien der Personen und führt die Konten; die HR API des Plans ist nicht Teil dieses Vorhabens. Das ändert die bisherige Regel „BC ist alleiniger Master für Stammdaten“ für **neu angelegte** Personen: bis BC sie kennt, ist die Karte im Tool die Quelle. Bestehende Personen bleiben BC-Kopien. **Braucht deine Bestätigung.**
- **Klassen:** Name, Personalnummer, Filiale, Funktion, Arbeitsverhältnis, Eintritt/Austritt: Klasse 1 (wie `EmployeeChanged`). Privatadresse, private Telefon/E-Mail, Geburtsdatum, Geschlecht: Klasse 2 (nie auf dem Bus, nur autorisierter Abruf). **Lohnart**: Klasse 2 oder 3? (Der Plan führt das Lohnarten-Mapping in der HR API.) Einordnung mit dem Datenschutzbeauftragten, bis dahin Klasse 2 und nur für HR sichtbar.
- **Rechte:** Anlegen und Ändern nur mit eigenen Rechten (z. B. `employee:create`, `employee:update`, `employee:read-private`), je mit Beschreibung und Filial-Scope, Vergabe später in Access.
- **Fluss nach BC (später):** Karte angelegt → Zustand `PendingBc` → Befehl an BC Integration (neue Nachricht, **braucht Freigabe**, Queue nur von BC Integration lesbar) → BC legt die Person mit der vergebenen Nummer an → `BcSyncSucceeded`/`BcSyncFailed` → danach fließt `EmployeeChanged` wie bisher. Voraussetzung in BC: Nummer von außen vorgebbar, Feldliste für die Anlage (`TODO(BC-Modell)`).

## Entschieden (2026-10-27)

1. **Workforce besitzt die Karte** (eigener Bereich `Employees/`). Bestehende Personen bleiben BC-Kopien, neu angelegte sind bis zur Übernahme durch BC im Tool die Quelle.
2. **Passwort:** Es wird nirgends gespeichert, auch nicht verschlüsselt. In BC ist das Kennwort ein reines Überschreiben-Feld; das Tool verhält sich gleich: die Karte führt nur das Flag „Kennwort ändern“ (Standard ja). Wie die Start-PIN beim Übertragen nach BC gesetzt wird, ohne dass sie je in der Datenbank oder im Outbox-Eintrag steht, wird beim Bau des BC-Flusses entschieden (zum Beispiel erzeugt BC Integration sie bei der Anlage und zeigt sie HR einmalig).
3. **Kassenprofile** (Schnittstellen-, Menü-, Stilprofil, Manager-Benutzerrechte) kommen **nicht** auf die Karte: bei Benutzern sind sie nicht gepflegt. Später additiv möglich.
4. **Auswahlfelder:** Funktion, Rolle, Region, Arbeitsort, Beschäftigungsverhältniscode, Arbeitsverhältnis, Berechtigungsgruppe und Lohnart sind **Auswahllisten**, die HR pflegt (Einträge nur deaktivieren, nicht löschen). In BC sind einige davon heute Freitext.
5. **Sichtbarkeit:** Die **Lohnart** sehen nur HR, Admin und Geschäftsführung (eigenes Recht). **Private Daten** (Adresse, Telefon, private E-Mail, Geburtsdatum, Geschlecht) sehen dieselben **und die Person selbst**; die Person kann Änderungen **beantragen**, HR wird benachrichtigt und entscheidet (Schritt 2).
6. **Nicht-Verfügbarkeit** ist die Liste, an der die Dienstpläne hängen (siehe unten), **Urlaubskonto** nur der Anspruch und sein Verbrauch.
7. Das **Überstundenkonto** heißt **Arbeitszeitkonto** (siehe unten).

## Nicht-Verfügbarkeit

Die Liste „Nichtverfügbarkeitsarten“ hat 25 Einträge mit den Spalten **Code, Beschreibung, Bezahlte Zeit (0/1), Zeitplan Zeichen, Arbeitscode setzen** (leer): zum Beispiel KRANK (K), URLAUB (U), KRANK I URLAUB (KU), ELTERNZEIT (EZ), FREI (FT), MUTTERSCHUTZ (MS, bezahlt), UB (unwiderruflich bezahlte Freistellung, bezahlt), UNENTSCHULDIGT (UE), UNBEZ. U (UBE), SONDERURLAUB (SU), FORTBILDUNG (FB), PRÜFUNG (P, bezahlt) und weitere. Sie sind die **Ursachen, weshalb jemand nicht zur Arbeit erscheint**.

Sie beeinflussen den Dienstplan (Dienstplanung selbst ist noch nicht gebaut, die Schnittstelle ist vorbereitet):
- Hat eine Person für einen Tag eine Nicht-Verfügbarkeit **und** dort eine Schicht, wird die Schicht als **nicht abgedeckt** markiert (`ShiftUncovered`, im Katalog) und HR benachrichtigt.
- Nach dem Tag steht im Dienstplan **nicht „leer“, sondern das Zeitplan-Zeichen** („war krank“: K), weil die Lohnabrechnung es braucht. Die Art trägt dafür „bezahlte Zeit“.
- Die bestehenden Abwesenheiten (Krankmeldung, Urlaub) werden zu Nicht-Verfügbarkeiten dieser Arten; die Arten selbst werden als von HR pflegbare Liste geführt (Start: die 25 Einträge der Datei).

## Arbeitszeitkonto

Statt „Überstundenkonto“. Ein Konto je Person mit diesen Bestandteilen:

| Bestandteil | Bedeutung |
| --- | --- |
| Sollarbeitszeit | vertraglich vereinbarte Stunden, z. B. 40 h/Woche |
| Ist-Arbeitszeit | tatsächlich geleistete Stunden |
| Überstunden | Stunden oberhalb der Sollarbeitszeit |
| Minusstunden | Stunden unterhalb der Sollarbeitszeit |
| Zeitguthaben | aktueller Saldo aus Plus- und Minusstunden |
| Freizeitausgleich | Abbau durch freie Stunden oder Tage |
| Auszahlung | vergütete Überstunden, vom Guthaben abgezogen |
| Korrekturbuchungen | nachträgliche Änderungen, z. B. bei fehlerhafter Zeiterfassung |
| Übertrag | Saldo aus dem vorherigen Abrechnungszeitraum |

Regeln, alle von HR pflegbar:
- **Auszahlungsschwelle:** Ab diesem Guthaben können Überstunden ausbezahlt werden. Bei **Austritt** wird das Restguthaben **immer** ausbezahlt, auch unter der Schwelle.
- **Verfall:** Überstunden tragen das **Datum ihrer Erfassung**, damit sie verfallen können. Die Frist ist ein pflegbarer Schwellwert, **Standard: nichts verfällt**. Verbrauch wie bei Zusatzurlaub: ältere zuerst.
- Ist- und Sollzeit kommen später aus der Zeiterfassung (nicht Teil dieses Vorhabens); bis dahin gibt es das Konto mit **Anfangsbestand** und manuellen Buchungen.

## Schritte

| Schritt | Inhalt | Stand |
| --- | --- | --- |
| 1 | Karte: Felder, Anlegen mit Personalnummer aus dem Nummernkreis, Auswahllisten, Lesen mit Datenschutz-Stufen, Urlaubsanspruch beim Anlegen | **gebaut** (Entscheidung 29) |
| 2 | Änderungsanträge der Person für private Daten, HR-Benachrichtigung und Entscheidung | geplant |
| 3 | Nicht-Verfügbarkeit: pflegbare Arten (Start: 25 aus der Liste), Einträge je Person, Anbindung der Abwesenheiten, Hinweis „nicht abgedeckt“ | geplant |
| 4 | Arbeitszeitkonto mit Schwellen für Auszahlung und Verfall | geplant |
| 5 | Übertragung nach BC (neue Nachricht, Start-PIN, Feldliste) | braucht Freigabe und BC-Modell |

## Offene Fragen

1. ~~Format und Startwert des Nummernkreises~~ **geklärt:** Bereich 1001 bis 99999, nur Ziffern ohne Auffüllen, zuletzt vergeben 15301 (Stand 2026-10-27). **Vor dem Go-live prüfen**, ob 15301 noch die aktuellste Nummer ist; HR trägt sie im Tool ein, ohne sie lässt sich niemand anlegen.
2. **Vorgesetzter:** Die Karte speichert ihn über die Personalnummer. Die Zuständigkeit für Genehmigungen bleibt in Access; soll der Vorgesetzte der Karte zusätzlich etwas steuern?
3. **Rolle** (Mitarbeiterliste): Was bedeutet sie fachlich? (Nicht die Rolle in Access.)
4. **Lohnart:** Klasse 2 oder 3 (mit dem Datenschutzbeauftragten).
5. **Arbeitszeitkonto:** Gilt der Verfall und die Auszahlungsschwelle für alle gleich, oder je Arbeitsverhältnis?
