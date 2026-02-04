#!/usr/bin/env bash
# Step 1: Full sync – copy all current data from Azure Table Storage to Cosmos DB Table API.
# Generates config from config/migrationsettings.full-sync.template.json and .env (same as backfill).

set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXAMPLE_DIR="$(dirname "$SCRIPT_DIR")"
CONFIG_DIR="$EXAMPLE_DIR/config"
cd "$EXAMPLE_DIR"

if [[ ! -f ".env" ]]; then
  echo "Missing .env. Copy .env.example to .env and set STORAGE_CONNECTION_STRING and COSMOS_CONNECTION_STRING."
  exit 1
fi
set -a
source .env
set +a

# Generate config from template + .env (LAST_SYNC_UTC not used for full sync)
export LAST_SYNC_UTC=""
"$SCRIPT_DIR/subst-env.sh" < "$CONFIG_DIR/migrationsettings.full-sync.template.json" \
  > "$CONFIG_DIR/migrationsettings.full-sync.json"

echo "Running full sync (Step 1)..."
docker compose -f docker-compose.full-sync.yml up --build

echo "Full sync finished. Next: run ./scripts/run-backfill.sh periodically (Step 2), then switch app config (Step 3), then run backfill again (Step 4)."
