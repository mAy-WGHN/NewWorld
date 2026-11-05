# Zweiter Faktor mit Microsoft Entra (2026-11-05)

Ziel: Wer die eigene Lohnabrechnung (und später weitere Klasse-3-Daten) öffnen will, muss einen zweiten Faktor mit der Microsoft Authenticator App bestätigen. Freigegeben am 2026-11-05 (Entscheidung 45 in CLAUDE.md); der Datenschutzbeauftragte hat zugestimmt. Der Ablauf weicht von Entscheidung 18 ab (Entra nur dort, wo ein externer Dienst es verlangt); die Abweichung ist vom Projektverantwortlichen gewünscht (2026-11-05).

## Ausgangslage

- Alle Mitarbeitenden haben ein Microsoft-365-Konto im bestehenden Firmen-Tenant (Angabe des Projektverantwortlichen). Es ist also der **bestehende Tenant** und keine neue External-ID-Umgebung nötig.
- Die Anmeldung im Portal bleibt Personalnummer + BC-PIN (Entscheidung 13). Entra ist **nur der zweite Faktor** für bestimmte Rechte, kein Ersatz der Anmeldung.
- Die Lohnabrechnung selbst entsteht in der HR API (noch nicht gebaut, Entscheidung 36). Gebaut wird zunächst nur der Baustein in Access.

## Ablauf (Step-up)

1. Die Person ist mit PIN angemeldet und öffnet etwas, das den zweiten Faktor verlangt.
2. Der Dienst antwortet `403` mit dem Hinweis „zweiter Faktor nötig“. Das Frontend schickt die Person zu Access (`/api/auth/step-up/start`).
3. Access leitet zu Entra weiter (OpenID Connect, Authorization-Code-Flow mit PKCE, `prompt=login`, Anforderung von `amr`/`acr`). Entra verlangt dort die Bestätigung in der Authenticator App (Push mit Nummernabgleich).
4. Access prüft den ID-Token (Aussteller, Zielgruppe, Signatur, Nonce) und dass **MFA wirklich stattfand** (`amr` enthält `mfa`). Fehlt das, wird abgelehnt (fail closed), egal was der Tenant sonst einstellt.
5. Access prüft, dass der Entra-Konto-Bezeichner (`oid`) zu **dieser** Person gehört (siehe Zuordnung) und stellt für kurze Zeit eine Bestätigung aus (Claim im Access-Token oder eigener Marker, Gültigkeit konfigurierbar, Standard 15 Minuten).
6. Die Dienste prüfen die Bestätigung lokal über `Shared.Authorization`, wie bei allen Rechten.

Es werden keine Entra-Tokens an andere Dienste weitergereicht und keine Entra-Daten über die Person gespeichert außer der Zuordnung.

## Zuordnung Person ↔ Entra-Konto

Auf dem Bus gibt es keine E-Mail-Adresse der Person (`EmployeeChanged`, Klasse 1). Deshalb pflegt **HR in Access** je Person das Entra-Konto (UPN/E-Mail), Access speichert beim ersten erfolgreichen Step-up die unveränderliche Kennung `oid` und prüft künftig nur noch diese. Ändert sich der UPN, bleibt die Zuordnung gültig; ein anderer `oid` wird abgelehnt. Die Zuordnung zu ändern ist ein eigenes Recht und wird protokolliert. Automatisch über eine BC-E-Mail zuordnen geht erst, wenn BC eine Firmen-Adresse liefert (offen, `TODO(BC-Modell)`).

## Was Entra tenantseitig leisten muss (Aufgabe der IT, nicht des Portals)

- Eine **App-Registrierung** für Access (Weiterleitungsadresse, Client-Geheimnis oder besser Zertifikat im Key Vault).
- Der Tenant muss MFA tatsächlich verlangen, sonst bestätigt Entra ohne zweiten Faktor und Access lehnt mangels `amr=mfa` ab. Dafür gibt es drei Wege: **Sicherheitsstandards** (kostenlos, gilt tenantweit), **MFA pro Person** (in den Microsoft-365-Plänen enthalten) oder **Conditional Access** (braucht Entra ID P1, enthalten in Microsoft 365 E3/Business Premium). Welcher Weg offen ist, hängt vom Paket ab.
- Die Person richtet die Authenticator App einmalig im Microsoft-Konto ein.

## Kosten und Lizenz

Keine Mehrkosten. Die Serviceplaene von Microsoft 365 F1 enthalten Microsoft Entra ID P1 (geerbt) und Microsoft Azure Multi-Factor Authentication (bestaetigt am 2026-11-05, rund 247 zugewiesene Mitarbeitende). Conditional Access und die Authenticator App sind damit lizenziert. **Nicht abgedeckt:** Personen mit einem Konto ohne F1 (zum Beispiel Business Basic, etwa 9 Personen); sie haben kein P1 und waeren nur betroffen, wenn sie die Lohnabrechnung ueber das Portal oeffnen muessen (bei der IT klaeren). Die Richtlinie, die den zweiten Faktor fuer die App von Access verlangt, legt die IT an, sobald die App-Registrierung steht.

## Test und Entwicklung

Es gibt keinen Entra-Test-Tenant. Der Ablauf wird gegen einen **Entra-Ersatz** (WireMock-Container im AppHost, wie bei BC) entwickelt: Discovery, Schlüssel und Token-Ausgabe werden nachgestellt, die Prüfung von `amr`, Nonce und Zuordnung ist so ohne echten Tenant testbar. Der echte Tenant wird nur manuell geprüft.

## Entscheidungen (2026-11-05)

1. Der zweite Faktor gilt **nur fuer die Lohnabrechnung** (Recht dort, wo sie entsteht: HR API). Weitere Bereiche nur auf neue Entscheidung.
2. Die Bestaetigung gilt **15 Minuten** (`Access:StepUp:Validity`).
3. **Ausgeschiedene Personen** duerfen den Ablauf nutzen (Rolle „Former Employee“). Voraussetzung ausserhalb des Portals: Ihr Microsoft-Konto besteht weiter; die IT muss das beim Austritt beruecksichtigen (offen).
4. **Verlorenes Handy / Passwort:** wird in Entra (Microsoft-Konto) geloest, nicht im Portal.
5. **Entra nicht erreichbar:** die Lohnabrechnung ist dann nicht erreichbar (fail closed).
6. Die gespeicherte Zuordnung (Entra-Kennung je Person) hat der Datenschutzbeauftragte freigegeben.

## Offen

- Personen ohne F1-Lizenz (siehe Kosten) und Konten Ausgeschiedener: bei der IT klaeren.
- App-Registrierung in Entra und Conditional-Access-Richtlinie: macht die IT.
- Automatische Zuordnung ueber eine Firmen-E-Mail aus BC (bis dahin pflegt HR die Zuordnung).

## Bausteine (nach Freigabe)

1. Access: Tabelle der Zuordnung, Endpunkte für HR (Recht je Pflege), Step-up-Start/-Rückkehr, Prüfung des ID-Tokens, Bestätigung im Token; Tests mit Entra-Ersatz.
2. `Shared.Authorization`: Anforderung „zweiter Faktor“ je Recht und Prüfung der Bestätigung; Architekturtest, dass ein Recht mit Anforderung nie ohne sie genutzt wird.
3. Frontend: Hinweis „Bitte bestätige in der Authenticator App“, Weiterleitung und Rückkehr.
4. AppHost: Entra-Ersatz.
5. CLAUDE.md: Entscheidung zur Abweichung von 18.
