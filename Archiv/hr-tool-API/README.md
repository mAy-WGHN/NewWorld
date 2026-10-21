# HrTool-API

ASP.NET-Core-Web-API für das HR-Tool. Aufbau und Anbindung folgen der **Portal** (Portal): Anmeldung, Rollen, Rechte, Scopes und Filialen kommen von
**Rollen-Rechte**, Business Central ist nur über **BC-Anbindung** erreichbar, Benachrichtigungen laufen über **Benachrichtigungen**. Details und Konventionen: `CLAUDE.md`.

## Techstack

- .NET 10, ASP.NET Core (Controller-basiert), Nullable + ImplicitUsings
- EF Core 10 mit PostgreSQL (Npgsql, snake_case), JWT-Prüfung (Token von Rollen-Rechte), OpenAPI (nur Development)
- Tests: xUnit, `WebApplicationFactory`, Fakes für die Nachbardienste
- Lokale DB: Postgres 17 per `docker compose` (Port 5435)

## Ordnerstruktur

| Ordner | Inhalt |
|---|---|
| `Authentication/` | JWT-Prüfung, Rechteprüfung gegen Rollen-Rechte, Rechtekatalog und dessen Anmeldung, Scope |
| `Common/` | Response-Envelope, Fehlerbehandlung, Validierung, Rate-Limiting |
| `Workforce/` | Abwesenheiten (`Absences/`), Dienstplanung (`ShiftPlanning/`), Arbeitsorte (`Workplaces/`): Entities, Controller, DTOs |
| `Hr/` | Mitarbeiterstamm (`Employees/`): Entities, Controller, DTOs |
| `Contracts/` | bereichsübergreifende DTOs |
| `Controllers/` | `PingController` |
| `Data/` | `HrToolDbContext`, später Migrationen |
| `Domain/Entities` | gemeinsames Interface `IHasUpdatedAt` |
| `Serialization/` | zentrale JSON-Konfiguration |
| `Services/` | Fachlogik und Clients der anderen Dienste (Filialverzeichnis) |
| `docs/` | Dokumentation |
| `HrTool-API.Tests/` | Tests (`Functional/`, `Unit/`, `Infrastructure/`) |

## Konfiguration

`.env.example` nach `.env` kopieren. Für geschützte Routen braucht es den öffentlichen JWT-Schlüssel (`Jwt__PublicKey`, lokal reicht `Jwt__Key` aus `appsettings.Development.json`)
und Rollen-Rechte (`Access__BaseUrl`, `Access__ApiKey` = dort `Apps__Keys__hr`).

## Befehle

```bash
make db-up      # Postgres starten
make run        # API starten (http://localhost:5129)
make test       # Tests (Docker muss laufen)
```

Erreichbarkeit: `GET /api/ping`, Health-Check: `GET /health`.
