# Plattform: Schnittstellen zwischen Identity, Portal und HR-Tool (Entwurf)

Status: **Entwurf zur Abstimmung**, nichts davon ist umgesetzt. Ziel ist, die drei Anwendungen so zu schneiden, dass sie unabhängig
ausgeliefert werden können (eigene Datenbank, eigener Release-Rhythmus) und trotzdem ein Login, ein Rechtesystem und dieselben Filialen/Regionen nutzen.

## 1. Bausteine und wer wofür zuständig ist

| Baustein | Besitzt (Daten) | Macht |
|---|---|---|
| **Identity** | Benutzer (Personalnummer, Passwort-Hash, BC-PIN verschlüsselt, aktiv, Heimatfiliale), Rollen, Zuweisungen, Scopes, Katalog aller Rechte | Anmeldung (BC), Token ausstellen, Rechte und Reichweite auflösen, Rollen/Scopes verwalten |
| **Portal** (heutige IRS-API) | Meldungen, Filialen (aus BC), FAQ, Push-Abos, Benachrichtigungs-Einstellungen | Fachlogik des Portals, zeigt Dienstpläne aus dem HR-Tool an, reagiert auf HR-Ereignisse |
| **HR-Tool** | Mitarbeiterdaten, Dienstpläne, alles Personalbezogene | Fachlogik HR, veröffentlicht Ereignisse bei Änderungen |

Grundregeln:
- **Jeder Baustein hat seine eigene Datenbank.** Kein Baustein liest die Tabellen eines anderen.
- **Gemeinsamer Schlüssel** für Personen ist die User-ID (`sub`, Guid) und die Personalnummer (`username`). Mehr wird nicht dupliziert.
- **Die Hoheit über Daten bleibt beim Besitzer.** Das Portal speichert keine Dienstpläne, es holt sie bei Bedarf (höchstens kurz zwischengespeichert).
- Änderungen an Schnittstellen sind **abwärtskompatibel** (nur Felder hinzufügen), damit jeder Baustein allein aktualisiert werden kann.

## 2. Identität und Token

- Identity stellt das JWT aus (RS256, derselbe Schlüssel wie heute, damit bestehende Tokens gültig bleiben). Claims: `sub` (User-ID), `username` (Personalnummer), `exp`, `iat`. **Keine Rechte im Token.**
- Portal und HR-Tool **validieren** das Token selbst (öffentlicher Schlüssel, später ein JWKS-Endpunkt `GET /.well-known/jwks.json`). Kein Aufruf pro Anfrage nötig.
- Beim Aufruf zwischen Bausteinen (Portal → HR) wird **das Token des Users weitergereicht**. Das HR-Tool prüft dann dieselben Rechte; das Portal bekommt keine Sonderrechte.
- Für Aufrufe ohne User (Ereignisse, Abgleich) gibt es **Dienst-Zugangsdaten** pro Baustein (Client-Credentials), mit eng gefassten Rechten.
- Deaktivierte User: Jede App prüft beim Auflösen der Rechte, ob der User aktiv ist (heute pro Anfrage per DB, künftig über die Auflösung unten).

## 3. Rechte

**Katalog-Registrierung.** Jede App erzeugt ihren Katalog wie heute aus Entities und Attributen (gemeinsames Paket) und meldet ihn beim Start an Identity:

```
PUT /api/apps/{appKey}/catalog        (Dienst-Credentials)
{ "areas": [ { "key": "report", "label": "Meldungen",
               "actions": [ { "privilege": "report:read", "label": "Lesen", "scopable": true } ],
               "definesReach": true } ] }
```

Identity führt die Kataloge aller Apps zusammen (Schlüssel sind pro App eindeutig, z. B. `report:*` gehört dem Portal, `schedule:*` dem HR-Tool). Die Rollen-Matrix der UI wird daraus gebaut.

**Auflösung.** Eine App fragt für den angemeldeten User:

```
GET /api/access/me?app={appKey}       (Token des Users)
→ { "privileges": ["report:read", ...],
    "scopes": { "report:read": { "all": false, "locationIds": [...], "reportIds": [...] } },
    "roles": [ ... ] }
```

Das Ergebnis darf die App **wenige Sekunden** zwischenspeichern. Ein Entzug wirkt dadurch nach spätestens dieser Zeit, oder sofort, wenn Identity das Ereignis `access.changed` (siehe 5) sendet und die App den Zwischenspeicher leert.

**Durchsetzung bleibt in der App:** Die App prüft das Recht an der Route und wendet den Scope in ihren **eigenen** Abfragen an (wie heute der Meldungsfilter). Identity kennt die Daten der Apps nicht.

## 4. Scopes und Dimensionen

Ein Scope ist eine benannte Menge von Mitgliedern in einer **Dimension**. Heute: `location` (Filiale), `report` (Einzelmeldung). Für das HR-Tool schlage ich vor:
- `location` bleibt (dieselben Filialen und Regionen, damit niemand sie doppelt pflegt).
- `employee` (ein einzelner Mitarbeiter) für Vertretung und Einzelfreigaben, vergleichbar mit der Zweitmeinung bei Meldungen.
- Eine Regionszugehörigkeit eines Mitarbeiters ergibt sich aus seiner Heimatfiliale (`users.home_location_id`).

Eine App wendet nur die Dimensionen an, die sie kennt; unbekannte Dimensionen in einem Scope werden ignoriert (fail-closed: sie gewähren nichts).

## 5. Ereignisse (damit das Portal auf Änderungen reagiert)

**Zustellung.** Vorschlag: **Azure Service Bus (Topic mit Abonnements)**. Gründe: Entkopplung (das HR-Tool muss das Portal nicht kennen), eingebaute Wiederholung und Dead-Letter, mehrere Abonnenten möglich.
Einfachere Alternative ohne zusätzliche Infrastruktur: Webhooks mit Signatur und Wiederholung. Beides braucht dieselbe Disziplin (unten).

**Regeln, damit es zuverlässig bleibt:**
- **Outbox im Absender:** Das Ereignis wird in derselben Datenbank-Transaktion wie die Änderung gespeichert und danach veröffentlicht. So geht keines verloren und keines wird ohne echte Änderung gesendet.
- **Mindestens einmal zugestellt → der Empfänger ist idempotent** (Ereignis-ID merken, doppelte ignorieren).
- **Schlanke Ereignisse:** nur IDs und Art der Änderung, keine personenbezogenen Daten im Inhalt. Wer Details braucht, holt sie über die API **mit seinen Rechten**.
- **Abgleich als Netz:** Zusätzlich `GET /api/changes?since={zeitpunkt}` am Absender, falls ein Empfänger länger offline war oder ein Ereignis verpasst hat.
- **Versioniert:** Jedes Ereignis trägt `type` und `version`. Felder werden nur hinzugefügt.

**Format (gemeinsamer Umschlag):**

```json
{ "id": "uuid", "type": "schedule.changed", "version": 1,
  "occurredAt": "2026-10-19T08:30:00Z", "source": "hr",
  "subject": { "kind": "employee", "id": "uuid" },
  "data": { "scheduleId": "uuid", "from": "2026-11-02", "to": "2026-11-08" } }
```

**Erste Ereignisse (Vorschlag):**

| Von | Typ | Zweck |
|---|---|---|
| HR | `schedule.published`, `schedule.changed` | Portal benachrichtigt Betroffene, aktualisiert die Anzeige |
| Identity | `access.changed` (User, Rolle oder Scope geändert) | Apps leeren ihren Rechte-Zwischenspeicher |
| Identity | `user.deactivated` | Apps beenden Sitzungen, Push-Abos ruhen |
| Portal | `report.created` (optional) | Für spätere Auswertungen im HR-Tool |

**Anknüpfung an die Benachrichtigungen:** Die Tabelle `notification_policy` (Ressource, Ereignis, Kanal, Bedingungen, `allow`/`deny`) ist dafür vorbereitet. Ein HR-Ereignis wird im Portal wie eine Meldung behandelt: Die Policies entscheiden, wer wie benachrichtigt wird, und der Scope entscheidet, wer es sehen darf („eine Filiale genügt“ für eine Regions-Rundmail).

## 6. Dienstplan-Anzeige im Portal

- Das HR-Tool bietet eine Leseschnittstelle, z. B. `GET /api/schedules?employeeId=…&from=…&to=…`. Das Portal ruft sie **mit dem Token des Users** auf.
- Das HR-Tool entscheidet über die Sicht: der eigene Dienstplan, der des Teams oder der Region, je nach Rechten und Scope.
- Das Portal speichert die Daten nicht dauerhaft. Ein kurzer Zwischenspeicher (Sekunden bis Minuten) ist erlaubt, ein `schedule.changed`-Ereignis leert ihn.
- Bei Ausfall des HR-Tools zeigt das Portal eine Meldung statt der Dienstpläne; der Rest bleibt nutzbar.

## 7. Betrieb und Versionierung

- Jeder Baustein: eigene Azure-App, eigene Datenbank, **Deployment-Slots mit Tausch** und ein Health-Check, der die Datenbank und die Abhängigkeiten prüft.
- Datenbankänderungen immer **additiv zuerst** (neue Spalte/Tabelle, dann Code, erst viel später Aufräumen).
- Schnittstellen und Ereignisse sind abwärtskompatibel; Entfernen erst nach einer Übergangszeit, in der alle Verbraucher umgestellt sind.
- Beim Ausfall von Identity: Apps arbeiten mit dem letzten bekannten Rechte-Zwischenspeicher für kurze Zeit weiter, neue Anmeldungen sind nicht möglich.

## 8. Aufteilung des heutigen Codes (Reihenfolge)

1. **Solution in Projekte trennen**, ohne Verhaltensänderung: gemeinsames Rechte-Paket (Katalog, Attribute, Scope-Kontext, Prüfung), Identity-Teil (Benutzer, Rollen, Scopes, BC-Anmeldung), Portal.
2. **Identity herauslösen** (eigener Dienst, eigene Datenbank, Endpunkte aus 2 und 3) — vor dem Beginn des HR-Tools.
3. **Portal auf Identity umstellen:** Token validieren, Rechte über `/api/access/me`, Katalog registrieren, lokale Benutzer-Kopie für Push-Abos und Einstellungen (über `sub` verknüpft).
4. **HR-Tool** mit dem gemeinsamen Paket starten; Ereignisse und Dienstplan-Schnittstelle zuerst definieren.

## 9. Offene Entscheidungen

1. **Ereignis-Transport:** Azure Service Bus (empfohlen) oder Webhooks mit Wiederholung?
2. **Anmeldung:** eigene Anmeldung über BC beibehalten oder einen zentralen Microsoft-Entra-Mandanten als Anmeldequelle nutzen?
3. **Wohin ziehen die Benutzer-Tabellen?** Die Produktions-Datenbank gehört bisher dem Symfony-Original (Rückfallebene). Identity bekommt entweder zunächst dieselbe Datenbank als alleiniger Besitzer oder eine neue mit Datenübernahme.
4. **Sitzungen und Token-Laufzeit** bei mehreren Apps (heute 30 Tage Access-Token, Refresh-Token): beibehalten oder verkürzen?
5. **Welche Scope-Dimensionen braucht HR wirklich** (`employee`, Team, Abteilung)?

## 10. Stand der Umsetzung (2026-10-19)

Entschieden: vier Bausteine, jeder ein eigenes Repo unter `source/repos`, jeweils ASP.NET 10 — **IRS-API** (Portal, bleibt hier), **Rollen-Rechte** (Anmeldung, Rollen, Rechte, Scopes,
Filialen), **BC-Anbindung** (einziger Weg zu Business Central), **hr-tool-API** (HR-Tool). Die Filialen liegen bei Rollen-Rechte (Scopes und Heimatfiliale brauchen sie), das Portal holt sie bei Bedarf (`GET /api/apps/portal/locations`). Umzug nach dem Muster „kopieren, dann umstellen“:

- **BC-Anbindung** ist fertig: `POST /api/employees/verify`, `GET /api/stores`, API-Schlüssel je Aufrufer, Fake-BC-Tests, Format-Vertragstest.
- **Rollen-Rechte** ist fertig und eigenständig lauffähig (bietet Anwendungen zusätzlich `GET /api/apps/{appKey}/locations`): Login (BC über die BC-Anbindung, Offline-Fallback), Rollen, Zuweisungen mit Scope und Gültigkeit, Scopes, eigene Rechte,
  Filialen samt BC-Abgleich, **Registrierung der Rechtekataloge** (`PUT /api/apps/{appKey}/catalog`, Abschnitt 3). Eigene Datenbank (lokal Port 5433), eigene Migration `InitialCreate`.
- **Das Portal ist umgestellt (2026-10-19):** Es prüft nur noch die Signatur der Tokens von Rollen-Rechte und löst Rechte samt Reichweite bei Rollen-Rechte auf (`GET /api/me/privileges` mit dem Token des Users, 10 s zwischengespeichert; 401/503 wie in `CLAUDE.md`), meldet seinen Rechtekatalog beim Start an (`PUT /api/apps/portal/catalog`), holt Filialen über `GET /api/apps/portal/locations` und sendet `report.created` an Benachrichtigungen. Anmeldung, Benutzer, Rollen, Rechte, Scopes, Filialen, BC-Code und Push-Code sind aus dem Portal **entfernt**; die Tabellen räumt die Migration `RemoveIdentityAndNotificationTables` weg (Produktion erst nach der Datenübernahme). Das Frontend muss die Endpunkte für Anmeldung, Benutzer, Rollen, Filialen und Push bei den neuen Diensten aufrufen (eine gemeinsame Adresse/Verteiler steht aus).
- **Noch offen:** `GET /api/access/me?app=…` als eigener Auflösungs-Endpunkt für Dienste (heute dient `/api/me/privileges` mit dem User-Token), gemeinsames Rechte-Paket statt kopiertem Katalog-Code, Ereignisse von Rollen-Rechte (`access.changed`), Outbox für Ereignisse des Portals, Datenübernahme der Benutzer und Push-Abos aus der bestehenden Datenbank.

**Fünfter Baustein: Benachrichtigungen** (nach der Erkenntnis, dass Benachrichtigungen ein globales Thema sind: Portal, HR-Tool und weitere lösen sie aus, die Regions-Rundmail geht quer über alle).
Repo `Benachrichtigungen`, ASP.NET 10, eigene Datenbank (lokal Port 5434), **fertig und eigenständig lauffähig**:
- Eingang `POST /api/events` mit API-Schlüssel je Absender (`Sources__Keys__<absender>`), Ereignis-ID für Idempotenz, 503 ohne Merken, wenn Rollen-Rechte nicht erreichbar ist (Abschnitt 5).
  Erster Typ: `report.created` (`reportId`, `category`, `severity`, `description`, `locationIds`); Titel und Link baut der Dienst.
- Empfänger: User mit Push-Gerät, gefiltert durch **Rollen-Rechte** (`POST /api/apps/{appKey}/recipients`, neu dort: wer darf `report:read` für eine der Filialen — „eine genügt“), dann deren Einstellungen
  (Kategorie, Mindest-Schwere, Wochentag Europe/Berlin — die bisherigen Portal-Regeln). `notification_policy` ist mitgewandert, wird aber noch nicht ausgewertet.
- Push-Abos und `GET/PATCH /api/me/notification-settings` laufen mit dem JWT von Rollen-Rechte (neu im Token: `sub` = User-ID).
- Noch offen: Einstellen der Benachrichtigungen **für andere** User (braucht die Rechteprüfung gegen Rollen-Rechte); Übernahme der
  bestehenden Abos/Einstellungen aus der Portal-Datenbank (VAPID-Schlüssel unverändert!); Auswertung der `notification_policy`; weitere Kanäle (E-Mail, Postfach).