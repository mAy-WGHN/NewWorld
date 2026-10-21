# Benachrichtigungen

Zentraler Dienst für Benachrichtigungen des Unternehmensportals: Die Anwendungen melden Ereignisse (`POST /api/events`), dieser Dienst bestimmt die Empfänger
(über „Rollen-Rechte“ und ihre Einstellungen) und stellt per Web Push zu. Details, Konfiguration und Konventionen: [CLAUDE.md](CLAUDE.md).

```
copy .env.example .env      # Werte eintragen
make db-up                  # Postgres auf Port 5434
make migrate
make run                    # http://localhost:5303
make test                   # Docker muss laufen
```
# irs-notifications
