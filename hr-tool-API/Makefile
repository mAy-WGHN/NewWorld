# Makefile für HrTool-API (Windows, GNU Make + PowerShell 7)
# Make installieren: winget install ezwinports.make
# Alle Rezepte laufen in pwsh, damit Compress-Archive & Co. ohne Git-Bash funktionieren.

SHELL := pwsh.exe
.SHELLFLAGS := -NoProfile -NonInteractive -Command

RESOURCE_GROUP ?= IRS
APP_NAME       ?= hr-tool-api
PROJECT        := HrTool-API.csproj
PUBLISH_DIR    := publish
ZIP            := app.zip

.DEFAULT_GOAL := help
.PHONY: help db-up db-down api-up api-down run build test migrate import publish deploy

help:
	@@('Lokal:', '  make db-up          Postgres-Container starten (Port 5435)', '  make db-down        Postgres-Container stoppen', '  make api-up         API als Docker-Container (Port 8300) bauen und starten', '  make api-down       API-Container stoppen', '  make migrate        Migrationen auf lokale DB anwenden', '  make import         BC-Exporte (docs/spreadsheets) importieren (DIR=<ordner> ARGS=--dry-run)', '  make run            API lokal starten (Port 5129)', '  make build          Build', '  make test           Tests (Docker muss laufen)', '', 'Deployment:', '  make publish        dotnet publish + app.zip', '  make deploy         publish + az webapp deploy') | ForEach-Object { Write-Host $$_ }

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
	dotnet test HrTool-API.Tests

migrate:
	dotnet ef database update --project $(PROJECT)

# Liest die BC-Exporte (.xlsx) in die Datenbank, Ordner standardmäßig docs/spreadsheets (nicht in Git). Probelauf: make import ARGS=--dry-run
import:
	dotnet run --project $(PROJECT) -- import $(DIR) $(ARGS)

# --- Deployment ----------------------------------------------------------------
# Kein Migrationsschritt: Schemaänderungen laufen bewusst von Hand (dotnet ef migrations script bzw. database update).

publish:
	if (Test-Path $(PUBLISH_DIR)) { Remove-Item -Recurse -Force $(PUBLISH_DIR) }
	dotnet publish $(PROJECT) -c Release -o $(PUBLISH_DIR)
	if (Test-Path $(ZIP)) { Remove-Item -Force $(ZIP) }
	Compress-Archive -Path '$(PUBLISH_DIR)/*' -DestinationPath $(ZIP)

deploy: publish
	az webapp deploy --resource-group $(RESOURCE_GROUP) --name $(APP_NAME) --src-path $(ZIP) --type zip
