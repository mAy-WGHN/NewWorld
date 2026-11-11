# know-how.md

Kurze Vorgehensweisen für den Alltag. Details zu den Entscheidungen stehen in `CLAUDE.md`.

## Lokal starten

Voraussetzung: Docker Desktop läuft, .NET-10-SDK und Node/npm sind installiert.

```bash
cd src/AppHost && dotnet run     # Backend: Container + fünf Dienste (kein docker compose nötig)
cd Frontend && npm start         # Frontend (beim ersten Mal vorher: npm install), http://localhost:4200
```

- **Einmalig auf jedem Rechner:** Postgres-Passwort in die User Secrets (ohne startet Postgres nicht).
  Das Passwort darf sich danach nicht mehr ändern, es gehört zum Docker-Volume.

  ```bash
  dotnet user-secrets set "Parameters:postgres-password" "<Passwort>" --id newworld-apphost
  ```

- **Testnutzer** (PIN immer `4711`): `10099` Administrator, `10044` Personalabteilung, `10042` normale Mitarbeiterin (Filiale F017).
- **Feste Ports** 5101 (Access), 5102 (Workforce), 5103 (Portal), 5104 (Notifications); `Frontend/proxy.conf.json` zeigt darauf.
- **Wartezeit:** Der erste Start dauert 3 bis 10 Minuten (Images, Datenbanken anlegen). Bereit heißt: alle vier Ports antworten auf `/health/ready` mit 200.
- **Beenden:** Strg+C im Fenster des AppHost. Die Daten bleiben im Volume `newworld-postgres-data-local`.
- **Von vorn anfangen:** AppHost stoppen, dann `docker volume rm newworld-postgres-data-local` (vorher die gestoppten Postgres-Container entfernen, falls das Volume „in use“ meldet).
- Der Urlaubsanspruch fehlt nach einem frischen Start. Er lässt sich nur per API setzen: `PUT /api/employees/{id}/vacation-entitlements/{year}` mit `{"days":30}` (Recht `absence:manage-entitlement`, z. B. als 10044).

## Login hängt / Backend läuft nicht: Fehlersuche

1. **Läuft das Backend?** Das klärt fast alles:

   ```bash
   curl -i http://localhost:5101/health/ready      # ebenso 5102, 5103, 5104; erwartet: 200
   ```

2. **Browser** (F12, Netzwerk): `POST /svc/access/api/auth/login` hängt = Backend fehlt, `502`/`503` = Dienst startet noch, `401` = falsche PIN.
3. **Aspire-Dashboard:** `dotnet run` druckt die Adresse (`https://localhost:…/login?t=…`). „Waiting“ = wartet auf eine Abhängigkeit, „Finished“ = Dienst abgestürzt.
4. **Log eines Dienstes:**

   ```bash
   dotnet dnx --yes aspire.cli@13.6.2 -- logs access-api
   ```

5. **Postgres-Log:**

   ```bash
   docker logs --tail 30 $(docker ps -q --filter name=postgres)
   ```

6. **Einzelnen Dienst neu starten** (z. B. wenn er sich beim Start beendet hat):

   ```bash
   dotnet dnx --yes aspire.cli@13.6.2 -- resource workforce-api restart
   ```

7. **Debugger:** AppHost laufen lassen und in der IDE an den Prozess des Dienstes anhängen (`Access.Api.exe` usw.). Einzeln starten geht nur mit der Verdrahtung des AppHost.

## Bekannte Stolpersteine

| Symptom | Ursache | Lösung |
| --- | --- | --- |
| Dienste bleiben in „Waiting“, Postgres-Log: `password authentication failed` | Passwort in den User Secrets passt nicht zum Volume (anderes Profil, gelöscht, neu erzeugt) | Altes Passwort wieder setzen (Befehl oben) oder Volume löschen (Daten weg) |
| Ressource `postgres-password` steht auf `ValueMissing`, Postgres startet nicht | Secret fehlt | `dotnet user-secrets set …` (siehe oben) |
| Ein Dienst, meist Workforce, ist „Finished“, Log: `Database migration failed` | Datenbank war beim Start noch nicht bereit | Nur diesen Dienst neu starten (Punkt 6) |
| Menü leer, „Für dieses Jahr ist kein Urlaubsanspruch hinterlegt“ | Datenbank neu (z. B. Volume gelöscht) oder veraltete Browser-Sitzung | Abmelden und neu anmelden; Anspruch neu setzen |
| `dotnet build`/`dotnet test` meldet gesperrte DLLs | AppHost und Dienste laufen noch | AppHost beenden, ggf. übrig gebliebene `*.Api`-Prozesse beenden |
| Container laufen nach hartem Beenden weiter | AppHost wurde nicht mit Strg+C beendet | Nur die Projekt-Container stoppen (`bc-mock`, `postgres`, `clamav`, `servicebus*`), nie pauschal alle |

## Mehrere Instanzen, Proxy, Migrationen (Betrieb)

- **Hintergrunddienste** (Stammdatenabgleich in BC Integration, Wartung der AU und des Arbeitszeitkontos in Workforce) laufen je Runde nur auf einer Instanz: `IDistributedLock` (`Shared.Infrastructure/Persistence`) auf einer PostgreSQL-Sitzungssperre der Dienst-Datenbank, ohne Tabelle und ohne Migration. Wer die Sperre nicht bekommt, überspringt die Runde (Log auf Debug); stirbt der Halter, gibt Postgres sie frei. Der Outbox-Dispatcher braucht keine Sperre: mehrfach gesendete Nachrichten erkennt der Empfänger (Inbox).
- **Proxy:** `ForwardedHeaders:KnownProxies` (Adressen) und/oder `ForwardedHeaders:KnownNetworks` (CIDR) legen fest, von wem `X-Forwarded-*` geglaubt wird. Ohne Konfiguration geschieht in Access nichts (alle Aufrufer teilen sich sonst die Adresse des Proxys, das Login-Limit je Client wird dann zu grob: **in Produktion setzen**); das Portal vertraut ohne Konfiguration jedem Proxy (wie bisher, wegen des Limits der öffentlichen Formulare).
- **Migrationen in Produktion:** nicht beim Start, sondern als Schritt der Auslieferung: `dotnet <Dienst>.Api.dll --migrate` wendet die Migrationen an und beendet das Programm (Fehler → Exit-Code ungleich 0, der Schritt schlägt fehl). Development und Testing migrieren weiter beim Start.
- **Azure prüfen:** `infra/Test-ServiceBusDeployment.ps1` (what-if, Deployment in eine Wegwerf-Resource-Group, Abgleich mit der Emulator-Topologie, keine Shared Access Keys, danach löschen). Braucht `az login` und Rechte für Rollenzuweisungen; noch nicht gegen echtes Azure gelaufen.
- **AU:** ClamAV-Vorlage und Prüfliste in `infra/clamav/`, Schlüsselrotation (Schlüsselring) in `docs/krankmeldung-au.md`.

## Rollen und Rechte

- Referenzdatei `docs/rollen.json` (Rollen „Public User“ und „Personalabteilung“). Wer ein Recht anlegt oder ändert, passt sie im selben Commit an. Ein Architekturtest prüft, dass jedes Recht dort existiert.
- Import in der Oberfläche: Bereich „Rollen“ → Datei vergleichen → Ausgewählte übernehmen.
- Lokal richtet der AppHost die Rollen und Ben (10044) als HR-Person ein (Seed, nur lokal).

## Tests

```bash
dotnet test NewWorld.slnx                          # gesamte Solution (AppHost und Container müssen vorher beendet sein)
cd Frontend && npx ng test --watch=false           # Frontend
```

- Die AppHost-Tests starten die Landschaft selbst, mit freien Ports und leerer Datenbank (`--Ports:Fixed=false --Seed:Development=false --Volumes:Persistent=false`).
- Die Integrationstests brauchen Docker (Testcontainers: Postgres 17, Service-Bus-Emulator 2.0.1).

## Docker-Images (lokal)

| Image | Version | Wofür |
| --- | --- | --- |
| `postgres` | 18.3 | Datenbanken der neuen Dienste |
| `servicebus-emulator` | 2.0.0 | Ersatz für Azure Service Bus |
| `mssql/server` | 2022-latest | Datenbank des Service-Bus-Emulators |
| `sheyenrath/wiremock.net` | 2.19.0 | BC-Mock (Business Central) |
| `clamav/clamav` | 1.4 | Virenscan der hochgeladenen AU |

`docker-compose.yaml` und `make up` gehören nur zu den Archiv-Diensten unter `Archiv/`.
