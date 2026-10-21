# Rollen-Rechte

Anmeldung, Benutzer, Rollen, Rechte, Scopes und Filialen für das Unternehmensportal. Die anderen Anwendungen (Portal, HR-Tool) melden hier
ihren Rechtekatalog an und lösen die Rechte ihrer User auf. Details, Konfiguration und Konventionen: [CLAUDE.md](CLAUDE.md).

```
copy .env.example .env      # Werte eintragen
make db-up                  # Postgres auf Port 5433
make migrate
make create-user NAME=admin
make run                    # http://localhost:5302
make test                   # Docker muss laufen
```
# irs-authorization-authentication
