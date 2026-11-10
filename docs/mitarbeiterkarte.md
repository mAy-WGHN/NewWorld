# Mitarbeiterkarte, Nicht-Verfügbarkeit, Arbeitszeitkonto

Entwurf von 2026-10-27/28, gekürzt 2026-11-10. Gebaut sind die Schritte 1 bis 4; Entscheidungen 29 bis 32, 34, 35, 37 und 38 in `CLAUDE.md` sind die verbindlichen Fassungen. Code in `Workforce.Core` (`Employees/`, `Unavailability/`, `WorkingTime/`).

**Ziel:** Neue Mitarbeitende werden im neuen Tool mit **einer** gesammelten Karte angelegt statt in zwei BC-Listen (Mitarbeiterliste der Personalplanung, Kassiererübersicht). Die Karte führt jede Information einmal; „Vollständiger Name“ und „Suchbegriff“ werden abgeleitet, die Kassierer-ID ist die Personalnummer.

## Besitz und Datenschutz

- **Workforce besitzt die Karte** für neu angelegte Personen. Bestehende Personen bleiben BC-Kopien; bis BC eine neue Person kennt, ist die Karte die Quelle (Zustand `PendingBc`).
- **Klassen:** Name, Personalnummer, Filiale, Funktion, Arbeitsverhältnis, Ein- und Austritt: Klasse 1. Adresse, private Telefonnummer und E-Mail, Geburtsdatum, Geschlecht: Klasse 2 (nie auf den Bus). **Lohnart:** vorläufig Klasse 2, nur für HR, Admin und Geschäftsführung; die Einordnung steht beim Datenschutzbeauftragten aus.
- **Lesen mit Stufen:** `employee:read` (ohne Privates), `employee:read-private`, `employee:read-wage-type`; die Person sieht ihr Privates über `/api/me/employee-card`, nie die Lohnart. Anlegen und Ändern mit `employee:create`/`update` (mit Filial-Scope).
- Das **Kennwort** wird nie gespeichert, die Karte führt nur das Flag „Kennwort ändern“. **Kassenprofile** gehören nicht auf die Karte.

## Felder

Identität: Personalnummer (vom Nummernkreis, danach unveränderlich), Vorname, Nachname, Name auf dem Bon (Standard Nachname), Geschlecht, Geburtsdatum, Status (abgeleitet aus Ein- und Austritt). Kontakt: Straße, PLZ, Ort, private Telefonnummer und E-Mail, geschäftliche E-Mail. Beschäftigung: Hauptfiliale, Region, Arbeitsort, Funktion, Rolle (nicht die Rolle in Access), Arbeitsverhältnis, Beschäftigungsverhältniscode, Berechtigungsgruppe, Lohnart, Anstellungs- und Austrittsdatum (das Anstellungsdatum steuert den anteiligen Urlaub), Vorgesetzter (per Personalnummer; Zuständigkeiten bleiben in Access), Bemerkung (Freitext, **keine** Gesundheitsangaben). Zeiterfassung und Anmeldung: Web-Zeiterfassung erlaubt (Standard ja), QTrado-Export, „Kennwort ändern“.

Funktion, Rolle, Region, Arbeitsort, Beschäftigungsverhältniscode, Arbeitsverhältnis, Berechtigungsgruppe und Lohnart sind **Auswahllisten**, die HR pflegt (`/api/employee-lists/{kind}`, Einträge nur deaktivieren).

## Personalnummer

Nummernkreis von **1001 bis 99999** (pflegbar, `PUT /api/personnel-number-series`), HR trägt die **zuletzt vergebene** Nummer von Hand ein (Stand 2026-10-27: 15301); ohne sie wird niemand angelegt. Vergabe atomar und nie wiederverwendet (Lücken erlaubt); am Ende des Bereichs `409 NumberSeriesExhausted`. Meldet BC eine höhere Nummer im Bereich, hebt das Tool die letzte Nummer an. Duplikate gegen alle bekannten Personen werden abgelehnt. **Vor dem Go-live prüfen**, ob 15301 noch die aktuellste Nummer ist.

## Änderungsanträge (Schritt 2)

Die Person ändert nichts selbst, sie beantragt (Adresse, Telefon, E-Mail, Mitteilung; ein offener Antrag je Person). HR entscheidet mit Grund (`employee:decide-change`); niemand entscheidet den eigenen Antrag. Die Benachrichtigungen enthalten keine Daten. Zuständig ist, wer für das Thema `employee.change-request` eingetragen ist; ohne Zuständigen wird der Antrag mit `409` abgelehnt.

## Nicht-Verfügbarkeit (Schritt 3)

Die Liste „Nichtverfügbarkeitsarten“ hat **24 Einträge** aus BC (Code, Beschreibung, bezahlte Zeit, Zeitplan-Zeichen), z. B. KRANK (K), URLAUB (U), KRANK I URLAUB (KU), ELTERNZEIT, MUTTERSCHUTZ, SONDERURLAUB, FORTBILDUNG; HR pflegt sie (nur deaktivieren). KRANK, URLAUB und KU sind Systemarten und kommen aus den Abwesenheiten. Einträge trägt nur HR ein (`unavailability:create`/`delete`): ganze Tage, nicht vor Eintritt und nicht nach Austritt, **ohne Überschneidung**; ein Urlaubsantrag über einen Eintrag wird abgelehnt, eine Krankmeldung gewinnt.

Sichtbarkeit: die Person selbst und `absence:read-reason` sehen die Art; `absence:read-availability` sieht Krank, Urlaub und KU, jede andere Art nur als „nicht verfügbar“ mit zusammengeführten Zeiträumen. Hat jemand eine Schicht an einem solchen Tag, wird sie als nicht abgedeckt gemeldet (`ShiftUncovered`) und die HR-Gruppe (Zuständigkeit `schedule.adjustment`) benachrichtigt; die Person selbst nie. Einträge, die nach einem später gemeldeten Austritt reichen, werden nur markiert (`afterExit`).

## Arbeitszeitkonto (Schritt 4)

Ein Buchungskonto je Person; der Saldo wird aus den Buchungen berechnet (Plus bildet Töpfe mit Erfassungsdatum, Minus verbraucht den ältesten Topf zuerst). Alle Regeln pflegt HR **einmal für alle** (`workingtime:manage-rules`):

- **Überstunden:** Das Arbeitsverhältnis legt eine **Monatsschwelle** fest; ab ihr werden die weiteren Stunden 1:1 als Überstunden gebucht (Schwelle 160 h, gearbeitet 172 h → 12 Überstunden).
- **Minusstunden:** Unter der Schwelle bucht der Monatsabschluss nur, wenn HR `bookMinusHours` einschaltet (Standard aus); `maxMinusHours` begrenzt den Fehlbetrag, der Rest steht als `MinusHoursNotBooked`.
- **Auszahlungsschwelle** (Standard 0, also immer) und **Verfall** (ab dem Tag nach Erfassung plus Monate; Standard: nichts verfällt).
- **Buchungen** (`workingtime:book`): Anfangsbestand, Monatsabschluss, Freizeitausgleich, Auszahlung, Auszahlung bei Austritt (ganzes Guthaben, HR-Aktion, Erinnerung am Tag nach dem Austritt über das Thema `workingtime.payout`), Korrekturen. Lesen: die Person (`workingtime:read-own`) und `workingtime:read`.
- Die Stunden kommen von Hand, bis es eine Zeiterfassung gibt.

## Schritte und Offenes

| Schritt | Inhalt | Stand |
| --- | --- | --- |
| 1 | Karte, Nummernkreis, Auswahllisten, Datenschutzstufen | gebaut (Entscheidung 29) |
| 2 | Änderungsanträge | gebaut (30) |
| 3 | Nicht-Verfügbarkeit | gebaut (32) |
| 4 | Arbeitszeitkonto | gebaut (34, 35) |
| 5 | Übertragung neuer Personen nach BC (neue Nachricht nur nach Freigabe, Start-PIN ohne Speichern im Klartext, `PendingBc` → `Synced`) | braucht Freigabe und BC-Modell |

Offen: BC-Feldliste der Karte und Spiegelung von Nicht-Verfügbarkeit und Arbeitszeitkonto (`TODO(BC-Modell)`), Dienstplan als Quelle für `IShiftLookup`, Teiltage (Uhrzeiten), Bedeutung des Felds „Rolle“, ob der Vorgesetzte der Karte etwas steuern soll, Klasse der Lohnart (Datenschutzbeauftragter), Einrichtung der Rechte und Zuständigkeiten in Access (`docs/rollen.json` deckt die Rechte ab).
