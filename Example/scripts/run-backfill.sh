#!/usr/bin/env bash
# Steps 2 & 4: Backfill – sync only entities changed since last run.
# Generates config from config/migrationsettings.backfill.template.json, .env, and .last-sync-utc.

set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXAMPLE_DIR="$(dirname "$SCRIPT_DIR")"
CONFIG_DIR="$EXAMPLE_DIR/config"
LAST_SYNC_FILE="$EXAMPLE_DIR/.last-sync-utc"

cd "$EXAMPLE_DIR"

if [[ ! -f ".env" ]]; then
  echo "Missing .env. Copy .env.example to .env and set STORAGE_CONNECTION_STRING and COSMOS_CONNECTION_STRING."
  exit 1
fi
set -a
source .env
set +a

# Last sync time: from file, or epoch for first run (syncs everything; redundant with full sync but safe)
if [[ -f "$LAST_SYNC_FILE" ]]; then
  export LAST_SYNC_UTC=$(cat "$LAST_SYNC_FILE")
else
  export LAST_SYNC_UTC="1970-01-01T00:00:00Z"
  echo "First backfill run: using $LAST_SYNC_UTC (all changes since epoch)."
fi

# Generate backfill config from template + .env + .last-sync-utc
"$SCRIPT_DIR/subst-env.sh" < "$CONFIG_DIR/migrationsettings.backfill.template.json" \
  > "$CONFIG_DIR/migrationsettings.backfill.json"

echo "Backfilling changes since $LAST_SYNC_UTC..."
if docker compose -f docker-compose.backfill.yml run  --build --rm dmt; then
  NEW_LAST_SYNC=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  echo "$NEW_LAST_SYNC" > "$LAST_SYNC_FILE"
  echo "Backfill done. Next run will use timestamp: $NEW_LAST_SYNC"
else
  echo "Backfill failed. .last-sync-utc was not updated."
  exit 1
fi
