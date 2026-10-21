# NewWorld

Backend des Mitarbeiterportals: neue Dienste nach dem Umsetzungsplan (`src/`) und die bisherigen Dienste als Referenz (`Archiv/`).
Regeln und Entscheidungen: `CLAUDE.md`; Plan: `Umsetzungsplan Mitarbeiterportal – Backend-Architektur.md`.

## Struktur

```
src/
  AppHost/            .NET Aspire: startet PostgreSQL, Service-Bus-Emulator, WireMock (BC-Ersatz) und alle Dienste
  ServiceDefaults/    OpenTelemetry, Health Checks (/health/live, /health/ready), Resilience
  Shared/
    Shared.Messaging        Nachrichten-Basis (IMessage, Sensitivity), paketfrei
    Shared.Approvals        Genehmigungs-Statusmaschine, paketfrei
    Shared.Authorization    Claims-Mapping, Policies, Scope-Prüfung, Berechtigungs-Cache, Token-Handler
    Shared.Infrastructure   Outbox/Inbox, Service-Bus-Transport, EF-Mappings, DB-Migration (nur für *.Infrastructure)
    Shared.Testing          Testcontainers-Fixtures (PostgreSQL, Service Bus), Test-Auth, FakeTimeProvider
  BcIntegration/ Access/ Notifications/ Workforce/ Portal/
    <Dienst>.Api | .Core | .Infrastructure | .Contracts
tests/                Unit-, Integrations-, Architektur- und E2E-Tests
Archiv/               Bestandsdienste (BC-Anbindung, Rollen-Rechte, Benachrichtigungen, Portal, hr-tool-API), unverändert
docs/                 Phasenberichte
```

## Neue Dienste (`src/`)

Voraussetzungen: .NET SDK 10, Docker.

```bash
make test                         # dotnet build + dotnet test der gesamten Solution (inkl. E2E über Aspire)
dotnet run --project src/AppHost  # alles starten, Aspire-Dashboard im Browser
```

Beim ersten `dotnet run` des AppHost bietet Aspire an, ein lokales Entwicklerzertifikat als vertrauenswürdig einzustufen (macOS fragt nach dem Passwort). Die Tests brauchen das nicht.

## Archiv-Dienste gemeinsam starten

Zusätzlich nötig: `dotnet-ef` (für die Migrationen).

```bash
make up       # .env mit Dev-Schlüsseln anlegen, Postgres, Migrationen, alle Dienste bauen und starten
make health   # /health aller Dienste
make down     # stoppen (Daten bleiben), make reset löscht auch die Datenbanken
make help     # alle Ziele
```

| Dienst | URL |
| --- | --- |
| Portal | http://localhost:8000 |
| Rollen-Rechte | http://localhost:8100 |
| Benachrichtigungen | http://localhost:8200 |
| hr-tool-API | http://localhost:8300 |
| BC-Anbindung | http://localhost:8400 |
| PostgreSQL (eine Instanz, je Dienst eigene Datenbank) | localhost:5440 |

- `make up` erzeugt beim ersten Mal die gitignorierte `.env` (zufällige Dev-Schlüssel, Admin-Login für Rollen-Rechte `admin` plus Passwort in der `.env`).
- Business Central: echte Zugangsdaten stehen weiterhin in `Archiv/BC-Anbindung/.env` (wird optional eingelesen). Ohne sie antworten die BC-Endpunkte mit 503, der Rest läuft.
- Die Einzel-`docker-compose.yaml` und Makefiles unter `Archiv/` bleiben unverändert (Einzelbetrieb, Windows/PowerShell).
