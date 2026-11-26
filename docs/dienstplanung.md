# Dienstplanung – Datenmodell-Entwurf

Entwurf vom 2026-11-26, **kein Code, keine Entscheidung**. Dienstplanung, Zeiterfassung und Schichttausch liegen laut Plan außerhalb des Scopes; dieser Entwurf soll nur zeigen, wie sie sich an das Vorhandene hängen würden, damit der Aufwand und die offenen Fragen sichtbar sind. Eigentümer wäre **Workforce** (Besitzer von Dienstplänen, Schichten, Schichttausch, Stempelungen und Zeitkorrekturen laut Tabelle der Datenbesitzer in `CLAUDE.md`).

## Was schon da ist

| Vorhanden | Wirkung auf die Dienstplanung |
| --- | --- |
| `IShiftLookup` (Workforce.Core, Standard: keine Schichten) | Liefert geplante Schichten einer Person für einen Zeitraum. Krankmeldungen und Einträge der Nicht-Verfügbarkeit fragen darüber, ob Schichten unbesetzt werden. **Die Dienstplanung ersetzt nur diese Implementierung.** |
| `ShiftUncovered` (Event, Klasse 2 neutralisiert) | Wird bei unbesetzten Schichten gemeldet; die HR-Gruppe je Filiale (Thema `schedule.adjustment` in Access) bekommt eine InApp-Nachricht. Noch ohne Konsumenten. |
| Abwesenheiten, Nicht-Verfügbarkeit, KU | Zeitleiste je Person; bestimmt, wer an welchem Tag **nicht** planbar ist. |
| Arbeitstage Mo–Sa, Feiertage, „an Feiertagen geöffnet“ je Filiale | Rahmen für planbare Tage. |
| Arbeitszeitkonto, Monatsabschluss | Nimmt später die **gearbeiteten Stunden** (heute von Hand) aus der Zeiterfassung. |
| Berechtigungskopie (Access), Filial-Reichweite | Wer einen Dienstplan sehen oder ändern darf, je Filiale. |

## Kernbegriffe (Vorschlag)

- **Dienstplan** (`rosters`): eine Filiale, ein Zeitraum (Woche oder Monat), Zustand `Draft` → `Published`. Nach der Veröffentlichung gelten Änderungen als Änderungen mit Spur (Version).
- **Schichtart** (`shift_kinds`, von HR/Filialleitung pflegbar): Kürzel, Name, Standardzeiten (z. B. Früh, Spät, Wochenende), zählt als Arbeitszeit ja/nein, Pausenregel.
- **Schicht** (`shifts`): Filiale, Datum, Beginn und Ende (`DateTimeOffset`, lokale Zeit Europe/Berlin wie beim Berichtsheft), Schichtart, **Soll-Besetzung** (Anzahl und optional Rolle/Qualifikation).
- **Zuweisung** (`shift_assignments`): Schicht ↔ Person, Zustand `Planned`, `Confirmed`, `Swapped`, `Cancelled`; Besetzung fehlt, solange weniger Zuweisungen als Soll-Besetzung.
- **Schichttausch** (`shift_swaps`): Anfrage einer Person an eine andere (oder an alle der Filiale), Zustand `Requested`, `Accepted`, `Approved`, `Rejected`, `Withdrawn`; Genehmigung über `Shared.Approvals` (Zuständige zum Thema `schedule.swap` in Access).
- **Stempelung** (`time_punches`, spätere Stufe): Kommen/Gehen mit Quelle (Terminal, App, Korrektur), Korrekturen als eigene Zeilen mit Grund (Klasse 2).

## Regeln, die sich aus dem Bestand ergeben

1. **Keine Zuweisung an Tagen der Nicht-Verfügbarkeit.** Die Zeitleiste (Abwesenheit, Eintrag, KU, Austritt) gilt als harte Grenze; ein Konflikt ist ein Fehler (`409`), kein Warnhinweis. Die Prüfung ist eine Funktion im Core, nutzbar von Planung und Tausch.
2. **Nachträgliche Abwesenheit:** Kommt eine Krankmeldung oder ein Eintrag für eine geplante Schicht, bleibt die Zuweisung bestehen, wird als `Uncovered` markiert und über das vorhandene `ShiftUncovered` gemeldet (Verhalten von heute, nur mit echter Quelle).
3. **Feiertage:** Gesetzliche Feiertage sind Schließtage, außer die Filiale ist „an Feiertagen geöffnet“; dann sind Schichten erlaubt und zählen als Arbeitstage.
4. **Arbeitszeitrecht** (Höchstarbeitszeit, Ruhezeit 11 h, Pausen, Sonntagsarbeit) als **Prüfregeln**, zunächst als Warnung; welche davon harte Fehler sind, entscheidet die Personalabteilung. `TODO(Entscheidung)`.
5. **Sichtbarkeit:** Mitarbeitende sehen ihren eigenen Plan und den der Filiale (Name und Schicht, keine Gründe); Abwesenheitsgründe bleiben wie bisher nur bei Person, Vorgesetzten und HR (Entscheidung 32).
6. **Klasse:** Dienstpläne sind Klasse 1 (Organisationsdaten), Zeitkorrekturen und Gründe Klasse 2. Auf dem Bus nur Ereignisse ohne Gründe.

## Rechte (Vorschlag, je Filiale begrenzbar)

`roster:read`, `roster:plan` (anlegen, ändern, veröffentlichen), `roster:read-own`, `shift-swap:request-own`, `shift-swap:approve`, `shift-kind:manage`; später `timepunch:read-own`, `timepunch:correct`. Jedes Recht mit Klartext-Beschreibung im Katalog (Entscheidung 27) und in `docs/rollen.json` (Filialleitung: planen und genehmigen; Mitarbeitende: eigener Plan, Tausch beantragen).

## Nachrichten (nur nach Rückfrage, Katalog)

- **Keine neue Nachricht für den Start.** `ShiftUncovered` und `SendNotification` genügen (Plan veröffentlicht, Tausch angefragt, Tausch entschieden).
- Für BC (Spiegelung der Dienstplanung, laut CLAUDE.md „vollständig nach BC gespiegelt“) wäre ein Command nötig, vergleichbar `SyncAbsenceToBc`; das BC-Modell ist offen (`TODO(BC-Modell)`).

## Stufen (Aufwand grob)

| Stufe | Inhalt | Aufwand |
| --- | --- | --- |
| 1 | Schichtarten, Dienstplan je Filiale, Schichten und Zuweisungen, Konfliktprüfung gegen die Zeitleiste, `IShiftLookup` echt, Oberfläche „Dienstplan“ (Wochenraster) | groß |
| 2 | Veröffentlichen mit Benachrichtigung, Änderungen nach Veröffentlichung mit Spur, Eigenansicht | mittel |
| 3 | Schichttausch mit Genehmigung | mittel |
| 4 | Stempelung und Zeitkorrektur, Übergabe an das Arbeitszeitkonto | groß, braucht Terminal/App-Entscheidung |
| 5 | Spiegelung nach BC | braucht BC-Modell |

## Offene Fragen (für euch)

1. Planungszeitraum: Woche oder Monat? Vorlaufzeit bis zur Veröffentlichung?
2. Gibt es Rollen oder Qualifikationen, die eine Schicht besetzen müssen (z. B. Kassenberechtigung, Schlüsselträger)?
3. Wer plant: nur die Filialleitung oder auch Stellvertretung/Bezirksleitung?
4. Welche Arbeitszeitregeln sind harte Fehler, welche Warnungen?
5. Wie wird gestempelt (Terminal, App, Browser)? Hängt an Hosting und Hardware.
6. Welches Planungswerkzeug läuft heute? „Tool bleibt ersetzbar“ (Kontext in `CLAUDE.md`): Soll es zunächst nur **gelesen** werden (Import) statt neu gebaut?
