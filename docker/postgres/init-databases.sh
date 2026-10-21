#!/bin/sh
# Legt je Bestandsdienst eine eigene Datenbank mit eigenem Benutzer an (nur lokal; Dev-Passwörter wie in den
# Einzel-Compose-Dateien der Dienste). Läuft nur beim ersten Start eines leeren Datenvolumens.
set -eu

create() {
  db="$1"; user="$2"; password="$3"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<SQL
CREATE ROLE ${user} LOGIN PASSWORD '${password}';
CREATE DATABASE ${db} OWNER ${user};
SQL
}

create irs           irs           irs_dev_only            # Portal
create access        access        access_dev_only         # Rollen-Rechte
create notifications notifications notifications_dev_only  # Benachrichtigungen
create hrtool        hrtool        hrtool_dev_only         # hr-tool-API
