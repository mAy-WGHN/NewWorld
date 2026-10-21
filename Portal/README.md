# Portal

Das Portal des Unternehmensportals: Meldungen, Statistik und FAQ (ASP.NET 10). Anmeldung, Rechte und Filialen liefert **Rollen-Rechte**, Benachrichtigungen **Benachrichtigungen**.
Konventionen, Konfiguration und Ablauf: [CLAUDE.md](CLAUDE.md).

```
copy .env.example .env      # Werte eintragen (Rollen-Rechte und Benachrichtigungen müssen laufen)
make db-up                  # Postgres auf Port 5432
make migrate
make run                    # http://localhost:5159
make test                   # Docker muss laufen
```
