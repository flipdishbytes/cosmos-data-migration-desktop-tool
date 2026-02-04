# Azure Table Storage → Cosmos DB Table API migration (zero-downtime)

This guide covers migrating from **Azure Table Storage** to **Cosmos DB Table API** using the Data Migration Tool so you can **migrate on the fly** without losing data. It includes Docker Compose usage, running **multiple tables in one run**, and ready-to-run scripts.

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
     `"QueryFilter": "Timestamp ge datetime'<last_sync_utc>'"`  
     Use the **UTC** time when you last *started* a sync (or a few seconds before).
   - Keep **Sink** `WriteMode` as `Replace` or `Merge`.
   - After each successful run, update and persist `last_sync_utc` (e.g. in a file or env var).

   See [Azure Table API extension – QueryFilter](../Extensions/AzureTableAPI/README.md#query-filter-examples) for filter syntax.

3. **Switch**  
   When backfill lag is acceptable, switch the application's table endpoint from `*.<account>.table.core.windows.net` to `*.<account>.table.cosmos.azure.com`. Rollback is a config change only.

4. **Backfill after switch**  
   Run **one or more delta syncs** (same config as step 2) with the source still pointing at **Azure Table Storage**. This backfills any data written to Table Storage during the switch (in-flight traffic, caches, etc.). After that, Cosmos DB has the full dataset and you can treat Table Storage as read-only or retire it.

---

## Running multiple tables at once

**Yes.** The tool supports **multiple data transfer operations in a single run** via the `Operations` array in `migrationsettings.json`.

- Set **Source** and **Sink** once at the top level (e.g. `AzureTableAPI` for both).
- Put shared settings in **SourceSettings** and **SinkSettings** (e.g. connection strings, `WriteMode`).
- Add one entry in **Operations** per table; each entry only needs to override **Table** (and optionally **QueryFilter** for delta). Operation settings are merged over the top-level settings.

See `config/migrationsettings.full-sync.template.json` and `config/migrationsettings.backfill.template.json` for examples.

---

## Ready-to-run example (two tables)

This `Example/` folder contains Docker Compose files, configs, and scripts for the full workflow with **two tables** (`testmigration`, `testmigration2`). The scripts handle config generation, running docker compose, and tracking the last sync timestamp automatically.

### Directory structure

```
Example/
├── .env.example                                    # Copy to .env and fill in connection strings
├── config/
│   ├── migrationsettings.full-sync.template.json   # Template for full sync
│   └── migrationsettings.backfill.template.json    # Template for backfill
├── data/                                           # Optional, for file-based sources/sinks
├── docker-compose.full-sync.yml                    # Compose for full sync
├── docker-compose.backfill.yml                     # Compose for backfill
└── scripts/
    ├── run-full-sync.sh                            # Runs Step 1 (full sync)
    ├── run-backfill.sh                             # Runs Steps 2 & 4 (backfill)
    └── subst-env.sh                                # Helper: replaces __VAR__ placeholders
```

### Setup

1. **Copy `.env.example` to `.env`** and set your connection strings:
   ```bash
   cp .env.example .env
   ```
   Edit `.env` and set:
   - `STORAGE_CONNECTION_STRING` – Azure Table Storage connection string
   - `COSMOS_CONNECTION_STRING` – Cosmos DB Table API connection string
   
   **Important:** Values must be quoted because connection strings contain semicolons. Do not commit `.env`; it is in `.gitignore`.

2. **Rename tables** if needed: change `testmigration` and `testmigration2` in `config/migrationsettings.full-sync.template.json` and `config/migrationsettings.backfill.template.json` to your real table names.

3. **Reserved or custom renames:** Cosmos DB Table API reserves `id`, `etag`, `rid`, `ResourceId`. The templates set **SinkSettings** `PropertyRenames`: `[ { "From": "id", "To": "entityId" } ]`. Add more entries to rename other properties (e.g. `{ "From": "etag", "To": "entityEtag" }`).

4. **Make scripts executable:**
   ```bash
   chmod +x scripts/run-full-sync.sh scripts/run-backfill.sh scripts/subst-env.sh
   ```

### How the scripts work

**`run-full-sync.sh`** (Step 1):
1. Loads environment variables from `.env`
2. Generates `config/migrationsettings.full-sync.json` from the template (replacing `__STORAGE_CONNECTION_STRING__` and `__COSMOS_CONNECTION_STRING__`)
3. Runs `docker compose -f docker-compose.full-sync.yml up --build`

**`run-backfill.sh`** (Steps 2 & 4):
1. Loads environment variables from `.env`
2. Reads the last sync timestamp from `.last-sync-utc` (defaults to `1970-01-01T00:00:00Z` on first run)
3. Generates `config/migrationsettings.backfill.json` from the template (replacing `__STORAGE_CONNECTION_STRING__`, `__COSMOS_CONNECTION_STRING__`, and `__LAST_SYNC_UTC__`)
4. Runs `docker compose -f docker-compose.backfill.yml run --build --rm dmt`
5. On success, updates `.last-sync-utc` with the current UTC timestamp for the next run

### Example workflow

| Step | Command | When |
|------|---------|------|
| **1. Full sync** | `./scripts/run-full-sync.sh` | Once. Copies all current data. |
| **2. Backfill** | `./scripts/run-backfill.sh` | Repeatedly (e.g. every 5–15 min) until lag is small. |
| **3. Switch** | — | Change app config: point table endpoint from `*.table.core.windows.net` to `*.table.cosmos.azure.com`. |
| **4. Backfill after switch** | `./scripts/run-backfill.sh` | Once or a few times to catch writes that hit Table Storage during the switch. |

### Requirements

- Docker with Compose v2 (`docker compose` command)
- Connection strings with access to source (Azure Table Storage) and destination (Cosmos DB Table API)
- Bash shell (for running the scripts)

---

## Scheduling backfill with cron

The tool does not schedule itself. To run **backfill** repeatedly, use `cron` or another scheduler.

**Example crontab (every 10 minutes):**

```cron
*/10 * * * * cd /path/to/Example && ./scripts/run-backfill.sh >> /var/log/backfill.log 2>&1
```

The `run-backfill.sh` script automatically manages the `.last-sync-utc` timestamp file.

---

## RBAC (passwordless) authentication

If you use connection strings, keep them in `migrationsettings.json` (and exclude that file from version control). For RBAC (e.g. managed identity, Azure CLI), use the settings described in [Azure Table API extension – Authentication](../Extensions/AzureTableAPI/README.md#authentication-methods) and ensure the container has the right identity (e.g. Azure CLI login on the host, or env vars / managed identity when running in Azure). The same `docker compose` commands apply; only the config content changes.

---

## Risks to watch

- **Hot partitions** → use autoscale and monitoring on the Cosmos DB account.
- **RU exhaustion** → set alerts and consider autoscale.
- **Data drift** → keep running delta sync (with `Timestamp` filter) until cutover; optionally run a final full verification pass.

---
