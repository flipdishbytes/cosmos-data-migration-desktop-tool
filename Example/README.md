# Azure Table Storage → Cosmos DB Table API migration (zero-downtime)

This guide covers migrating from **Azure Table Storage** to **Cosmos DB Table API** using the Data Migration Tool so you can **migrate on the fly** without losing data. It includes Docker and Docker Compose usage, running **multiple tables in one run**, and a [ready-to-run example (two tables)](#ready-to-run-example-two-tables) in the `table-migration-example/` folder.

---

## Workflow: you need multiple syncs (full sync → backfill → switch → backfill after switch)

You run **several syncs** in sequence so that all current data is copied, changes are backfilled, then you switch the app, and finally you backfill any writes that hit the old storage during the switch.

| Step | What to run | Config | Purpose |
|------|-------------|--------|---------|
| **1. Full sync** | One run | No `QueryFilter`. `WriteMode`: `Replace` or `Merge`. | Copy **all current data** from Azure Table Storage to Cosmos DB Table API. |
| **2. Backfill** | Multiple runs (e.g. every 5–15 min) | Add `QueryFilter`: `Timestamp ge datetime'<last_sync_utc>'`. Same `WriteMode`. Update `<last_sync_utc>` after each run. | Catch **all updates** (inserts/updates) written to Table Storage while the app still points there. Repeat until lag is small. |
| **3. Switch** | — | Change app config only. | Point the app from `*.table.core.windows.net` to `*.table.cosmos.azure.com`. Same SDK, no code change. |
| **4. Backfill after switch** | One or more delta runs | Same as step 2: `QueryFilter` with last sync time, `WriteMode` `Replace` or `Merge`. | Copy any writes that still went to **Table Storage** during the switch window (e.g. in-flight requests, caches, or a short dual-write). Then Cosmos DB has everything. |

After step 4, the app reads and writes Cosmos DB only; you can leave Table Storage as a backup or decommission it.

---

## What gets synced (all current data + updates)

- **Full sync (step 1):** No filter → entire table(s) copied; `Replace`/`Merge` upserts into Cosmos DB.
- **Backfill (steps 2 & 4):** `QueryFilter`: `Timestamp ge datetime'<last_sync_utc>'` → only **new or modified** entities since that time are read and upserted. Azure Table Storage updates `Timestamp` on every insert and update, so you capture all changes. Use the same `WriteMode`: `Replace` or `Merge` so the destination stays in sync.

---

## Strategy: initial full copy + backfill + switch + backfill after switch

1. **Full sync**  
   Run the tool once with **Source** = Azure Table Storage and **Sink** = Cosmos DB Table API. Do **not** set `QueryFilter`. Use **Sink** `WriteMode`: `Replace` or `Merge`. This copies **all current data** in the table(s).

2. **Backfill (repeat until ready to switch)**  
   Run the tool **periodically** (e.g. every 5–15 minutes). Each run:
   - In **SourceSettings**, set `QueryFilter` to:  
     `"QueryFilter": "Timestamp ge datetime\u0027<last_sync_utc>\u0027"`  
     Use the **UTC** time when you last *started* a sync (or a few seconds before).
   - Keep **Sink** `WriteMode` as `Replace` or `Merge`.
   - After each successful run, update and persist `last_sync_utc` (e.g. in a file or env var).

   See [Azure Table API extension – QueryFilter](../Extensions/AzureTableAPI/README.md#query-filter-examples) for filter syntax.

3. **Switch**  
   When backfill lag is acceptable, switch the application’s table endpoint from `*.<account>.table.core.windows.net` to `*.<account>.table.cosmos.azure.com`. Rollback is a config change only.

4. **Backfill after switch**  
   Run **one or more delta syncs** (same config as step 2) with the source still pointing at **Azure Table Storage**. This backfills any data written to Table Storage during the switch (in-flight traffic, caches, etc.). After that, Cosmos DB has the full dataset and you can treat Table Storage as read-only or retire it.

---

## Running multiple tables at once

**Yes.** The tool supports **multiple data transfer operations in a single run** via the `Operations` array in `migrationsettings.json`.

- Set **Source** and **Sink** once at the top level (e.g. `AzureTableAPI` for both).
- Put shared settings in **SourceSettings** and **SinkSettings** (e.g. connection strings, `WriteMode`).
- Add one entry in **Operations** per table; each entry only needs to override **Table** (and optionally **QueryFilter** for delta). Operation settings are merged over the top-level settings.

### Example: multiple tables (full copy)

```json
{
  "Source": "AzureTableAPI",
  "Sink": "AzureTableAPI",
  "SourceSettings": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=<storage-account>;AccountKey=<key>;EndpointSuffix=core.windows.net"
  },
  "SinkSettings": {
    "ConnectionString": "AccountEndpoint=https://<account>.table.cosmos.azure.com;AccountKey=<key>;",
    "WriteMode": "Replace"
  },
  "Operations": [
    { "SourceSettings": { "Table": "Users" }, "SinkSettings": { "Table": "Users" } },
    { "SourceSettings": { "Table": "Orders" }, "SinkSettings": { "Table": "Orders" } },
    { "SourceSettings": { "Table": "AuditLog" }, "SinkSettings": { "Table": "AuditLog" } }
  ]
}
```

### Example: multiple tables (delta sync)

Use the same structure and add a **Timestamp** filter to **SourceSettings** so it applies to all operations (same “changed since” time for every table):

```json
{
  "Source": "AzureTableAPI",
  "Sink": "AzureTableAPI",
  "SourceSettings": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=<storage-account>;AccountKey=<key>;EndpointSuffix=core.windows.net",
    "QueryFilter": "Timestamp ge datetime\u00272024-01-15T12:00:00Z\u0027"
  },
  "SinkSettings": {
    "ConnectionString": "AccountEndpoint=https://<account>.table.cosmos.azure.com;AccountKey=<key>;",
    "WriteMode": "Replace"
  },
  "Operations": [
    { "SourceSettings": { "Table": "Users" }, "SinkSettings": { "Table": "Users" } },
    { "SourceSettings": { "Table": "Orders" }, "SinkSettings": { "Table": "Orders" } },
    { "SourceSettings": { "Table": "AuditLog" }, "SinkSettings": { "Table": "AuditLog" } }
  ]
}
```

Replace the datetime in `QueryFilter` with your last sync time (UTC, ISO 8601). Update it after each successful run when scripting delta sync.

---

## Ready-to-run example (two tables)

The `docs/table-migration-example/` folder contains Docker Compose files, configs, and scripts for the full workflow with **two tables** (TableOne, TableTwo). You can copy that folder to your workspace (e.g. `migration/`) and run it.

### Setup

1. **Copy** the `table-migration-example` folder to your workspace (e.g. `migration/`).
2. **Copy `.env.example` to `.env`** and set your connection strings:
   - `STORAGE_CONNECTION_STRING` – Azure Table Storage
   - `COSMOS_CONNECTION_STRING` – Cosmos DB Table API  
   The scripts generate the migration config from templates and `.env` (and `.last-sync-utc` for backfill). Do not commit `.env`; it is in `.gitignore`.
3. **Rename tables** if needed: change `TableOne` and `TableTwo` in `config/migrationsettings.full-sync.template.json` and `config/migrationsettings.backfill.template.json` to your real table names.
4. **Reserved or custom renames:** Cosmos DB Table API reserves `id`, `etag`, `rid`, `ResourceId`. The templates set **SinkSettings** `PropertyRenames`: `[ { "From": "id", "To": "entityId" } ]`. Add more entries to rename other properties (e.g. `{ "From": "etag", "To": "entityEtag" }`).
5. **Make scripts executable:**  
   `chmod +x scripts/run-full-sync.sh scripts/run-backfill.sh scripts/subst-env.sh`

### Example files

| File | Purpose |
|------|---------|
| `.env.example` | Copy to `.env` and set `STORAGE_CONNECTION_STRING`, `COSMOS_CONNECTION_STRING`. |
| `config/migrationsettings.full-sync.template.json` | Template for Step 1; script replaces `__STORAGE_CONNECTION_STRING__`, `__COSMOS_CONNECTION_STRING__` from `.env` and writes `migrationsettings.full-sync.json`. |
| `config/migrationsettings.backfill.template.json` | Template for steps 2 & 4; script replaces the same env vars plus `__LAST_SYNC_UTC__` from `.last-sync-utc` and writes `migrationsettings.backfill.json`. |
| `docker-compose.full-sync.yml` | Compose for full sync (builds tool from source). |
| `docker-compose.backfill.yml` | Compose for backfill (builds tool from source). |
| `scripts/run-full-sync.sh` | Loads `.env`, generates config from template, runs Step 1. |
| `scripts/run-backfill.sh` | Loads `.env`, reads `.last-sync-utc`, generates config from template, runs backfill, updates `.last-sync-utc`. |
| `scripts/subst-env.sh` | Helper: replaces `__VAR_NAME__` with env var values when generating JSON. |

### Example workflow

| Step | Command | When |
|------|---------|------|
| **1. Full sync** | `./scripts/run-full-sync.sh` | Once. Copies all current data. |
| **2. Backfill** | `./scripts/run-backfill.sh` | Repeatedly (e.g. every 5–15 min) until lag is small. |
| **3. Switch** | — | Change app config: point table endpoint from `*.table.core.windows.net` to `*.table.cosmos.azure.com`. |
| **4. Backfill after switch** | `./scripts/run-backfill.sh` | Once or a few times to catch writes that hit Table Storage during the switch. |

### Example commands (without scripts)

Generate config from templates and `.env` first (run-full-sync.sh and run-backfill.sh do this). Then you can run Compose directly:

```bash
# Step 1 (after generating config/migrationsettings.full-sync.json via run-full-sync.sh or subst-env.sh)
docker compose -f docker-compose.full-sync.yml up

# Steps 2 & 4 (run-backfill.sh is recommended so .last-sync-utc is updated)
docker compose -f docker-compose.backfill.yml run --rm dmt
```

**Requirements:** Docker (and Docker Compose v2), and connection strings with access to source (Table Storage) and destination (Cosmos DB Table API).

---

## Docker commands

### One-off run (full sync or backfill)

- **Full sync (workflow step 1):** Use a settings file with **no** `QueryFilter`.
- **Backfill (workflow steps 2 & 4):** Use a settings file with `QueryFilter`: `Timestamp ge datetime'<last_sync_utc>'` and update `<last_sync_utc>` after each run.

**Using the pre-built image:**

```bash
# Pull the image
docker pull mcr.microsoft.com/azurecosmosdb/linux/azure-cosmos-dmt:latest

# Run once (mount config and optional data dir)
docker run --rm \
  -v "$(pwd)/config:/config" \
  -v "$(pwd)/data:/data" \
  mcr.microsoft.com/azurecosmosdb/linux/azure-cosmos-dmt:latest \
  run --settings /config/migrationsettings.json
```

**Using a locally built image:**

```bash
docker build -t data-migration-tool .
docker run --rm \
  -v "$(pwd)/config:/config" \
  -v "$(pwd)/data:/data" \
  data-migration-tool \
  run --settings /config/migrationsettings.json
```

---

## Docker Compose

### Directory layout

Use a dedicated folder (e.g. `migration/`) with config and optional data:

```
migration/
├── config/
│   └── migrationsettings.json    # your Table Storage → Cosmos Table API settings
├── data/                         # optional, for any file-based sources/sinks
└── docker-compose.yml
```

### Docker Compose file

Create `docker-compose.yml` in your migration folder (e.g. `migration/docker-compose.yml`) and ensure `config/migrationsettings.json` exists. Workflow: **full sync** (step 1) → **backfill** (steps 2 & 4) — use no `QueryFilter` for full sync, add `QueryFilter` for backfill runs.

```yaml
version: '3'

services:
  dmt:
    image: mcr.microsoft.com/azurecosmosdb/linux/azure-cosmos-dmt:latest
    volumes:
      - ./config:/config
      - ./data:/data
    command: run --settings /config/migrationsettings.json
```

**Commands:**

```bash
# Step 1 – Full sync: no QueryFilter in migrationsettings.json
docker compose up

# Steps 2 & 4 – Backfill: set QueryFilter to Timestamp ge datetime'<last_sync_utc>', update after each run
docker compose run --rm dmt
```

Run these manually or from a scheduler (cron, Azure DevOps, etc.). For multiple tables, use the `Operations` array in `migrationsettings.json`:

```json
"Operations": [
  { "SourceSettings": { "Table": "Users" }, "SinkSettings": { "Table": "Users" } },
  { "SourceSettings": { "Table": "Orders" }, "SinkSettings": { "Table": "Orders" } },
  { "SourceSettings": { "Table": "AuditLog" }, "SinkSettings": { "Table": "AuditLog" } }
]
```

Add this to your existing settings; keep top-level `Source`, `Sink`, `SourceSettings` (connection string, and `QueryFilter` for backfill), and `SinkSettings` (connection string, `WriteMode`). See [Running multiple tables at once](#running-multiple-tables-at-once) for full examples.

### Compose file: run with locally built image

If you build from the repo root:

```yaml
version: '3'

services:
  dmt:
    build:
      context: .
      dockerfile: Dockerfile
    volumes:
      - ./config:/config
      - ./data:/data
    command: run --settings /config/migrationsettings.json
```

From the repo root (so `context` is correct):

```bash
docker compose -f migration/docker-compose.yml up
```

Or from `migration/` with a path to the repo root:

```yaml
services:
  dmt:
    build:
      context: ..
      dockerfile: Dockerfile
    volumes:
      - ./config:/config
      - ./data:/data
    command: run --settings /config/migrationsettings.json
```

### Scheduling delta sync with Docker Compose

The tool does not schedule itself. To run **delta sync** repeatedly:

1. **Cron (Linux/macOS)**  
   Update `last_sync_utc` in `config/migrationsettings.json` (or in a template that you copy over), then run:

   ```bash
   docker compose up
   ```

   Example crontab (every 10 minutes):

   ```cron
   */10 * * * * cd /path/to/migration && docker compose run --rm dmt
   ```

   You’ll need a small script that (a) sets `QueryFilter` to the current time (or “last run” time) and (b) runs `docker compose run --rm dmt`.

2. **One-off “delta now”**  
   For a single delta run, set `QueryFilter` in `config/migrationsettings.json` to the desired `last_sync_utc`, then:

   ```bash
   docker compose run --rm dmt
   ```

---

## Example settings files

### Single table – full copy

Use `WriteMode`: `"Replace"` or `"Merge"` so that the initial copy and all later updates are applied (upsert). Do **not** use `Create` if you plan to run ongoing syncs—it would fail on existing rows.

`config/migrationsettings.json`:

```json
{
  "Source": "AzureTableAPI",
  "Sink": "AzureTableAPI",
  "SourceSettings": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=<storage-account>;AccountKey=<key>;EndpointSuffix=core.windows.net",
    "Table": "YourTable"
  },
  "SinkSettings": {
    "ConnectionString": "AccountEndpoint=https://<account>.table.cosmos.azure.com;AccountKey=<key>;",
    "Table": "YourTable",
    "WriteMode": "Replace"
  }
}
```

### Single table – delta sync

Add to **SourceSettings** (use your last sync time in ISO 8601 UTC; in JSON use `\u0027` for single quotes):

```json
"QueryFilter": "Timestamp ge datetime\u00272024-01-15T12:00:00Z\u0027"
```

### Multiple tables – full copy

Use the [multiple tables example](#example-multiple-tables-full-copy) above and put it in `config/migrationsettings.json`. Then run:

```bash
docker compose up
```

or:

```bash
docker run --rm -v "$(pwd)/config:/config" -v "$(pwd)/data:/data" \
  mcr.microsoft.com/azurecosmosdb/linux/azure-cosmos-dmt:latest \
  run --settings /config/migrationsettings.json
```

---

## RBAC (passwordless) authentication

If you use connection strings, keep them in `migrationsettings.json` (and exclude that file from version control). For RBAC (e.g. managed identity, Azure CLI), use the settings described in [Azure Table API extension – Authentication](../Extensions/AzureTableAPI/README.md#authentication-methods) and ensure the container has the right identity (e.g. Azure CLI login on the host, or env vars / managed identity when running in Azure). The same `docker` and `docker compose` commands apply; only the config content changes.

---

## Risks to watch

- **Hot partitions** → use autoscale and monitoring on the Cosmos DB account.
- **RU exhaustion** → set alerts and consider autoscale.
- **Data drift** → keep running delta sync (with `Timestamp` filter) until cutover; optionally run a final full verification pass.

---
