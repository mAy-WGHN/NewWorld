# Nächste Schritte (Mitarbeiterkarte)

Stand 2026-10-27. Gebaut sind Schritt 1 (Karte, Nummernkreis, Auswahllisten, Datenschutzstufen) und Schritt 2 (Änderungsanträge). Ausstehend sind **Schritt 3** und **Schritt 4**; dazu Schritt 5 als Ausblick. Entwurf und Entscheidungen: `docs/mitarbeiterkarte.md`, `CLAUDE.md` (Entscheidungen 29 bis 31).

Reihenfolge: **Schritt 3 zuerst** (kleiner, Grundlage für die Dienstplanung), dann Schritt 4.

---

## Schritt 3: Nicht-Verfügbarkeit (ausstehend)

**Ziel:** Alles, weshalb eine Person nicht zur Arbeit erscheint, als eine Liste führen. Sie hängt später an den Dienstplänen und wirkt direkt auf sie.

**Zu bauen**
- **Arten** als von HR pflegbare Liste. Start sind die 25 Einträge aus `Archiv/hr-tool-API/docs/spreadsheets/Nichtverfügbarkeitsarten.xlsx` (Spalten: Code, Beschreibung, Bezahlte Zeit 0/1, Zeitplan-Zeichen, Arbeitscode setzen (leer)), zum Beispiel KRANK (K), URLAUB (U), KRANK I URLAUB (KU), ELTERNZEIT (EZ), FREI (FT), MUTTERSCHUTZ (MS, bezahlt), UB (bezahlt), UNENTSCHULDIGT (UE), UNBEZ. U (UBE), SONDERURLAUB (SU), FORTBILDUNG (FB), PRÜFUNG (P, bezahlt). Einträge nur deaktivieren, nicht löschen.
- **Einträge je Person:** Zeitraum und Art, mit Plausibilitätsprüfungen (keine unmöglichen Überschneidungen, Zeitraum vor Eintritt und nach Austritt).
- **Anbindung der bestehenden Abwesenheiten** (Entscheidung: „wie bisher“): Krankmeldung wird zu KRANK, Urlaub zu URLAUB, intern auf die Art abgebildet. Die anderen Arten werden zunächst nicht von Mitarbeitenden selbst beantragt, sondern von HR erfasst.
- **Dienstplan-Wirkung, soweit ohne Dienstplan möglich:**
  - Hat die Person an dem Tag eine Schicht, wird sie als **nicht abgedeckt** markiert (Nachricht `ShiftUncovered`, steht im Katalog, ist vorbereitet) und HR wird benachrichtigt.
  - Nach dem Tag steht im Dienstplan **nicht „leer“**, sondern das **Zeitplan-Zeichen** („war krank“: K), weil die Lohnabrechnung es braucht.
  - Die Dienstplanung selbst ist nicht Teil dieses Vorhabens; gebaut wird nur die Schnittstelle.
- **Rechte** mit Beschreibung (neue `employee:`- oder `absence:`-Rechte, Vergabe später in Access), Tests zuerst, Migration, Doku.

**Zu beachten**
- Datenschutz: Die Art kann Klasse 2 sein (Krank). Auf dem Bus nur neutralisiert („nicht verfügbar“, ohne Grund), wie bei `EmployeeAvailabilityChanged`.
- Jede Regel mit Datumsbezug braucht die Pflichtfälle: Monats- und Jahreswechsel, Sommer-/Winterzeit, Schaltjahr, Feiertag, Zeitraum über Mitternacht.

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
