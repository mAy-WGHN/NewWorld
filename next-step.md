# Nächste Schritte (Mitarbeiterkarte)

Stand 2026-10-28. Gebaut sind Schritt 1 (Karte, Nummernkreis, Auswahllisten, Datenschutzstufen), Schritt 2 (Änderungsanträge) und Schritt 3 (Nicht-Verfügbarkeit). Gebaut ist auch Schritt 4 (Arbeitszeitkonto); dazu Schritt 5 als Ausblick. Entwurf und Entscheidungen: `docs/mitarbeiterkarte.md`, `CLAUDE.md` (Entscheidungen 29 bis 31).

Als Nächstes: **Schritt 5** (braucht Freigabe und BC-Modell) oder die offenen Punkte unten.

---

## Schritt 3: Nicht-Verfügbarkeit (gebaut 2026-10-28, Entscheidung 32)

Arten (24 aus BC), Einträge von HR, Zeitleiste mit KU, Sichtbarkeit nach Art/Verfügbarkeit, Schichthinweis an die HR-Gruppe. Details: `docs/mitarbeiterkarte.md`, `CLAUDE.md` (Entscheidung 32).

**Offen aus Schritt 3**
- **Benachrichtigungstexte** sind gebaut (Entscheidung 33). Offen: Oberfläche, HR die Rechte `template:manage` und `unavailability:*` in Access geben, Datenschutzbeauftragter zum Mail-Anbieter, Bezirksleiter-Vermerk, ausgeschiedene Personen überspringen, BC soll Vor- und Nachnamen getrennt liefern.
- **Einrichtung in Access:** HR-Gruppe als Zuständigkeit zum Thema `schedule.adjustment` (sechs Personen, pflegbar), Rechte `unavailability:*` für HR, Leserechte der Rollen.
- **BC-Spiegelung** der Einträge (`TODO(BC-Modell)`), **Dienstplan** als Quelle für `IShiftLookup`, **Teiltage** (Uhrzeiten) später, Einträge mit offenem Ende (bisher nicht), Einträge nach Austritt (bisher nur beim Anlegen geprüft).

---

## Schritt 4: Arbeitszeitkonto (gebaut 2026-10-28, Entscheidung 34)

Buchungskonto, Saldo aus Buchungen, Monatsabschluss gegen die Monatsschwelle des Arbeitsverhältnisses, Freizeitausgleich, Auszahlung, Auszahlung bei Austritt, Korrekturen, Verfall nach Erfassungsdatum, Regeln und Schwellen von HR pflegbar.

**Offen aus Schritt 4**
- **Minusstunden:** Werden Stunden unter der Monatsschwelle als Minusstunden gebucht (Schwelle = Soll)? Bis zur Antwort bucht der Monatsabschluss keine (`TODO(Entscheidung)` in `WorkingTimeService`).
- **Austritt:** Die Auszahlung des Restguthabens ist eine HR-Aktion; automatisch bei `EmployeeLeft` ist nicht gebaut.
- **BC-Spiegelung** des Kontos (`TODO(BC-Modell)`), **Stunden aus der Zeiterfassung** statt von Hand, Rechte `workingtime:*` in Access an HR vergeben, Monatsschwellen je Arbeitsverhältnis eintragen.

---

## Schritt 5 (Ausblick, braucht Freigabe und BC-Modell)

Übertragung neuer Mitarbeitender nach BC: neue Nachricht (nur nach Freigabe), Start-PIN ohne Speichern im Klartext, Abgleich der internen GUID mit BC, Zustand `PendingBc` zu `Synced`. Dazu BC-Feldnamen der Mitarbeiterkarte und die Entscheidung, welche Daten BC bekommt.

## Weitere offene Punkte (unabhängig von den Schritten)

- Austritt im Jahr: anteiliger Urlaubsanspruch, wartet auf die Personalabteilung (fünf Fragen in `docs/urlaubsberechnung.md`).
- **Vor dem Go-live prüfen:** Ob 15301 noch die zuletzt vergebene Personalnummer ist (Bereich 1001 bis 99999).
- Rechte und Zuständigkeiten in Access zum Schluss in der Oberfläche vergeben (u. a. Thema `employee.change-request`).
- Portal-Gerüst übernimmt die Hauptfiliale noch nicht.
- BC-Änderungserkennung per Polling (BC-Feldnamen fehlen).
- Datenschutzbeauftragter: Klasse der Lohnart, AU, Datenklassifizierung.
