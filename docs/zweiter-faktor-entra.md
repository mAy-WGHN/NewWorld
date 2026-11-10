# Zweiter Faktor mit Microsoft Entra

Entscheidung 45 (`CLAUDE.md`), freigegeben 2026-11-05, überarbeitet 2026-11-10. Der Datenschutzbeauftragte hat der gespeicherten Zuordnung zugestimmt. Der Ablauf weicht von Entscheidung 18 ab (Entra nur dort, wo ein externer Dienst es verlangt); die Abweichung ist gewollt.

**Ziel:** Wer die eigene Lohnabrechnung (und nur diese, weitere Bereiche erst nach neuer Entscheidung) öffnet, bestätigt zusätzlich in der Microsoft Authenticator App. Die Lohnabrechnung selbst entsteht in der HR API (noch nicht gebaut); gebaut ist der Baustein in Access, in `Shared.Authorization` und im Frontend.

**Ausgangslage:** Alle Mitarbeitenden haben ein Microsoft-365-Konto im bestehenden Firmen-Tenant. Die Anmeldung bleibt Personalnummer + BC-PIN; Entra ist nur der zweite Faktor.

## Ablauf

1. Die Person ist mit PIN angemeldet und öffnet etwas, das den zweiten Faktor verlangt; der Dienst antwortet `403` mit Titel `SecondFactorRequired`, das Frontend startet den Ablauf bei Access (`/api/auth/step-up/start`).
2. Access leitet zu Entra weiter (OpenID Connect, Authorization-Code mit PKCE, `prompt=login`); Entra verlangt die Bestätigung in der Authenticator App.
3. Access prüft den ID-Token (Signatur, Aussteller, Zielgruppe, Tenant, Nonce) und dass **MFA wirklich stattfand** (`amr` enthält `mfa`); fehlt das, wird abgelehnt (fail closed), egal wie der Tenant eingestellt ist.
4. Access prüft, dass der Entra-`oid` zu **dieser** Person gehört, und stellt die Bestätigung als Claim `mfa_until` im neu ausgestellten Access-Token aus (Gültigkeit 15 Minuten, `Access:StepUp:Validity`). Login und Refresh tragen ihn nie.
5. Die Dienste prüfen die Bestätigung lokal über `Shared.Authorization`. Entra-Tokens werden an niemanden weitergereicht, gespeichert wird nur die Zuordnung.

## Zuordnung Person ↔ Entra-Konto

Auf dem Bus gibt es keine E-Mail-Adresse. HR pflegt deshalb in Access je Person das Microsoft-Konto (UPN; Recht `access:entra-link:manage`, protokolliert); Access bindet beim ersten Erfolg die unveränderliche Kennung `oid`, danach zählt nur diese (ein anderer `oid` wird abgelehnt, ein geänderter UPN stört nicht). Eine automatische Zuordnung über eine Firmen-E-Mail aus BC ist offen (`TODO(BC-Modell)`).

## Entschieden

- Gilt **nur für die Lohnabrechnung**; die Bestätigung gilt 15 Minuten.
- **Ausgeschiedene** dürfen den Ablauf nutzen (Rolle „Former Employee“), solange ihr Microsoft-Konto besteht; die IT muss das beim Austritt beachten.
- **Verlorenes Handy oder Passwort** löst Entra, nicht das Portal. **Entra nicht erreichbar:** die Lohnabrechnung ist dann nicht erreichbar (fail closed).
- **Keine Mehrkosten:** Microsoft 365 F1 enthält Entra ID P1 und MFA (bestätigt 2026-11-05, rund 247 Personen). **Nicht abgedeckt:** Konten ohne F1 (z. B. Business Basic, etwa 9 Personen); sie wären nur betroffen, wenn sie die Lohnabrechnung im Portal öffnen müssen.

## Aufgaben der IT (nicht des Portals)

- **App-Registrierung** für Access (Weiterleitungsadresse, Client-Geheimnis oder besser Zertifikat im Key Vault); Konfiguration unter `Access:StepUp`, Secrets nie in `appsettings*.json`.
- Der Tenant muss MFA **tatsächlich verlangen**, sonst lehnt Access mangels `amr=mfa` ab: Sicherheitsstandards, MFA pro Person oder Conditional Access (braucht P1). Die Richtlinie legt die IT an, sobald die App-Registrierung steht.
- Die Person richtet die Authenticator App einmalig ein.

## Test und Entwicklung

Es gibt keinen Entra-Test-Tenant. Der Ablauf wird gegen einen **Entra-Ersatz** im Test entwickelt (Discovery, Schlüssel und Token-Ausgabe nachgestellt); die Prüfung von `amr`, Nonce und Zuordnung ist so ohne echten Tenant testbar. Der echte Tenant wird nur manuell geprüft.

## Offen

Konten Ausgeschiedener und Personen ohne F1 klären (IT); App-Registrierung und Richtlinie in Entra anlegen (IT); automatische Zuordnung über eine Firmen-E-Mail aus BC.
