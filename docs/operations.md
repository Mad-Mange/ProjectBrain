# Operations and recovery

## Storage and startup

The API runs as the image's non-root app user. Docker initializes the named projectbrain_data volume at /app/data; migrations run before the API accepts requests. The root filesystem is read-only, with a bounded /tmp tmpfs. The source bind mount uses read_only and create_host_path:false. Both published ports bind to 127.0.0.1.

Required environment: PROJECTBRAIN_AUTH_TOKEN (at least 32 UTF-8 bytes), PROJECTBRAIN_SOURCES (default /sources/projects), PROJECTBRAIN_DB (default an application data subdirectory). PROJECTBRAIN_AGENT_TOKEN is optional, distinct and at least 32 characters. The database must not be inside the scanned source mount. No token is compiled into the frontend, stored in browser storage, or returned by settings.

First package/image installation needs network access. Once images and data exist, inventory, capture, FTS search, scheduling, review and experiments require no internet, model provider, or engine installation.

## Scheduling and failure behavior

Register a root and explicitly run Initial Inventory. A single hosted consumer processes the database queue, and a unique filtered index allows only one queued/running job per root. Concurrent manual requests return the existing job ID. One API instance per database is supported.

After each completed attempt, next_scan_utc is set to the configured interval from completion. The default is six hours. Successful scans update last_success_utc; successful full audits also update last_audit_utc. Failed scans wait the same interval, avoiding retry loops. On restart, running jobs become interrupted, queued work remains, and overdue enabled roots queue one catch-up. Weekly full audits override metadata fast paths.

Pause and UTC quiet hours apply to automatic scheduling. Manual scans are still allowed. Root-specific schedule enablement is preserved after the first successful inventory.

The scanner stages a complete inventory before publishing one transaction. Cancellation, byte/file/depth/time budgets, permission failures and unstable reads preserve the old manifest and do not generate removals. Partial scans repeat safe directory traversal but reuse durable hashes/extracts for unchanged noncritical text. Each retry receives a fresh byte budget; full audits and critical files are always rehashed. Completed publication clears checkpoints. Restart recovery requeues interrupted work once and preserves cancellation requests. File/depth ceilings still require raising the configured limit before retry. Missing roots are marked unavailable with zero delete events. Knowledge remains searchable.

A normal rescan compares size and timestamp, rehashes critical metadata, and skips unchanged text. Full audits also detect same-size/same-timestamp edits. Matching unique removed/added hashes yield file renames; otherwise separate changes are retained. An unambiguous project manifest signature preserves identity across folder moves. Ambiguous matches are recorded, never silently merged.

## Backup

Use **Settings → Download database backup** or POST /api/v1/backups. This uses SQLite's online backup API, including WAL-visible data, and returns a generated ID, SHA-256 and download URL. GET /api/v1/backups/{id} requires authentication. Backups are stored beneath the data volume, never in the scanned root. Keep an off-machine copy according to your own retention policy; automatic backup rotation is not implemented.

Do not copy a live database alone while ignoring its WAL. Do not use `docker compose down -v` unless you explicitly intend to erase the named volume.

Before an upgrade from an existing schema, the runner creates a .before-upgrade online snapshot and applies each migration transactionally. A migration failure aborts startup. Preserve the database and backup rather than repeatedly editing schema manually.

## Restore

Stop the API and restore into a destination that does not exist. The command validates the source database and schema, uses online backup, and refuses to overwrite an existing database.

Native development:

```powershell
# Stop the running API first. Preserve the old data file and any WAL/SHM together.
dotnet run --project src/ProjectBrain.Api --no-launch-profile -- restore D:/Backups/projectbrain.db D:/ProjectBrainData/restored.db
# Set PROJECTBRAIN_DB to the restored destination and restart.
```

Docker, with a backup already in /app/data/backups:

```powershell
docker compose stop api
docker compose run --rm --no-deps api restore /app/data/backups/<id>.db /app/data/restored.db
# Set PROJECTBRAIN_DB: /app/data/restored.db in compose.yaml, then:
docker compose up -d api
```

The REST JSON export is portable reference data. A duplicate-safe JSON/Markdown importer is not implemented; use database backup/restore for authoritative restoration.

## Diagnostics and checks

- GET /health/live and /health/ready are public loopback health probes.
- GET /api/v1/diagnostics returns schema/version, file count, queue size and disabled AI state.
- Dashboard shows job counts, cancellation and sanitized errors. Activity records decisions and evidence changes.
- `scripts/check.ps1` runs build/tests/frontend checks and Compose configuration validation.
- `scripts/compose-smoke.ps1` uses only a dedicated synthetic source directory and isolated Compose project. It attempts a write to that fixture mount, checks non-root execution, captures a note and tests restart persistence. It stops the smoke services afterward but retains their isolated data volume.
- `python scripts/mcp-smoke.py` verifies a real initialized MCP tool call without modifying client configuration.

The regular stack uses ports 4317/4318; browser and Compose tests use 14317/14318 by default and can run alongside it. The Compose script accepts `-ApiPort` and `-WebPort`. `PROJECTBRAIN_API_PORT` and `PROJECTBRAIN_WEB_PORT` override Compose host ports while preserving loopback binding; the configured web origin is passed to the API and checked exactly. See `IMPLEMENTATION_PROGRESS.md` for actual validation results.


## Scanner metadata and exceptions

Settings accepts `includeDirectories`, a list such as `["MyTool/build"]` relative to each registered scan root. Only `build`, `Library` and `Saved` exclusions can be overridden; credential directories, secret files and symbolic links stay excluded. The web Settings page exposes this field.

Generated Trivy cache folders (`trivy-cache`, `.trivy`), Python `site-packages`, and virtual environments identified by `pyvenv.cfg` are excluded. Other `artifacts` folders remain eligible, including experiment results. Scan progress reports the phase, root-relative path, enumeration/read counters and elapsed time; sensitive path segments are redacted individually.

Git metadata reads only bounded `.git/HEAD`, a validated loose ref, or `packed-refs`, and charges those bytes to the scan budget. It never launches Git or reads objects/hooks/remotes. Linked worktree `.git` redirects are not followed; commit metadata is unknown there. Multiple `.uproject` manifests in one directory receive separate IDs keyed by manifest path, with shared source attributed explicitly to each. `source_relative_path` records their physical evidence base.
