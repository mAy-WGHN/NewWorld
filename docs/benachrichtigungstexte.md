# Benachrichtigungstexte pflegbar, mit Variablen

**Gebaut am 2026-10-28 (Entscheidung 33 in `CLAUDE.md`).** Der Text unten ist der Entwurf; abweichend vom Entwurf gilt: Klasse 2 darf im Mail-Text bei Vorlagen mit `mailMayContainSensitive` stehen (zunächst HR), die Namenskopie ist gebaut, ausgeschiedene Personen werden noch nicht übersprungen, eine Oberfläche gibt es nicht (nur die Endpunkte).

Stand 2026-10-28. **Wunsch des Projektverantwortlichen:** Alle Benachrichtigungstexte sollen pflegbar sein, in der Oberfläche, von einer Person der Personalabteilung. Im Text sollen Bausteine („Variablen“) stehen dürfen, z. B. „Willkommen zurück, $USER.firstName“ (Hans Dampf sieht „Willkommen zurück, Hans“) und für die Meldung zur Dienstplan-Änderung bei Krankheit Filiale, DP, Monat und Jahr. **Noch nichts gebaut**, weil mehrere Punkte eine Freigabe brauchen (unten).

## Ist

Vorlagen sind JSON-Dateien im Notifications-Dienst (`Templates/<dienst>.<anlass>.<empfängerrolle>.json`), beim Start geprüft. Platzhalter `{name}` werden vom **Absender** in `SendNotification.Data` geliefert und je Platzhalter als `sensitive` (Klasse 2, nur InApp) oder `public` deklariert. Push und Mail sind generisch und dürfen keine Platzhalter haben (Entscheidung 14).

## Vorschlag

- **Speicherort:** Tabelle im Notifications-Schema. Die Dateien bleiben als **Standard** (Startwert und Rückfall), eine gepflegte Fassung überschreibt sie. Änderungen mit Zeitpunkt und Person (ohne Textinhalt im Log), Zurücksetzen auf den Standard möglich.
- **Pflege:** Endpunkte und Oberfläche für Titel und Text je Vorlage und Kanal; neues Recht `template:manage` (nicht scopable, Vergabe in Access an HR). Pflichtvorlagen bleiben nicht abschaltbar.
- **Variablen:** Schreibweise `$BEREICH.name` (z. B. `$USER.firstName`, `$USER.lastName`, `$STORE.name`, `$STORE.number`, `$DP.month`, `$DP.year`). Je Vorlage gibt es einen **Katalog erlaubter Variablen**; unbekannte Variablen lehnt das Speichern ab (keine freie Ausführung, keine Formeln, kein HTML), eine Vorschau zeigt den Text mit Beispielwerten. Die Variablen des Absenders (Filiale, Monat, Jahr …) liefert weiter `SendNotification.Data`; Variablen zur Empfängerperson löst Notifications selbst auf.
- **Datenschutz (Stand 2026-10-28, ändert Entscheidung 14):** Jede Variable trägt eine Klasse; die höchste erlaubte Klasse hängt am Kanal und an der Vorlage:

  | Teil | Höchste Klasse |
  | --- | --- |
  | Push-Text, Mail-Betreff | 1, immer |
  | InApp-Text | 2 |
  | Mail-Text | 1; bei Vorlagen mit dem Vermerk „Empfängerkreis darf Klasse 2 per Mail erhalten“ (z. B. HR) 2 |
  | überall | Klasse 3 nie (kann ohnehin nicht über den Service Bus) |

  Der Vermerk hängt an der Vorlage und liegt im Code, nicht in der Textpflege. Geprüft wird beim Speichern und beim Versand. Das passt zum Wortlaut der Datenschutz-Regel („nie Klasse-2-Inhalte in Push-Texten oder Mail-Betreffs“). Klasse 1 (Vorname, Nachname, Filiale, Monat, Jahr) ist in allen Kanälen erlaubt. Vorlagen, deren Text selbst Klasse 2 ist (z. B. Krankheit) und die keinen Vermerk haben, bleiben in Push und Mail generisch. Restrisiko Klasse 2 per Mail (Anbieter, Postfach-Weiterleitung): Abstimmung mit dem Datenschutzbeauftragten.
- **Absender „System“:** Anzeige in der InApp-Liste; `SendNotification` hat dafür kein Feld und braucht keines, wenn der Absender eine Eigenschaft der Vorlage ist.

## Offene Fragen (Freigabe nötig)

1. **Empfängername:** `$USER.firstName` setzt voraus, dass Notifications den Vornamen kennt. Das geht nur mit einer **Kopie der Mitarbeitenden** (Vorname, Nachname) in Notifications: neuer Konsument von `EmployeeChanged` (Katalog sieht Notifications dort nicht vor) und Löschen bei `EmployeeDataErasureRequested`. Alternative ohne Kopie: der Absender liefert den Namen mit; das geht aber nur dort, wo der Absender ihn kennt. **Empfehlung: Kopie in Notifications (nur Name, Klasse 1).** Zustimmung?
2. **Variablen in Push und Mail:** Entschieden, siehe Tabelle oben. Offen: **für welche Vorlagen** der Vermerk „Klasse 2 per Mail“ gilt (nur HR, oder auch Filialleitung/Vorgesetzte), und die Abstimmung mit dem Datenschutzbeauftragten zum Mail-Anbieter (z. B. Brevo, Auftragsverarbeitung).
3. **Wer pflegt?** Nur die Personalabteilung (ein Recht `template:manage`) oder auch andere Rollen, etwa je Dienst nur dessen Vorlagen?
4. **Sprache/Formatierung:** Reiner Text (Zeilenumbrüche) genügt? Mehrere Sprachen sind nicht vorgesehen.
5. **Variablenkatalog zum Start:** `$USER.firstName`, `$USER.lastName`, `$STORE.name`, `$STORE.number`, `$DP.month`, `$DP.year`; weitere Wünsche?
