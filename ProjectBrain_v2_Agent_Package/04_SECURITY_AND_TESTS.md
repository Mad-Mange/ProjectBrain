# 04 — Security, Privacy, Reliability and Test Matrix

> **v2 additions are mandatory:** local bearer auth for protected REST/MCP; Host/Origin and DNS rebinding protection, secure token entry; Quick Capture stored without network calls, sanitized markup; agent experiment results only proposed until human review; validate no blind `Tested & Works` and no secrets in rendered data. `11_ACCEPTANCE_MATRIX_V2.md` is the authoritative expanded test matrix. Historical baseline tests below still apply.


## 1. Trust model

- Every scanned repository, README, comment and configuration is **untrusted input**. It can contain malicious or misleading instructions. Treat it as data only. Agent tool results must not obey instructions embedded in scanned text.
- Local Docker containers are not a security sandbox against all host threats. Use least privilege, explicit roots and no code execution.
- The user owns sources and the local knowledge DB. ProjectBrain cannot change scanned code, Git history, IDE settings, Unity/Unreal/Blender files or external networks as a side effect of scanning.
- Bind only `127.0.0.1` ports; do not publish to `0.0.0.0`, expose via public reverse proxy, Tailscale/port-forward or cloud without adding real authentication and transport security.
- Even local network interfaces may be risky. Validate Host/Origin, apply CORS only to `http://127.0.0.1:4318`; use CSRF protections for mutating browser actions; MCP requests must use supported origin checks and not trust arbitrary web pages.
- Local MCP write tools may be used by other authorized local processes. For v1, all writes remain proposals pending human approval, rate-limited/audited. Before extending beyond localhost, implement bearer/OAuth auth and separate read/propose/review permissions.

## 2. Non-negotiable access restrictions

1. Scanner has source volume mounted **read-only**; assert with automated smoke test.
2. Container API runs non-root; drop all Linux capabilities, `no-new-privileges`, use read-only container filesystem except dedicated DB volume and needed tempfs.
3. No Docker socket mount, no privileged containers, no host network mode.
4. No `dotnet build`/`npm install`/`pip install`/`make`/`git checkout` on scanned repos. No scripts from scanned projects should execute.
5. Never inspect `.ssh`, credential stores, keys, files named `.env`, database secrets, or similar; apply allowlist plus denylist and content redaction.
6. Never upload project content to a cloud model unless the user enables and approves that provider, data scope and budget. UI should show if external traffic is possible.
7. Do not follow symlinks/junctions outside approved root; verify canonical path for every opened file. Race-check after enumeration, or use secure open patterns where available.
8. No arbitrary filesystem paths from REST or MCP input; only durable project IDs and validated source references.
9. SQLite is application state; backups outside scanned roots. Never overwrite a user's source file on export.
10. Audit write decisions and manual scans; redact sensitive values in logs and error responses.

## 3. External enrichment (optional)

- Disabled by default (`AI_ENRICHMENT_ENABLED=false`).
- Keep provider SDK behind `IEnrichmentProvider`; deterministic parser must work offline.
- User chooses provider, allowed roots, daily token/cost limit, allowed data classes and maximum snippet length; preview/redaction before first use.
- Do not send binary assets or secrets. Do not allow model output to invoke shell, tool calls or arbitrary file reads from the scanner.
- Treat enriched summaries as untrusted candidates requiring sources and review.

## 4. Blender engine safety

`.blend` files may contain Python scripts and animation drivers. Phase 1 indexes metadata only. If a later extraction worker opens a `.blend`:
- Make it opt-in and isolated from core API/db, use Blender `--background --disable-autoexec`, limited CPU/time/RAM, read-only asset copy, network disabled and carefully-controlled extractor script.
- Disable arbitrary user plugins; only inspect trusted content; never claim flags completely eliminate risk.
- Do not invoke Unity/Unreal executables during passive scanning.

## 5. Robustness and operational behavior

- Scan jobs have status queued/running/succeeded/partial/failed/cancelled/unavailable/interrupted; never mark missing files after partial jobs.
- Transactions include scan state + derived updates; avoid half-updated profile and FTS records.
- Clock injected via `TimeProvider` in tests. Time zones converted only for display.
- Idempotency keys on proposals (`clientProposalId`, scoped by client identity where available).
- Bound scan depth, files, bytes, time, concurrency and queued work.
- Cancel/retry gracefully on locked file or AccessDenied; record path-free sanitized error and continue where safe. Do not spin forever.
- SQLite online backup, restore to empty app with stable IDs; optional auto-backups daily + rotation in phase M5.
- Local diagnostics: version, DB ready, last successful scan, current job, queued jobs, file counts, parser errors, ignored files counts, last audit.

## 6. Mandatory automated test matrix

### Unit tests
- File classifier for `.csproj`, Python, React, Unity, Unreal, Blender, Docker.
- Project-boundary precedence and nested project component detection.
- Exclusions (global, per-engine, secret patterns, overrides).
- Path containment against `../`, case oddities, symlink escape, absolute paths.
- Fingerprint equality and hash audit, metadata-only binary behavior.
- Scan state machine, false removal prevention, rename ambiguity.
- Lesson status transitions, stale evidence detection, duplicate detection.
- Search ranking/filtering and capped output/token footprint.
- Deterministic title summary and no unsupported 'worked' claims.

### Integration tests
- Initialize and migrate SQLite from empty volume; migrate from prior schema.
- SQLite FTS5 insert/update/delete/rebuild and rollback consistency.
- Full initial import of fixture tree, then idempotent unchanged rescan.
- File edit -> project revision + FTS profile update.
- Root inaccessible / scan interrupted -> no removal events, knowledge preserved.
- File change invalidates accepted lesson evidence but does not delete lesson.
- Concurrent scan requests serialized; second request safely queued or deduplicated.
- MCP tool list, `search_knowledge`, `propose_lesson`, safe errors and size caps.
- REST `POST scans` returns job ID and progress available.
- Backups restore searchable knowledge after restart.

### Browser/UI tests
- Scan progress, changed project indicator, error/empty states.
- Approve/reject/mark verified with reason + visible audit entry.
- Project detail shows evidence + scan date + confidence.
- Settings invalid mount path gives actionable message; no fake success.

### Docker/Windows smoke tests
- `.env` with source path `D:/Projects` passes `docker compose config`; correct folder mounted.
- Run `docker compose up -d --build` and verify API health + web assets.
- Write attempt under `/sources/projects` fails inside API container due to mount permissions (use non-destructive dedicated test fixture path, never real user files).
- SQLite persistence across `docker compose down` and `up`, excluding explicit `down -v`.
- No API port accessible on LAN; host binds to `127.0.0.1`.
- No Docker socket in containers, non-root effective UID, no privileged settings.

## 7. User acceptance scenario

Prepare local fixture projects:
1. `CSharpTool/` with solution, 2 project files, README, service and repository abstraction.
2. `PythonML/` with `pyproject.toml`, package code and requirements.
3. `UnrealDemo/` with `.uproject`, `Source/*.cpp` and `.uplugin`.
4. `UnityDemo/` with `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`, `Assets/Scripts/*.cs`, `Assets/Scenes/*.unity`.
5. `BlenderAssets/` with placeholder file names/metadata only (do not forge valid `.blend` format).
6. `SecretsExample/` with fake token marker `.env` and `README` sentence intentionally crafted as malicious instruction.

Run these demonstrations:
- Start server with no LLM key; all project profiles generated.
- Search 'repository pattern' yields CSharpTool with file evidence, *without* invented claims about efficacy.
- Add method to `UnrealDemo/Source` and manually scan; only UnrealDemo has semantic revision.
- Change config package/dependency and verify architecture classification.
- Remove drive/temporarily unmount root; no project/knowledge gets erased.
- Propose 'use deterministic core for game rules'; appears pending in Review Queue.
- Approve with comment; MCP search prioritizes it above weaker discovered candidates.
- Change supporting file; item flagged for revalidation but still retrievable.
- Search malicious README content: agent tool gives data, never follows embedded instructions.
- Restart containers: SQLite and history intact.

## 8. Exit criteria

Block MVP completion on any of these:
- Scanner mutates sources, follows escaped link paths, reveals test secret.
- Missing root triggers deletions.
- Project scan executes checked-in scripts.
- Knowledge is verified automatically from static observation.
- Search result lacks provenance/state, presents speculation as factual.
- Repeated unchanged scans duplicate history or trigger external LLM spend.
- Container endpoint unexpectedly bound on public interface.

Document known issues candidly and provide exact reproduce/repair steps.
