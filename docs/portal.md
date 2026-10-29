# Portal API (`src/Portal`): FAQ, Meldungen, Statistik

Stand 2026-10-29 (Entscheidung 41). Fachlogik aus `Archiv/Portal` (früher `IRS-API`), neu in `src/Portal`. Das Angular-Frontend bleibt: gleiche Pfade,
gleiche Antwortform, gleiche Feldnamen.

## Antwortform (kompatibel zum Frontend)

- Erfolg: `{"success":true,"data":…}`; `201` beim Anlegen, `204` ohne Body bei Löschen/Archivieren.
- Fehler: `{"success":false,"message":"…","errors":[{"field":"…","message":"…"}]}`. Validierung `422` (Feldnamen in camelCase), `404` mit festem Text,
  `403` mit festem Text je Aktion (einsehen/erstellen/bearbeiten/löschen), `401` nur für „nicht angemeldet“, `429` mit `Retry-After`.
- Enums klein geschrieben (`open`, `theft`, `critical`), JSON camelCase, FAQ-Position heißt `order`.

## Endpunkte

| Bereich | Pfad | Recht |
| --- | --- | --- |
| FAQ lesen (aktive) | `GET /api/faqs` | öffentlich |
| FAQ archiviert | `GET /api/faqs/archived` | `faq:read` |
| FAQ anlegen / ändern / Reihenfolge / löschen | `POST /api/faqs`, `PATCH /api/faqs/{id}`, `PATCH /api/faqs/reorder`, `DELETE /api/faqs/{id}` | `faq:create` / `faq:update` / `faq:update` / `faq:delete` |
| FAQ-Bild ansehen | `GET /api/faqs/images/{id}` | öffentlich (die zufällige ID ist die Zugriffskontrolle) |
| FAQ-Bilder | `POST/GET /api/admin/faq/images`, `DELETE /api/admin/faq/images/{id}` | `faq:create` / `faq:read` / `faq:delete` |
| Meldung anlegen | `POST /api/reports` | öffentlich, Rate Limit (10 je Minute und Client) |
| Meldungen | `GET /api/reports`, `GET /api/reports/archived`, `GET /api/reports/{id}` | `report:read` (Reichweite: Filialen) |
| Meldung ändern / archivieren / Hauptmeldung lösen | `PATCH /api/reports/{id}`, `DELETE /api/reports/{id}`, `DELETE /api/reports/{id}/parent` | `report:update` / `report:delete` / `report:update` |
| Statistik | `GET /api/statistics/{monthly,monthly-by-category,by-location-severity,by-personnel-number}` | `statistic:read` (Reichweite: Filialen) |
| Empfänger neuer Meldungen | `GET /api/report-recipients`, `PUT /api/report-recipients/{kategorie}` | `report:manage-recipients` |

## Regeln

- **Reichweite:** eine Meldung sieht (bearbeitet, archiviert, zählt in der Statistik), wer **mindestens eine** ihrer Filialen erreicht. Außerhalb der
  Reichweite, aber vorhanden: `403`; nicht vorhanden: `404`. Wer nur einen Teil erreicht, ändert auch nur diesen Teil der Filialliste (fremde Filialen
  bleiben, neue nur im eigenen Bereich).
- **Filialen** kommen als Kopie aus `StoreChanged`; eine Meldung für eine unbekannte oder geschlossene Filiale wird mit `404` abgelehnt. Duplikate in
  `locationIds` werden zusammengefasst. Die Quelle (`source`) ist beim öffentlichen Anlegen immer `employee` und danach nicht änderbar.
- **Hauptmeldung:** nur über `PATCH` (`parentReportId`) mit Zyklusprüfung, lösen über `DELETE …/parent`.
- **FAQ-Bilder:** nur PNG, JPEG, WebP (am Inhalt erkannt, höchstens 5 MB), als Datei unter `Portal:Faq:ImagePath` (Standard `var/uploads/faq`, außerhalb
  des Webroots, im Azure App Service nach `/home/...` legen), Name = zufällige UUID. Die Verknüpfung zur FAQ entsteht über die Bild-URL im Markdown
  (`/api/faqs/images/{id}`): `orphaned` (keine FAQ), `archived`, `active`; ein Bild in einer aktiven Antwort lässt sich nicht löschen (`409`).
- **Benachrichtigung bei neuer Meldung** (ersetzt `report.created` des Archivs): Wer benachrichtigt wird, hängt von der **Kategorie** ab und ist **einstellbar**
  (`PUT /api/report-recipients/{kategorie}`, Liste von Personen; leer = niemand; Kategorien `virus`, `bug`, `theft`, `other`, `suggestion`). Vorgesehen z. B.:
  Revisor bei `theft`, IT bei `bug` und `virus`. Das Portal schickt `SendNotification` (Vorlage `portal.report-created.handler`, InApp + generischer Push)
  in derselben Transaktion wie die Meldung (Outbox). **Die Nachricht enthält nichts aus der Meldung** (Klasse 2), nur den Link `/reports/{id}`; die
  Meldung wird nach dem Öffnen per API geladen. Das löst den offenen Konflikt aus Entscheidung 6 (Meldungstext im Push).
- **Löschung:** bei `EmployeeDataErasureRequested` entfallen Personenkopie, Berechtigungskopie und Empfängereinträge der Person.
- **Rate Limits:** im Speicher je Instanz (Sliding Window je Client-Adresse; `Portal:RateLimiting:RequestsPerMinute` 100, `ReportsPerMinute` 10). Hinter
  dem Proxy zählt `X-Forwarded-For` (`ForwardedHeaders:KnownProxies`, sonst wird jedem Proxy vertraut).
- **CORS:** `Cors:AllowedOrigins` (Liste) und/oder `Cors:AllowedOriginPattern` (Regex).

## Bewusst anders als im Archiv

- Datenbank neu (Schema `portal`, Tabellen `faqs`, `faq_images`, `reports`, `report_locations`, `report_recipients`); Zeiten ohne Zeitzone (UTC), `noticedAt`
  wie gesendet (Wanduhrzeit, nicht umgerechnet). Enums liegen kleingeschrieben in der Datenbank, damit der spätere Import der Altdaten einfach bleibt.
- Keine Benutzer, Rollen oder Filialen im Portal (Access bzw. BC), keine Zweitmeinung (Einzelmeldung außerhalb der eigenen Filialen teilen).
- Rechte lokal aus der Berechtigungskopie (`Shared.Authorization`), kein Aufruf von Access je Anfrage; die Rechte meldet das Portal bei Access an
  (Katalog mit Beschreibung).

## Offen

- **Altdaten übernehmen:** Schema der alten Datenbank folgt vom Projektverantwortlichen; danach Import (Meldungen, Filialzuordnung über die Filial-IDs, FAQ,
  Bilddateien in `Portal:Faq:ImagePath`). Die Filial-IDs der alten Daten müssen zu den Filial-IDs der BC-Kopie passen (sonst Zuordnung nötig).
- **Zweitmeinung** (einzelne Meldung außerhalb der eigenen Filialen teilen): nicht gebaut.
- **Einrichtung in Access:** Rollen mit den Rechten `faq:*`, `report:read/update/delete`, `statistic:read`, `report:manage-recipients`; Empfänger je Kategorie
  im Portal eintragen (Revisor, IT).
- **Personalnummer in Meldungen:** bleibt bei einer Löschung der Person stehen (die Meldung kennt nur die Nummer, keine interne ID); mit dem
  Datenschutzbeauftragten klären, ob anonymisiert werden soll.
- Rate Limits sind pro Instanz; bei mehreren Instanzen wäre ein geteilter Speicher nötig.
