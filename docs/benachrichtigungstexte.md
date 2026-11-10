# Benachrichtigungstexte: pflegbar, mit Variablen

Gebaut (Entscheidung 33 in `CLAUDE.md`), Entwurf von 2026-10-28, gekürzt 2026-11-10. Code in `src/Notifications`, Oberfläche in `Frontend/.../notification-templates`.

## Wie es funktioniert

- Vorlagen sind JSON-Dateien (`Templates/<dienst>.<anlass>.<empfängerrolle>.json`) und bilden den **Standard**. Die Personalabteilung überschreibt Titel und Text je Kanal (InApp, Push, E-Mail) in der Tabelle `template_overrides` (Recht `template:manage`, nicht scopable). Gespeichert wird nur, was abweicht; „Auf Standard zurücksetzen“ und eine **Vorschau** mit Beispielwerten gibt es.
- **Variablen:** `$USER.firstName`, `$USER.lastName` (löst Notifications aus einer eigenen Namenskopie auf; `EmployeeChanged` ist dafür der einzige zusätzliche Konsument) und die je Vorlage deklarierten Daten des Absenders (`$DP.store`, `$DP.month`, `$DP.year`). Die alte Form `{name}` gilt weiter. Unbekannte Variablen lehnt das Speichern ab; keine Formeln, kein HTML.
- **Klasse je Teil:**

  | Teil | Höchste Klasse |
  | --- | --- |
  | Push-Text, Mail-Betreff | 1, immer |
  | InApp-Text | 2 |
  | Mail-Text | 1; Klasse 2 nur bei Vorlagen mit `mailMayContainSensitive` (zunächst nur die HR-Vorlagen zur Dienstplanänderung) |
  | überall | Klasse 3 nie |

  Vorlagen mit sensiblem Text (`sensitiveText`, z. B. Krankheit, Urlaub, AU) behalten feste Push-Texte und Mail-Betreffe. Geprüft wird beim Speichern (422, bei festen Texten 409), beim Versand und beim Ausliefern (fehlerhafte gespeicherte Texte fallen auf den Standard zurück).
- Der Vermerk `mailMayContainSensitive` liegt im Code an der Vorlage, nicht in der Textpflege.

## Offen

- **Mail-Anbieter** (z. B. Brevo, Auftragsverarbeitung) und das Restrisiko von Klasse 2 per Mail: mit dem Datenschutzbeauftragten klären. Echten Mailversand gibt es noch nicht.
- **Bezirksleiter-Vermerk:** Welche weiteren Empfängerkreise Klasse 2 per Mail erhalten dürfen.
- **Namen:** BC liefert nur einen Gesamtnamen; `EmployeeNames.Split` nimmt „Vorname Nachname“ oder „Nachname, Vorname“ an (`TODO(BC-Modell)`).
- **Absender „System“** in der InApp-Liste: `SendNotification` hat dafür kein Feld; offen, ob es eine Eigenschaft der Vorlage wird.
- Ausgeschiedene Personen werden **nicht** übersprungen (Entscheidung 36).
- Keine mehrsprachigen Texte, nur Text ohne Formatierung.
