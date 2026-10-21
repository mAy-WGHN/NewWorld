# Alle Bestandsdienste lokal per Docker (macOS/Linux, POSIX-Shell). Voraussetzungen: Docker, dotnet SDK 10, dotnet-ef (Migrationen).
# Die Makefiles unter Archiv/ (Windows/PowerShell) bleiben für den Einzelbetrieb unverändert.

POSTGRES_PORT ?= 5440
COMPOSE := docker compose

.DEFAULT_GOAL := help
.PHONY: help env up down restart build db-up migrate apps-up ps logs health reset

help:
	@echo "make up        Alles starten: .env anlegen, Postgres, Migrationen, alle Dienste (baut die Images)"
	@echo "make down      Alle Container stoppen (Daten bleiben)"
	@echo "make reset     Container stoppen und Datenbanken/Uploads LÖSCHEN"
	@echo "make restart   down + up"
	@echo "make build     Images neu bauen"
	@echo "make ps        Status der Container"
	@echo "make logs      Logs folgen (S=<dienst> für einen Dienst, z. B. S=portal)"
	@echo "make health    /health aller Dienste abfragen"
	@echo "make migrate   EF-Migrationen auf die Compose-Datenbanken anwenden (Postgres muss laufen)"
	@echo "make env       Lokale .env mit Dev-Schlüsseln erzeugen (nur falls sie fehlt)"
	@echo ""
	@echo "Ports: Portal 8000, Rollen-Rechte 8100, Benachrichtigungen 8200, hr-tool-API 8300, BC-Anbindung 8400, Postgres $(POSTGRES_PORT)"

env:
	@./scripts/dev-env.sh

db-up: env
	$(COMPOSE) up -d --wait postgres

# dotnet ef läuft auf dem Host gegen den veröffentlichten Postgres-Port (wie "make migrate" in den Archiv-Ordnern).
# Der Host der Dienste wird dabei gebaut und verlangt Pflicht-Konfiguration (z. B. JWT-Schlüssel): sie kommt aus der .env.
EF = set -a && . ./.env && set +a && Jwt__Key="$$JWT_KEY" Auth__PinEncryptionKey="$$PIN_ENCRYPTION_KEY" dotnet ef database update

migrate: db-up
	$(EF) --project Archiv/Portal/Portal.csproj --connection "Host=localhost;Port=$(POSTGRES_PORT);Database=irs;Username=irs;Password=irs_dev_only"
	$(EF) --project Archiv/Rollen-Rechte/Rollen-Rechte.csproj --connection "Host=localhost;Port=$(POSTGRES_PORT);Database=access;Username=access;Password=access_dev_only"
	$(EF) --project Archiv/Benachrichtigungen/Benachrichtigungen.csproj --connection "Host=localhost;Port=$(POSTGRES_PORT);Database=notifications;Username=notifications;Password=notifications_dev_only"
	$(EF) --project Archiv/hr-tool-API/HrTool-API.csproj --connection "Host=localhost;Port=$(POSTGRES_PORT);Database=hrtool;Username=hrtool;Password=hrtool_dev_only"

apps-up: env
	$(COMPOSE) up -d --build

up: migrate apps-up
	@echo ""
	@$(MAKE) --no-print-directory health

down:
	$(COMPOSE) down

reset:
	$(COMPOSE) down -v

restart: down up

build: env
	$(COMPOSE) build

ps:
	$(COMPOSE) ps

logs:
	$(COMPOSE) logs -f $(S)

health:
	@for s in portal:8000 rollen-rechte:8100 benachrichtigungen:8200 hr-tool-api:8300 bc-anbindung:8400; do \
	  name=$${s%%:*}; port=$${s##*:}; \
	  code=$$(curl -s -o /dev/null -w '%{http_code}' --max-time 3 http://localhost:$$port/health || true); \
	  printf '%-20s http://localhost:%s/health -> %s\n' $$name $$port "$$code"; \
	done
