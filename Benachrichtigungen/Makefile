# Makefile für Benachrichtigungen (Windows, GNU Make + PowerShell 7)
# Make installieren: winget install ezwinports.make
# Alle Rezepte laufen in pwsh.

SHELL := pwsh.exe
.SHELLFLAGS := -NoProfile -NonInteractive -Command

RESOURCE_GROUP ?= IRS
APP_NAME       ?= benachrichtigungen
PROJECT        := Benachrichtigungen.csproj
PUBLISH_DIR    := publish
ZIP            := app.zip

.DEFAULT_GOAL := help
.PHONY: help db-up db-down api-up api-down run build test migrate publish deploy

help:
	@@('Lokal:', '  make db-up          Postgres-Container starten (Port 5434)', '  make db-down        Postgres-Container stoppen', '  make api-up        Dienst als Docker-Container (Port 8200) bauen und starten', '  make api-down      Dienst-Container stoppen', '  make migrate        Migrationen auf die lokale DB anwenden', '  make run            Dienst lokal starten (Port 5303)', '  make build          Build', '  make test           Tests (Docker muss laufen)', '', 'Deployment:', '  make publish        dotnet publish + app.zip', '  make deploy         publish + az webapp deploy') | ForEach-Object { Write-Host $$_ }

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
	dotnet test Benachrichtigungen.Tests

migrate:
	dotnet ef database update --project $(PROJECT)

# --- Deployment ----------------------------------------------------------------
# Kein Migrationsschritt: Schemaänderungen laufen bewusst von Hand (dotnet ef database update bzw. SQL-Skript).

publish:
	if (Test-Path $(PUBLISH_DIR)) { Remove-Item -Recurse -Force $(PUBLISH_DIR) }
	dotnet publish $(PROJECT) -c Release -o $(PUBLISH_DIR)
	if (Test-Path $(ZIP)) { Remove-Item -Force $(ZIP) }
	Compress-Archive -Path '$(PUBLISH_DIR)/*' -DestinationPath $(ZIP)

deploy: publish
	az webapp deploy --resource-group $(RESOURCE_GROUP) --name $(APP_NAME) --src-path $(ZIP) --type zip