# Makefile für Portal (Windows, GNU Make + PowerShell 7)
# Make installieren: winget install ezwinports.make
# Alle Rezepte laufen in pwsh, damit Compress-Archive & Co. ohne Git-Bash funktionieren.

SHELL := pwsh.exe
.SHELLFLAGS := -NoProfile -NonInteractive -Command

RESOURCE_GROUP ?= IRS
APP_NAME       ?= irs-api
PROJECT        := Portal.csproj
PUBLISH_DIR    := publish
ZIP            := app.zip

.DEFAULT_GOAL := help
.PHONY: help db-up db-down api-up api-down run build test migrate publish deploy

help:
	@@('Lokal:', '  make db-up          Postgres-Container starten', '  make db-down        Postgres-Container stoppen', '  make api-up         API als Docker-Container (Port 8000) bauen und starten', '  make api-down      API-Container stoppen', '  make migrate        Migrationen auf lokale DB anwenden', '  make run            API lokal starten', '  make build          Build', '  make test           Tests (Docker muss laufen)', '', 'Deployment:', '  make publish        dotnet publish + app.zip', '  make deploy         publish + az webapp deploy') | ForEach-Object { Write-Host $$_ }

# --- Lokal -------------------------------------------------------------------

db-up:
	docker compose up -d

db-down:
	docker compose down

api-up:
	docker compose --profile app up -d --build

api-down:
	docker compose --profile app down

run:
	dotnet run --project $(PROJECT)

build:
	dotnet build

test:
	dotnet test Portal.Tests

migrate:
	dotnet ef database update --project $(PROJECT)

# --- Deployment ----------------------------------------------------------------
# Kein Migrationsschritt: Schemaänderungen laufen bewusst von Hand (dotnet ef database update bzw. SQL-Skript),
# solange das Symfony-Backend als Rückfallebene dient.

publish:
	if (Test-Path $(PUBLISH_DIR)) { Remove-Item -Recurse -Force $(PUBLISH_DIR) }
	dotnet publish $(PROJECT) -c Release -o $(PUBLISH_DIR)
	if (Test-Path $(ZIP)) { Remove-Item -Force $(ZIP) }
	Compress-Archive -Path '$(PUBLISH_DIR)/*' -DestinationPath $(ZIP)

deploy: publish
	az webapp deploy --resource-group $(RESOURCE_GROUP) --name $(APP_NAME) --src-path $(ZIP) --type zip
