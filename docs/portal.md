# Portal API (`src/Portal`): FAQ, Meldungen, Statistik

Entscheidungen 41 und 43 (`CLAUDE.md`), überarbeitet 2026-11-10. Die Fachlogik stammt aus `Archiv/Portal` (früher `IRS-API`); das Angular-Frontend bleibt kompatibel: gleiche Pfade, Antwortform und Feldnamen.

## Antwortform

- Erfolg `{"success":true,"data":…}` (`201` beim Anlegen, `204` ohne Body bei Löschen/Archivieren); Fehler `{"success":false,"message":"…","errors":[{"field":"…","message":"…"}]}`.
- Validierung `422` (Feldnamen camelCase), `404` mit festem Text, `403` mit festem Text je Aktion, `401` nur für „nicht angemeldet“, `429` mit `Retry-After`. Enums klein geschrieben (`open`, `theft`, `critical`), JSON camelCase, FAQ-Position heißt `order`.

## Endpunkte

| Bereich | Pfad | Recht |
| --- | --- | --- |
| FAQ lesen (aktive) | `GET /api/faqs` | öffentlich |
| FAQ archiviert | `GET /api/faqs/archived` | `faq:read` |
| FAQ anlegen / ändern / Reihenfolge / löschen | `POST /api/faqs`, `PATCH /api/faqs/{id}`, `PATCH /api/faqs/reorder`, `DELETE /api/faqs/{id}` | `faq:create` / `faq:update` / `faq:update` / `faq:delete` |
| FAQ-Anhänge | `POST/DELETE /api/faqs/{id}/attachments…`, `GET …/{attachmentId}` | `faq:update`; ansehen nur angemeldet |
| FAQ-Bilder | `POST/GET /api/admin/faq/images`, `DELETE /api/admin/faq/images/{id}`; `GET /api/faqs/images/{id}` ansehen | `faq:create` / `faq:read` / `faq:delete`; ansehen öffentlich (die zufällige ID ist die Zugriffskontrolle) |
| Meldung anlegen | `POST /api/reports` | öffentlich, Rate Limit (10 je Minute und Client) |
| Filialen im Meldeformular | `GET /api/locations` | öffentlich; offene Filialen aus der BC-Kopie, Klasse 1 |
| Meldungen | `GET /api/reports`, `/archived`, `/{id}` | `report:read` (Reichweite: Filialen) |
| Meldung ändern / archivieren / Hauptmeldung lösen | `PATCH`, `DELETE /api/reports/{id}`, `DELETE …/parent` | `report:update` / `report:delete` / `report:update` |
| Statistik | `GET /api/statistics/{monthly,monthly-by-category,by-location-severity,by-personnel-number}` | `statistic:read` (Reichweite: Filialen) |
| Empfänger neuer Meldungen | `GET /api/report-recipients`, `PUT /api/report-recipients/{kategorie}` | `report:manage-recipients` |
| Änderungsprotokoll | `GET /api/audit` | `portal-audit:read` (sonst `404`) |

## Regeln

- **Reichweite:** Eine Meldung sieht, bearbeitet und zählt in der Statistik, wer **mindestens eine** ihrer Filialen erreicht. Außerhalb der Reichweite, aber vorhanden: `403`; nicht vorhanden: `404`. Wer nur einen Teil erreicht, ändert auch nur diesen Teil der Filialliste.
- **Filialen** kommen als Kopie aus `StoreChanged`; eine Meldung für eine unbekannte oder geschlossene Filiale ergibt `404`. Die Quelle (`source`) ist beim öffentlichen Anlegen immer `employee`. Eine **Hauptmeldung** setzt nur `PATCH` (`parentReportId`, mit Zyklusprüfung).
- **FAQ-Antworten** sind ein TipTap-/ProseMirror-Dokument (`content`, Entscheidung 43); `description` bleibt als abgeleitetes Markdown für alte Clients und die Suche. Das Backend bereinigt das Dokument über eine Whitelist (erlaubte Knoten und Marks, Hex-Farben, feste Schriftgrößen, Links nur http/https/mailto, Bilder nur aus der Bilderliste, Grenzen Tiefe 12 / 5000 Knoten / 50 000 Zeichen); Unzulässiges wird mit `422` auf das Feld `content` abgelehnt.
- **FAQ-Bilder:** PNG, JPEG, WebP (am Inhalt erkannt, höchstens 5 MB) als Datei unter `Portal:Faq:ImagePath` (außerhalb des Webroots), Name = zufällige UUID. Ein Bild in einer aktiven Antwort lässt sich nicht löschen (`409`). **Anhänge:** höchstens 20 je Eintrag, 10 MB je Datei (PDF, Bilder, Office), Typ aus dem Inhalt.
- **Benachrichtigung bei neuer Meldung** (ersetzt `report.created`): Wer benachrichtigt wird, hängt von der **Kategorie** ab (`virus`, `bug`, `theft`, `other`, `suggestion`) und ist von der Verwaltung einstellbar (z. B. Revisor bei `theft`, IT bei `bug` und `virus`). Das Portal sendet `SendNotification` (Vorlage `portal.report-created.handler`, InApp + generischer Push) in derselben Transaktion wie die Meldung. **Die Nachricht enthält nichts aus der Meldung** (Klasse 2), nur den Link `/reports/{id}`.
- **Löschung:** `EmployeeDataErasureRequested` entfernt Personenkopie, Berechtigungskopie und Empfängereinträge der Person.
- **Rate Limits** je Instanz im Speicher (`Portal:RateLimiting:RequestsPerMinute` 100, `ReportsPerMinute` 10); hinter dem Proxy zählt `X-Forwarded-For` (`ForwardedHeaders:KnownProxies`). CORS: `Cors:AllowedOrigins` und/oder `Cors:AllowedOriginPattern`.

## Anders als im Archiv

Neue Datenbank (Schema `portal`: `faqs`, `faq_images`, `reports`, `report_locations`, `report_recipients`); Zeiten ohne Zeitzone (UTC), `noticedAt` wie gesendet, Enums klein geschrieben in der Datenbank (für den späteren Import). Keine Benutzer, Rollen oder Filialen im Portal. Rechte lokal aus der Berechtigungskopie (`Shared.Authorization`), die Rechte meldet das Portal mit Beschreibung bei Access an.

## Offen

- **Altdaten übernehmen:** Schema der alten Datenbank folgt vom Projektverantwortlichen; danach Import von Meldungen (Filialzuordnung über die Filial-IDs der BC-Kopie), FAQ und Bilddateien.
- **Zweitmeinung** (eine Meldung außerhalb der eigenen Filialen teilen): nicht gebaut.
- **Einrichtung in Access:** Rollen mit den Rechten `faq:*`, `report:*`, `statistic:read`, `report:manage-recipients`; Empfänger je Kategorie im Portal eintragen.
- **Personalnummer in Meldungen** bleibt bei einer Löschung der Person stehen (die Meldung kennt nur die Nummer); mit dem Datenschutzbeauftragten klären, ob anonymisiert werden soll.
- Rate Limits gelten je Instanz; bei mehreren Instanzen wäre ein geteilter Speicher nötig.
