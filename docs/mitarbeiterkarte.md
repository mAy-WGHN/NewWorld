# Mitarbeiterkarte (Entwurf)

Stand 2026-10-27. **Entwurf, nichts davon ist gebaut.** Ziel: neue Mitarbeitende werden künftig im neuen Tool angelegt, mit **einer** gesammelten Mitarbeiterkarte statt zwei getrennter Listen. Danach müssen die Daten nach BC fließen; das wird jetzt nur mitgedacht, nicht gebaut.

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
| Kasse | Kasse Schnittstellenprofil, Kasse Menüprofil, POS Stilprofil, Manager Benutzerrechte | K | in deiner Aufzählung nicht genannt; übernehmen? |
| | Berechtigungsgruppe | K, D | |
| Zeiterfassung / Export | Web-Zeiterfassung erlaubt | D | Standard **ja** (das HR-Tool braucht das Flag für die Stempelung) |
| | QTrado-Export | D | Flag |
| Anmeldung | Passwort, „Passwort muss geändert werden“ | D | **Konflikt** mit Entscheidung 13/19, siehe offene Fragen |

## Personalnummer und Nummernkreis

Ja, den bestehenden Nummernkreis weiterführen. Vorschlag:
- Im Tool liegt ein **Nummernkreis** mit der **zuletzt vergebenen Nummer**, den HR von Hand pflegen kann (Startwert, kein Import aus BC).
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

## Offene Fragen

1. **Passwort/PIN:** Die Anmeldung läuft über Personalnummer + BC-PIN, die BC selbst prüft (Entscheidung 19). Ein Passwort im Tool zu speichern widerspricht dem; ich würde **kein Passwort speichern**. Wo wird die Start-PIN einer neuen Person gesetzt (in BC), und wo ändert die Person sie später? Das Flag „muss geändert werden“ kann nur gelten, wenn es dafür einen Ort gibt.
2. **Besitz:** Workforce als Besitzer der Karte für neue Personen, wie oben?
3. **Kassendaten:** Kommen die vier Kassen-Felder (Schnittstellenprofil, Menüprofil, POS Stilprofil, Manager Benutzerrechte) mit auf die Karte?
4. **Nicht-Verfügbarkeitskonto:** Was genau wird darauf geführt?
5. **Überstundenkonto:** Nur Konto mit Anfangsbestand und manueller Buchung?
6. **Region, Rolle, Arbeitsort, Beschäftigungsverhältniscode, Lohnart:** Sind das freie Texte, Auswahllisten aus BC oder von HR gepflegte Listen? (Auswahllisten würde ich als pflegbare Stammlisten bauen.)
7. **Status:** nur abgeleitet (aktiv/ausgetreten) oder gibt es weitere Werte?
8. **Format der Personalnummer** und der aktuelle Startwert des Nummernkreises.
