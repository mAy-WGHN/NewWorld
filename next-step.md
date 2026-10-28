# Nächste Schritte (Mitarbeiterkarte)

Stand 2026-10-28. Gebaut sind Schritt 1 (Karte, Nummernkreis, Auswahllisten, Datenschutzstufen), Schritt 2 (Änderungsanträge) und Schritt 3 (Nicht-Verfügbarkeit). Ausstehend ist **Schritt 4**; dazu Schritt 5 als Ausblick. Entwurf und Entscheidungen: `docs/mitarbeiterkarte.md`, `CLAUDE.md` (Entscheidungen 29 bis 31).

Als Nächstes: **Schritt 4** (Arbeitszeitkonto), vorher die offene Frage zu den Minusstunden klären.

---

## Schritt 3: Nicht-Verfügbarkeit (gebaut 2026-10-28, Entscheidung 32)

Arten (24 aus BC), Einträge von HR, Zeitleiste mit KU, Sichtbarkeit nach Art/Verfügbarkeit, Schichthinweis an die HR-Gruppe. Details: `docs/mitarbeiterkarte.md`, `CLAUDE.md` (Entscheidung 32).

**Offen aus Schritt 3**
- **Benachrichtigungstexte** sind gebaut (Entscheidung 33). Offen: Oberfläche, HR die Rechte `template:manage` und `unavailability:*` in Access geben, Datenschutzbeauftragter zum Mail-Anbieter, Bezirksleiter-Vermerk, ausgeschiedene Personen überspringen, BC soll Vor- und Nachnamen getrennt liefern.
- **Einrichtung in Access:** HR-Gruppe als Zuständigkeit zum Thema `schedule.adjustment` (sechs Personen, pflegbar), Rechte `unavailability:*` für HR, Leserechte der Rollen.
- **BC-Spiegelung** der Einträge (`TODO(BC-Modell)`), **Dienstplan** als Quelle für `IShiftLookup`, **Teiltage** (Uhrzeiten) später, Einträge mit offenem Ende (bisher nicht), Einträge nach Austritt (bisher nur beim Anlegen geprüft).

---

## Schritt 4: Arbeitszeitkonto (ausstehend)

**Ziel:** Ein Konto je Person (heißt „Arbeitszeitkonto“, nicht Überstundenkonto), das Überstunden und Ausgleich führt.

**Bestandteile:** Sollarbeitszeit, Ist-Arbeitszeit, Überstunden, Minusstunden, Zeitguthaben (Saldo), Freizeitausgleich, Auszahlung, Korrekturbuchungen, Übertrag.

**Regeln (entschieden, Entscheidung 31)**
- **Auszahlungsschwelle:** von HR pflegbar, **einmal für alle**, Standard **0 Stunden** (also immer). Ein anderer Wert gilt für alle, unabhängig vom Arbeitsverhältnis.
- **Verfall:** Überstunden tragen das Datum ihrer Erfassung. Die Frist ist ein von HR pflegbarer Schwellwert, einmal für alle, Standard **nichts verfällt**. Ältere Überstunden werden zuerst verbraucht (wie bei Zusatzurlaub).
- **Austritt:** Das Restguthaben wird **immer** ausbezahlt, auch unter der Schwelle.
- **Arbeitsverhältnis:** legt **implizit fest, was Überstunden sind**. Je Arbeitsverhältnis gibt es eine **Monatsschwelle** (Stunden pro Monat, von HR gepflegt). Ab dem Punkt im Monat, an dem sie erreicht ist, werden die weiteren Stunden **1:1** gezählt und im Konto als Überstunden erfasst (kein Faktor). Beispiel: Schwelle 160 h, gearbeitet 172 h ergibt 12 Überstunden.
- Die Monatsschwellen hängen an den Einträgen der Auswahlliste „Arbeitsverhältnis“ (bereits gebaut).

**Zu bauen**
- Buchungskonto mit Einträgen (Art, Stunden, Datum, Urheber), Saldo und Anfangsbestand (manuell durch HR).
- **Monatsabschluss:** nimmt die gearbeiteten Stunden eines Monats entgegen, rechnet gegen die Monatsschwelle des Arbeitsverhältnisses und bucht Überstunden (und eventuell Minusstunden). Die Zeiterfassung selbst (Stempelungen) ist nicht Teil des Vorhabens; die Stunden kommen später von dort, bis dahin von Hand.
- Freizeitausgleich, Auszahlung (mit Schwelle), Auszahlung bei Austritt, Korrekturbuchungen, Übertrag, Verfall nach Erfassungsdatum.
- Regeln von HR pflegbar (Endpunkte wie bei `/api/vacation-rules`), Rechte mit Beschreibung, Tests zuerst (Zeit-/Datumsfälle), Migration, Doku.

**Offene Frage vor dem Bau**
- Werden Stunden **unter** der Monatsschwelle als **Minusstunden** gebucht (Schwelle = Soll), oder entstehen keine Minusstunden? Bis zur Antwort ist das eine Annahme.

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
