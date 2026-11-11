# Nächste Schritte (Mitarbeiterkarte)

Stand 2026-10-28. Gebaut sind Schritt 1 (Karte, Nummernkreis, Auswahllisten, Datenschutzstufen), Schritt 2 (Änderungsanträge) und Schritt 3 (Nicht-Verfügbarkeit). Gebaut ist auch Schritt 4 (Arbeitszeitkonto); dazu Schritt 5 als Ausblick. Entwurf und Entscheidungen: `docs/mitarbeiterkarte.md`, `CLAUDE.md` (Entscheidungen 29 bis 31).

Als Nächstes: **Schritt 5** (braucht Freigabe und BC-Modell) oder die offenen Punkte unten.

---

## Schritt 3: Nicht-Verfügbarkeit (gebaut 2026-10-28, Entscheidung 32)

Arten (24 aus BC), Einträge von HR, Zeitleiste mit KU, Sichtbarkeit nach Art/Verfügbarkeit, Schichthinweis an die HR-Gruppe. Details: `docs/mitarbeiterkarte.md`, `CLAUDE.md` (Entscheidung 32).

**Offen aus Schritt 3**
- **Benachrichtigungstexte** sind gebaut (Entscheidung 33). Offen: Oberfläche, HR die Rechte `template:manage` und `unavailability:*` in Access geben, Datenschutzbeauftragter zum Mail-Anbieter, Bezirksleiter-Vermerk, ausgeschiedene Personen überspringen, BC soll Vor- und Nachnamen getrennt liefern.
- **Einrichtung in Access:** HR-Gruppe als Zuständigkeit zum Thema `schedule.adjustment` (sechs Personen, pflegbar), Rechte `unavailability:*` für HR, Leserechte der Rollen.
- **BC-Spiegelung** der Einträge (`TODO(BC-Modell)`), **Dienstplan** als Quelle für `IShiftLookup`, **Teiltage** (Uhrzeiten) später, Einträge mit offenem Ende (bisher nicht). Einträge, die nach einem später gemeldeten Austritt reichen, werden nur markiert (`afterExit`, Entscheidung 38).

---

## Schritt 4: Arbeitszeitkonto (gebaut 2026-10-28, Entscheidung 34)

Buchungskonto, Saldo aus Buchungen, Monatsabschluss gegen die Monatsschwelle des Arbeitsverhältnisses, Freizeitausgleich, Auszahlung, Auszahlung bei Austritt, Korrekturen, Verfall nach Erfassungsdatum, Regeln und Schwellen von HR pflegbar.

**Offen aus Schritt 4**
- **Minusstunden** sind von HR schaltbar (`bookMinusHours`, Standard aus, Entscheidung 35); die Untergrenze für den Fehlbetrag (`maxMinusHours`) ist gebaut.
- **Austritt:** Die Auszahlung des Restguthabens ist eine HR-Aktion mit Erinnerung am Tag nach dem Austritt (Entscheidung 37, Thema `workingtime.payout` in Access einzurichten).
- **BC-Spiegelung** des Kontos (`TODO(BC-Modell)`), **Stunden aus der Zeiterfassung** statt von Hand, Rechte `workingtime:*` in Access an HR vergeben, Monatsschwellen je Arbeitsverhältnis eintragen.

---

## Schritt 5 (Ausblick, braucht Freigabe und BC-Modell)

Übertragung neuer Mitarbeitender nach BC: neue Nachricht (nur nach Freigabe), Start-PIN ohne Speichern im Klartext, Abgleich der internen GUID mit BC, Zustand `PendingBc` zu `Synced`. Dazu BC-Feldnamen der Mitarbeiterkarte und die Entscheidung, welche Daten BC bekommt.

## Segment C: Fachliche Platzhalter und Entscheidungen (Umsetzungsvorschlag, Stand 2026-11-11)

Aufwand grob: S (klein), M (mittel), L (groß). Alles hier wartet auf Freigabe oder Klärung; die technischen Blocker (Frontend-Endpunkte, Mehrinstanzbetrieb, Migrationen, AU-Betrieb) stehen nicht hier, sondern wurden im Code umgesetzt.

**C1. Echter Mailversand** (M, Rückfrage Datenschutzbeauftragter)
- Das Adapter-Interface für Mail gibt es schon. Vorschlag: SMTP-Adapter mit `System.Net.Mail` (paketfrei); ein Anbieter wie Brevo läuft über SMTP-Relay ohne SDK.
- Bis zur Klärung bleibt der Fake aktiv. Der Wechsel ist reine Konfiguration (Zugangsdaten aus User Secrets bzw. Key Vault, nie `appsettings*.json`).
- Vorher klären: Anbieter und Auftragsverarbeitung, Absenderadresse, Mail-Text Klasse 2 nur bei `mailMayContainSensitive` (Entscheidung 33).

**C2. Audit-Log: Löschung, Aufbewahrung, Hash-Kette** (M, Rückfrage Datenschutzbeauftragter)
- Vorschlag für die Abstimmung: Bei `EmployeeDataErasureRequested` bleiben die Einträge erhalten; Name und Personalnummer werden durch einen Platzhalter ersetzt, die geänderten Werte bleiben. Das Datenmodell bleibt sonst unveränderlich.
- Technisch: kontrollierte Ausnahme in Interceptor und Datenbank-Trigger (nur das Akteursfeld, nur über eine eigene Datenbankrolle oder Funktion) und ein eigener Eintrag, der die Anonymisierung selbst protokolliert.
- Aufbewahrungsfrist je Dienst (Konfiguration, Löschjob hinter der Sperre aus B1).
- Hash-Kette erst nach der Anonymisierung, weil die Löschung die Kette sonst bricht.

**C3. Portal** (je M)
- **Anonymisierung der Personalnummer in Meldungen** bei Löschung: Handler auf `EmployeeDataErasureRequested`, der das Feld leert (Integrationstest wie in den anderen Diensten).
- **Zweitmeinung:** eine Meldung wird einer einzelnen Person außerhalb der eigenen Filialen freigegeben (Tabelle `report_shares`, eigenes Recht, Prüfung zusätzlich zum Filialfilter). Vorher fachlich klären, wer teilen darf und ob die Freigabe befristet ist.
- **`EmployeeAvailabilityChanged` speichern:** erst sinnvoll, wenn das Portal Verfügbarkeit anzeigt (`TODO(Fachfunktion)` in `CopyHandlers.cs`).
- **News, Schulungen, Filialordner:** eigene Fachfunktionen außerhalb des Scopes, kein Umsetzungsvorschlag.
- **Hauptfiliale im Portal** wird übernommen (Entscheidung 21), Filialname und Rechte in Access einrichten bleibt Konfiguration.

**C4. Benachrichtigungen** (S bis M)
- **Absender „System“ in der InApp-Liste:** Variante ohne Katalogänderung: Der Absender ergibt sich aus dem Vorlagenschlüssel (`<dienst>.<anlass>.<empfängerrolle>`), die Liste zeigt ihn an. Variante mit optionalem Absenderfeld an `SendNotification` ist additiv, berührt aber den Katalog und braucht Rückfrage.
- **Oberfläche für die Texte:** Liste, Bearbeiten und Vorschau im Frontend (die API `/api/notification-templates` steht seit Entscheidung 33). Dazu HR das Recht `template:manage` in Access geben.
- **Ausgeschiedene Personen überspringen** (Austrittsdatum liegt in der Namenskopie), Bezirksleiter-Vermerk für Mail mit Klasse 2.

**C5. Dienstplanung** (L)
- Eigenes Vorhaben, im Plan out of scope. Vorbereitet sind der Port `IShiftLookup` (Workforce) und `ShiftUncovered`. Möglicher nächster Schritt: nur ein Datenmodell-Entwurf (Schichten, Besetzung, Tausch), noch kein Code.

**Reihenfolge für Segment C**
1. Nach Klärung mit dem Datenschutzbeauftragten: C1, C2.
2. Nach fachlicher Klärung: C3 (Zweitmeinung).
3. Nach Rückfrage zum Katalog: C4 (Absenderfeld); die Oberfläche für Texte kann sofort.
4. C5 nur auf ausdrücklichen Wunsch.

---

## Weitere offene Punkte (unabhängig von den Schritten)

- Austritt im Jahr: anteiliger Urlaubsanspruch ist umgesetzt (Entscheidung 39); nicht gebaut sind Abgeltung von Resturlaub und Zusatztagen und der gesetzliche Mindesturlaub.
- **Vor dem Go-live prüfen:** Ob 15301 noch die zuletzt vergebene Personalnummer ist (Bereich 1001 bis 99999).
- Rechte und Zuständigkeiten in Access zum Schluss in der Oberfläche vergeben (u. a. Thema `employee.change-request`).
- Portal: FAQ, Meldungen und Statistik gebaut (Entscheidung 41, `docs/portal.md`). Offen: Altdaten-Import (Schema vom Projektverantwortlichen), Zweitmeinung, Empfänger je Kategorie und Rechte in Access einrichten. Das Portal übernimmt die Hauptfiliale noch nicht.
- BC-Änderungserkennung per Polling (BC-Feldnamen fehlen).
- Datenschutzbeauftragter: Klasse der Lohnart, AU, Datenklassifizierung.
