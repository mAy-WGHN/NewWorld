# NewWorld Frontend

Das globale Frontend des Mitarbeiterportals: ein Angular-Rahmen (Version 22, Standalone-Komponenten, Signals, zoneless) mit Anmeldung und einer Navigation, die sich nach den Rechten der angemeldeten Person richtet. Die Fachbereiche (Abwesenheiten, Arbeitszeitkonto, Mitarbeiterkarte, FAQ, Meldungen, Verwaltung …) sind als Platzhalterseiten angelegt und werden nacheinander gebaut.

## Starten

```bash
cd Frontend
npm install
npm start          # http://localhost:4200
```

Voraussetzung sind die Backend-Dienste (zum Beispiel über den Aspire-AppHost). Der Dev-Server leitet Aufrufe über [proxy.conf.json](proxy.conf.json) weiter:

| Pfad im Browser | Dienst | Standard-Ziel |
| --- | --- | --- |
| `/access/*` | Access (Login, Rechte) | `http://localhost:5101` |
| `/workforce/*` | Workforce | `http://localhost:5102` |
| `/portal/*` | Portal | `http://localhost:5103` |
| `/notifications/*` | Notifications | `http://localhost:5104` |

Aspire vergibt die Ports dynamisch: die Ziele in `proxy.conf.json` auf die Adressen aus dem Aspire-Dashboard anpassen. Der Proxy entfernt das Präfix, die Dienste sehen ihre normalen Pfade (`/api/...`). Die Dienste brauchen so kein CORS.

## Konfiguration im Betrieb

[public/config.json](public/config.json) wird beim Start geladen und legt fest, unter welchem Pfad der Browser jeden Dienst erreicht (zum Beispiel hinter einem Reverse Proxy). So läuft derselbe Build in jeder Umgebung.

## Anmeldung

- Personalnummer und BC-PIN gehen an `POST /api/auth/login` von Access. Die PIN wird nirgends gespeichert und nach jedem Versuch aus dem Formular entfernt.
- Das Access-Token liegt nur im Speicher. Das rotierende Refresh-Token liegt im `sessionStorage` des Tabs, damit ein Neuladen die Sitzung behält. Beim Schließen des Tabs endet sie.
- Der Interceptor hängt das Token an Aufrufe der eigenen Dienste, erneuert bei `401` einmal über `/api/auth/refresh` und wiederholt den Aufruf. Parallele Erneuerungen teilen sich einen Aufruf, weil das Refresh-Token nur einmal gilt. Schlägt die Erneuerung fehl, geht es zur Anmeldung zurück.
- Die Rechte kommen von `GET /api/me/privileges` (Access löst Wildcards schon auf, das Frontend vergleicht Schlüssel exakt).

Die Rechteprüfung im Frontend steuert nur, was angezeigt wird. Durchgesetzt wird sie von den Diensten bei jedem Aufruf.

## Aufbau

```
src/app/
  core/            Konfiguration, Auth (Service, Interceptor, Guards, Rechte), Navigation (nav-items.ts)
  layout/          Rahmen mit Kopfzeile und Navigation
  pages/           Anmeldung, Startseite, Platzhalter
```

Neuer Bereich: Eintrag in [src/app/core/nav/nav-items.ts](src/app/core/nav/nav-items.ts) (Pfad, nötige Rechte), die Route dazu entsteht aus dem Eintrag. Soll der Bereich echte Inhalte bekommen, ersetzt eine eigene Route in `app.routes.ts` die Platzhalterseite.

## Tests und Build

```bash
npm test -- --watch=false   # Vitest
npm run build               # dist/newworld-frontend
```
