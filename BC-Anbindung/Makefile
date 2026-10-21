# Makefile für BC-Anbindung (Windows, GNU Make + PowerShell 7)
# Make installieren: winget install ezwinports.make
# Alle Rezepte laufen in pwsh.

SHELL := pwsh.exe
.SHELLFLAGS := -NoProfile -NonInteractive -Command

RESOURCE_GROUP ?= IRS
APP_NAME       ?= bc-anbindung
PROJECT        := BC-Anbindung.csproj
PUBLISH_DIR    := publish
ZIP            := app.zip

.DEFAULT_GOAL := help
.PHONY: help run build test publish deploy

help:
	@@('Lokal:', '  make run            Dienst lokal starten (Port 5301)', '  make build          Build', '  make test           Tests (kein Docker nötig, BC wird durch einen Fake ersetzt)', '', 'Deployment:', '  make publish        dotnet publish + app.zip', '  make deploy         publish + az webapp deploy') | ForEach-Object { Write-Host $$_ }

run:
	dotnet run --project $(PROJECT)

build:
	dotnet build

test:
	dotnet test BC-Anbindung.Tests

publish:
	if (Test-Path $(PUBLISH_DIR)) { Remove-Item -Recurse -Force $(PUBLISH_DIR) }
	dotnet publish $(PROJECT) -c Release -o $(PUBLISH_DIR)
	if (Test-Path $(ZIP)) { Remove-Item -Force $(ZIP) }
	Compress-Archive -Path '$(PUBLISH_DIR)/*' -DestinationPath $(ZIP)

deploy: publish
	az webapp deploy --resource-group $(RESOURCE_GROUP) --name $(APP_NAME) --src-path $(ZIP) --type zip
