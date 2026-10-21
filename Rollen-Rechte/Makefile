# Makefile für Rollen-Rechte (Windows, GNU Make + PowerShell 7)
# Make installieren: winget install ezwinports.make
# Alle Rezepte laufen in pwsh.

SHELL := pwsh.exe
.SHELLFLAGS := -NoProfile -NonInteractive -Command

RESOURCE_GROUP ?= IRS
APP_NAME       ?= rollen-rechte
PROJECT        := Rollen-Rechte.csproj
PUBLISH_DIR    := publish
ZIP            := app.zip

.DEFAULT_GOAL := help
.PHONY: help db-up db-down api-up api-down run build test migrate create-user assign-role sync-stores decrypt-pin publish deploy

help:
	@@('Lokal:', '  make db-up          Postgres-Container starten (Port 5433)', '  make db-down        Postgres-Container stoppen', '  make api-up         Dienst als Docker-Container (Port 8100) bauen und starten', '  make api-down       Dienst-Container stoppen', '  make migrate        Migrationen auf die lokale DB anwenden', '  make run            Dienst lokal starten (Port 5302)', '  make build          Build', '  make test           Tests (Docker muss laufen)', '  make create-user NAME=<name>   Administrator anlegen (Passwort wird einmalig angezeigt)', '  make assign-role NAME=<name> [ROLE=<rolle>]   Rolle zuweisen (Standard: Administrator)', '  make sync-stores    Filialen über die BC-Anbindung abgleichen', '  make decrypt-pin NAME=<personalnr>   gespeichertes BC-PIN anzeigen', '', 'Deployment:', '  make publish        dotnet publish + app.zip', '  make deploy         publish + az webapp deploy') | ForEach-Object { Write-Host $$_ }

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
	dotnet test Rollen-Rechte.Tests

migrate:
	dotnet ef database update --project $(PROJECT)

create-user:
	dotnet run --project $(PROJECT) -- create-user $(NAME)

# Weist einem User eine Rolle zu (ohne ROLE=: "Administrator"). User ohne Rolle haben keine Rechte.
# Beispiel:  make assign-role NAME=11207 ROLE=Administrator
assign-role:
	dotnet run --project $(PROJECT) -- assign-role $(NAME) $(ROLE)

sync-stores:
	dotnet run --project $(PROJECT) -- sync-stores

# Zeigt das verschlüsselt gespeicherte BC-PIN eines Users im Klartext (braucht Auth__PinEncryptionKey aus der .env).
# Beispiel:  make decrypt-pin NAME=11207
decrypt-pin:
	dotnet run --project $(PROJECT) -- decrypt-pin $(NAME)

# --- Deployment ----------------------------------------------------------------
# Kein Migrationsschritt: Schemaänderungen laufen bewusst von Hand (dotnet ef database update bzw. SQL-Skript).

publish:
	if (Test-Path $(PUBLISH_DIR)) { Remove-Item -Recurse -Force $(PUBLISH_DIR) }
	dotnet publish $(PROJECT) -c Release -o $(PUBLISH_DIR)
	if (Test-Path $(ZIP)) { Remove-Item -Force $(ZIP) }
	Compress-Archive -Path '$(PUBLISH_DIR)/*' -DestinationPath $(ZIP)

deploy: publish
	az webapp deploy --resource-group $(RESOURCE_GROUP) --name $(APP_NAME) --src-path $(ZIP) --type zip
