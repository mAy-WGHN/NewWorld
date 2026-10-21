#!/bin/sh
# Erzeugt die lokale .env im Repo-Root (gitignored) mit zufälligen Dev-Schlüsseln, falls sie noch fehlt.
# Nur für lokale Tests mit docker compose. Echte BC-Zugangsdaten stehen weiterhin in Archiv/BC-Anbindung/.env.
set -eu
cd "$(dirname "$0")/.."

if [ -f .env ]; then
  echo ".env existiert bereits, nichts geändert."
  exit 0
fi

rand() { openssl rand -base64 32; }
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }

# VAPID (P-256) für Web Push: öffentlicher Schlüssel = unkomprimierter Punkt (65 Byte), privater = 32 Byte, base64url.
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
openssl ecparam -name prime256v1 -genkey -noout -out "$tmp" 2>/dev/null
vapid_private="$(openssl ec -in "$tmp" -outform DER 2>/dev/null | tail -c +8 | head -c 32 | b64url)"
vapid_public="$(openssl ec -in "$tmp" -pubout -outform DER 2>/dev/null | tail -c 65 | b64url)"

admin_password="$(openssl rand -base64 18 | tr -d '/+=' | head -c 20)"

umask 077
cat > .env <<ENV
# Lokale Dev-Werte für docker compose (erzeugt von scripts/dev-env.sh). Nie committen, nie in Produktion verwenden.
JWT_KEY=dev-only-key-for-local-use-0123456789abcdef0123456789
PIN_ENCRYPTION_KEY=$(rand)
BC_KEY_IDENTITY=$(rand)
BC_KEY_PORTAL=$(rand)
BC_KEY_HR=$(rand)
APPS_KEY_PORTAL=$(rand)
APPS_KEY_HR=$(rand)
APPS_KEY_NOTIFICATIONS=$(rand)
SRC_KEY_PORTAL=$(rand)
SRC_KEY_HR=$(rand)
VAPID_PUBLIC_KEY=${vapid_public}
VAPID_PRIVATE_KEY=${vapid_private}
SEED_ADMIN_USERNAME=admin
SEED_ADMIN_PASSWORD=${admin_password}
ENV
echo ".env erzeugt. Admin-Login für Rollen-Rechte: admin / ${admin_password}"
