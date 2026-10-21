# NewWorld

## Lokal alles starten (Bestandsdienste)

Voraussetzungen: Docker, .NET SDK 10, `dotnet-ef`.

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
- Business Central: echte Zugangsdaten stehen weiterhin in `BC-Anbindung/.env` (wird optional eingelesen). Ohne sie antworten die BC-Endpunkte mit 503, der Rest läuft.
- Die Einzel-`docker-compose.yaml` und Makefiles der Dienste bleiben unverändert (Einzelbetrieb, Windows/PowerShell).
- Die neuen Gerüste unter `src/` (Aspire AppHost) sind davon getrennt.
