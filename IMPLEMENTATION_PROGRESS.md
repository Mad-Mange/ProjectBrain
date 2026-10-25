# ProjectBrain v2 implementation progress

Updated: 2026-10-25 (validation began 2026-10-24). Working application, real packages, real SQLite and real Docker containers. Acceptance tests use synthetic fixtures; later real-library checks are recorded below. Historical source files remain read-only. User-global Codex MCP and English retrieval instructions are now installed and verified as recorded below.

## Delivered and verified

The .NET 10 REST/MCP API, React UI, migrations, six-hour incremental scanner, weekly hash audit, Quick Capture, Inbox, Try Next, human-reviewed experiments, FTS search, backup/restore and two-service Compose deployment are implemented. MCP uses the official C# SDK 2.3.0 and exposes nine bounded tools, with proposals only for writes.

This session cleared earlier environment blockers and fixed actual runtime failures: Vite and nginx UTF-8 BOMs, vulnerable frontend/native SQLite dependencies, the web health probe, PowerShell smoke-test argument handling, and interrupted scan recovery. Added durable scan read checkpoints, Git metadata, project-scoped exclusion exceptions, separate identities for multiple Unreal manifests, real production migration tests, schedule/conflict/backup tests, and a self-contained browser golden journey. Current schema is 4.

## Commands and results

| Command | Actual result |
|---|---|
| `dotnet restore ProjectBrain.slnx --configfile NuGet.Config` | PASS, official packages; auditing enabled in final builds |
| `dotnet format whitespace ProjectBrain.slnx --no-restore` | PASS |
| `dotnet build ProjectBrain.slnx --no-restore` | PASS, 0 warnings, 0 errors |
| `dotnet test ProjectBrain.slnx --no-build --logger "trx;LogFileName=runtime.trx"` | PASS, 54 domain/parser + 36 service/HTTP/MCP/reliability = 90 tests; no skips |
| `python tests/schema_test.py` | PASS, 8 real SQLite/FTS5 tests |
| `npm.cmd --prefix frontend ci` | PASS, generated pinned lockfile |
| `npm.cmd --prefix frontend run build` | PASS, TypeScript and production Vite bundle |
| `npm.cmd --prefix frontend test` | PASS, 3 React tests |
| `npm.cmd --prefix frontend run e2e` | PASS, 2 Chromium journeys against an isolated local API |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/compose-smoke.ps1 -Browser` | PASS on final schema 4, including existing-volume upgrade, both browser journeys and capture/experiment restart persistence |
| `python scripts/mcp-smoke.py` (inside Compose smoke, temporary credential in environment) | PASS, initialize, list 9 tools, real search call |
| `dotnet src/ProjectBrain.Api/bin/Debug/net10.0/ProjectBrain.Api.dll restore <browser-backup.db> <new-destination.db>` | PASS, actual CLI restored 2 captures, 1 reviewed experiment, evidence and SQLite integrity; destination was a new ignored workspace file |
| `npm.cmd --prefix frontend audit` | PASS, 0 vulnerabilities after updates |
| `dotnet list ProjectBrain.slnx package --vulnerable --include-transitive` | PASS, no vulnerable packages reported |
| `codex.cmd mcp add --help`; `copilot.cmd mcp add --help`; `claude mcp add --help` | PASS, installed syntax checked; no model-driven client calls claimed |

Evidence paths: `tests/ProjectBrain.DomainTests/TestResults/runtime.trx`, `tests/ProjectBrain.Tests/TestResults/runtime.trx`, `.local/compose-services.json`, `.local/compose-smoke.log`, and `frontend/test-results/journey-inventory-capture--ae41d-nt-changed-proof-and-backup/reviewed-proof.png` / `stale-proof.png`. Screenshots were visually inspected. Test artifacts and temporary credentials are not committed.

## Demonstrated golden journey

1. Imported copies of .NET, Unity, Unreal, Python, JavaScript, Docker and Blender fixtures without executing them.
2. Saved a raw note through the browser shortcut and Quick Capture, then found its Draft/Untested item in Try Next. Hostile HTML stayed inert; reload required token re-entry and retained the capture.
3. Used the human UI to approve usefulness and accept a measurable experiment plan. A real MCP `propose_experiment_result` call created a pending proposal without verifying anything.
4. The browser test exercised the human review controls and explicit evidence confirmation. Only then did the badge become Tested & Works. This is an automated test of the human workflow using synthetic evidence, not a real-world claim approved by the user.
5. Modified only a temporary synthetic test artifact and rescanned. Validation became stale, Needs recheck appeared, and the previous passed result remained in history.
6. Downloaded a genuine SQLite online backup. Provider tests reopened backups with captures, FTS, two conflicting outcomes, proposals and resolution intact.
7. Compose verified both containers healthy, API non-root, read-only source write failure and capture persistence after restart. Real MCP search/result tool names and outcomes are in the API log. The final smoke also compared complete experiment/result histories before and after a second restart; they matched.

## Atomic backlog

DONE means implemented and validated at the indicated boundary. The client-session gap is explicitly retained.

| Task | Status | Evidence |
|---|---|---|
| PB-001 | DONE | Full solution and frontend build |
| PB-002 | DONE | Two images, healthy services, loopback ports, non-root and read-only probe |
| PB-003 | DONE | Readiness/migrations, nginx SPA/proxy and browser boot |
| PB-004 | DONE | HTTP tests: bearer, host/origin rejection, proposal-only agent credential |
| PB-005 | DONE | Local quality script and CI build/test/browser steps |
| PB-101 | DONE | Real provider clean/upgrade migration and backup tests; SQL/FTS integrity |
| PB-102 | DONE | Root containment/relative paths and reparse-point guards; input tests |
| PB-103 | DONE | File/depth/byte/time limits, partial publication prevention, byte/file tests |
| PB-104 | DONE | All required fixture stacks; separate shared-folder Unreal identities; scoped exceptions |
| PB-105 | DONE | Stable IDs, profiles, file hashes, revision/evidence persistence |
| PB-106 | DONE | Browser inventory/project cards, profiles, scans and search |
| PB-201 | DONE | Incremental fast path and same-size/same-mtime full-audit regression |
| PB-202 | DONE | Edit/touch/rename/remove tests and separate project revisions |
| PB-203 | DONE | Injected-clock six-hour/weekly/pause/duplicate scheduling tests |
| PB-204 | DONE | Missing roots, cancellation, interrupted requeue, durable byte-budget retry |
| PB-205 | DONE | Browser/provider source edit stales proof and preserves history |
| PB-301 | DONE | Shared knowledge lifecycle, provenance, versions, receipts and audits |
| PB-302 | DONE | Real FTS search, filters, bounded MCP responses |
| PB-303 | DONE | Safe deterministic extraction and redaction tests |
| PB-304 | DONE | Human review UI, review queue, duplicate suggestions and explicit relations |
| PB-305 | DONE | Provider upgrade, Python schema/FTS rollback and security tests |
| PB-401 | DONE | Original whitespace/raw capture and retry persistence tests |
| PB-402 | DONE | React tests and browser Ctrl+K/Inbox workflow |
| PB-403 | DONE | Independent axes and legacy verified downgrade |
| PB-404 | DONE | Try Next browser/service tests and flag clearing |
| PB-405 | DONE | Browser plan, MCP result proposal, indexed evidence validation |
| PB-406 | DONE | Reviewed proof, stale hashes, conflicts and explicit scoped resolution tests |
| PB-407 | DONE | Audit/activity/provenance routes and review history |
| PB-408 | DONE | Capture, XSS, retry, stale and backup tests |
| PB-501 | DONE | Official SDK Streamable HTTP builds and real initialization |
| PB-502 | DONE | Nine tool schemas, search/profile/analysis/Try Next tools |
| PB-503 | DONE | Real MCP proposals; no approval/verification tool exposed |
| PB-504 | DONE | Shared auth, input validation, redaction, bounded output and evidence checks |
| PB-505 | IN_PROGRESS | Codex user-global connection/instructions and real new-project agent retrieval PASS; Copilot/Claude templates and installed syntax checked, actual sessions remain untested |
| PB-506 | DONE | Candidate/context/proposal tools and retrospective instructions; model-driven curation is user-owned |
| PB-601 | DONE | Real online backup, restore command, provider reopen with captures/outcomes |
| PB-602 | DONE | Settings, source onboarding, scan history, diagnostics and activity |
| PB-603 | DONE | Windows Docker smoke and browser journey pass on final schema 4 |
| PB-604 | DONE | Runnable README, API/operations/security/ADR/MCP docs and this evidence log |

## Acceptance matrix A01-A28

| ID | Status | Evidence / boundary |
|---|---|---|
| A01 | PASS | Compose config/build; API and web healthy; browser reachable |
| A02 | PASS | Container touch denied by read-only filesystem; synthetic fixtures only |
| A03 | PASS | Seven required stack kinds imported and displayed; multi-Unreal test |
| A04 | PASS | Unchanged scans preserve project/event counts and revisions |
| A05 | PASS | Changed file updates only affected project; Git metadata tracked separately |
| A06 | PASS | Rename/removal history and stale evidence retained |
| A07 | PASS | Missing source root yields unavailable, no removals |
| A08 | PASS | Cancellation/recovery tests, one retried job, no partial publication |
| A09 | PASS | Browser capture with no AI or external service configured |
| A10 | PASS | React and real Chromium hostile-text tests |
| A11 | PASS | Type edits/approval alone do not verify |
| A12 | PASS | Try Next and clear flag; untested trust preserved |
| A13 | PASS | Real HTTP/MCP proposal and human-only credential restrictions |
| A14 | PASS | Browser journey makes real MCP result proposal, remains pending |
| A15 | PASS | Human-review UI exercised with explicit confirmation and indexed hashes |
| A16 | PASS | Passed + failed history produces inconclusive; explicit resolution tested |
| A17 | PASS | Edit synthetic proof, incremental scan, Needs recheck, old result preserved |
| A18 | PASS | FTS/provider/MCP tests retain provenance and separate status axes |
| A19 | PASS | Similar intentional records remain separate; explicit non-destructive links |
| A20 | PASS | Docker captures and complete experiment histories survive restart; real provider backup retains notes and outcomes |
| A21 | PASS | Missing token 401, agent review 403, Host/Origin checks; memory-only UI credential |
| A22 | PASS | Enum/version/size/path/hash boundaries and SQLite constraints |
| A23 | PARTIAL | Codex real model-driven search/profile calls PASS through global configuration; Copilot/Claude actual sessions remain untested |
| A24 | PASS | Entire local and Docker journey uses no LLM/provider key |
| A25 | PASS | Bounded scan preserves old inventory; durable byte-budget retry; no production-scale throughput claim |
| A26 | PASS | New empty project inherited user-global Codex MCP/instructions, searched real records and read a profile; no project-local config, files or shell commands |
| A27 | PASS | URLs stored as references; no URL fetch code; validation tests |
| A28 | PASS | Capture, proposal/result retries, mutation conflicts and job deduplication |

## Run and operational state

From the repository root, copy `.env.example` to `.env`, set `PROJECTS_ROOT` to a chosen directory and generate separate random human/agent secrets as described in README. Run `docker compose up -d --build --wait`. Open `http://127.0.0.1:4318`, enter the human token, register the mount in Settings, and explicitly run Initial Inventory. MCP is `http://127.0.0.1:4317/mcp`.

The smoke project is `projectbrain-smoke`. Its services were both healthy at the saved evidence point, then stopped/removed by the test cleanup. Its named volume remains; no `down -v` was used.

### Local library setup — 2026-10-25

- Configured the user-selected project directory in ignored `.env`, with separate random primary and agent credentials. No credentials are committed or printed in tool output.
- Built and started the personal `projectbrain` Compose instance. API and web are healthy on loopback ports 4317 and 4318; inspected source mount is read-only, with the database in the separate `projectbrain_projectbrain_data` volume.
- Registered the selected mounted library and explicitly queued the first real-library inventory. The original two attempts exposed a generated Trivy cache permission issue. After excluding only generated tool/package caches and improving read-only containment checks, a complete scan succeeded: **29,997 files, 25 projects**, root available, schema 4. No partial publication occurred. Current status evidence is in ignored `.local/real-library-inventory.json` and `.local/review-library-final.json`.
- Excluded `.local`, `test-results`, and `playwright-report` directories so temporary working copies and browser output do not pollute the real inventory. The targeted domain suite passes: 57 tests, zero failures or skips. Docker publish/build and both service health checks pass.
- Services are left running. User-global Codex setup was subsequently completed below; public publication is recorded in its own section.

## Remaining limits and decisions

### Project review — 2026-10-25

The user requested a fresh functional review. Initial runtime checks found both personal containers healthy, but **zero indexed projects**: both earlier library scans were `partial`. A read-only traversal inside the non-root container confirmed permission denied under a generated `artifacts/ci/trivy-cache/fanal` directory. Healthy HTTP endpoints had therefore not demonstrated a working real-library inventory.

Fixes and verification:

- PB-103/PB-104/PB-204, A03/A08/A25: exclude generated Trivy caches, installed Python `site-packages`, and arbitrarily named virtual environments with `pyvenv.cfg`. Preserve other artifact directories and experiment evidence. A new integration test verifies these exclusions and unchanged rescan behavior.
- PB-102/PB-602, A08/A22/A25: retained the scanner progress/connection-reuse changes already present at the start of this review. Directory enumeration reuses `FileSystemInfo` metadata; containment uses one metadata lookup per path segment without caching across checks. Before/after-open link checks remain. Tests cover existing/missing paths and diagnostic redaction. A separate synthetic Linux probe confirmed rejection of directory escapes, file links, dangling links, linked mounts and linked mount ancestors.
- PB-002/PB-005/PB-603, A01/A20/A21: Docker/browser tests previously collided with the running personal stack on 4317/4318. Tests now use 14317/14318, configurable URLs/ports, an explicitly validated loopback browser origin, and the separate `projectbrain-smoke` database volume. Four HTTP cases check exact configured-origin matching and rejection of unrelated origins.
- PB-604: corrected stale Docker-unavailable statements in operations documentation and documented test ports/exclusions.

| Fresh review command | Actual result |
|---|---|
| `dotnet test ProjectBrain.slnx --logger "trx;LogFileName=review.trx"` | PASS, 61 domain/parser + 42 service/HTTP/MCP/reliability = **103 tests**, no skips |
| `dotnet build ProjectBrain.slnx --no-restore` | PASS, no warnings/errors; later test and Docker builds include scanner fixes |
| `dotnet format whitespace ProjectBrain.slnx --no-restore --verify-no-changes` | PASS |
| `python tests/schema_test.py` | PASS, 8 SQLite/FTS5 tests |
| `npm.cmd --prefix frontend run build` | PASS, TypeScript and production bundle |
| `npm.cmd --prefix frontend test` | PASS, 3 React tests |
| `npm.cmd --prefix frontend run e2e` | PASS, 2 browser journeys using isolated API/fixtures alongside personal containers |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/compose-smoke.ps1 -Browser` | PASS on final scanner code, both containers healthy, non-root/read-only source, 2 browser journeys, MCP call, capture and experiment-history persistence across restart |
| `python scripts/mcp-smoke.py` with local credentials held only in the process environment | PASS against personal API: initialize, list 9 tools, real `search_knowledge`, then `get_project_profile` returned the actual indexed ProjectBrain project |
| `npm.cmd --prefix frontend audit --audit-level=moderate` | PASS, zero vulnerabilities reported |
| `dotnet list ProjectBrain.slnx package --vulnerable --include-transitive` | PASS, no vulnerable packages reported |
| `git diff --check` | PASS |

Review evidence: `tests/ProjectBrain.DomainTests/TestResults/review.trx`, `tests/ProjectBrain.Tests/TestResults/review.trx`, `.local/review-compose.log`, `.local/compose-services.json`, `.local/compose-smoke.log`, and browser `reviewed-proof.png` / `stale-proof.png` under `frontend/test-results/`. The stale-proof screenshot was visually inspected. Test-only containers were removed after success; their volume was retained. The personal stack remains running.

Additional read-only browser check: the personal Dashboard rendered the real registered source and active jobs; Settings loaded the persisted six-hour interval, with no browser errors. Screenshot: `.local/review-live-dashboard.png`. Runtime inspection confirms `app` user, read-only container root, read-only source bind and writable separate data volume (`.local/review-runtime.json`). The Linux link probe source is retained locally in `.local/path-probe`; it ran in a disposable network-disabled/read-only container with synthetic paths only.

Real-library verification is complete: the API lists 25 indexed projects; an FTS search for `repository` returns 11 matches; the Streamable HTTP MCP client initialized, listed all nine tools, called `search_knowledge`, and used `get_project_profile` to retrieve the indexed `ProjectBrain` project. At that review point Codex had no global ProjectBrain entry; the continued implementation below closes that Codex gap.

### Codex global onboarding — 2026-10-25

- Added `scripts/connect-codex.ps1`: verifies the separate agent credential against the running API, backs up existing Codex files privately, registers the loopback MCP, stores the credential in the Windows user environment, and installs one marked English global instruction block. Re-running setup left exactly one block. Secrets are absent from the repository, Codex config and AGENTS text.
- The first actual Codex session exposed missing tool annotations and a client approval barrier. Added explicit read-only/non-destructive/idempotent/closed-world metadata for seven retrieval tools and write metadata for the two proposal tools. HTTP SDK tests and the protocol smoke assert these contracts. Setup uses Codex's `writes` approval policy, permitting reads while proposals require approval; human-only API decisions remain enforced.
- Added `scripts/codex-onboarding-smoke.py`: uses the user's configured Codex model/login in one ephemeral read-only session in a new empty temporary project. There is no per-project AGENTS/config. The prompt does not name ProjectBrain. Actual result PASS: real `search_knowledge` calls (trusted and draft fallback) plus `get_project_profile`; retrieved IDs/statuses, zero shell commands, zero project files.
- Evidence: `.local/codex-onboarding-latest.json` and `.local/codex-onboarding-05568850ec/{events.jsonl,answer.txt,report.json}`. The initial blocked-client result is retained separately in `.local/codex-onboarding-0c8920cf42`. Global backups are under `~/.codex/backups/projectbrain-*`, outside the source library. These local reports and credentials are not part of the public repository.
- `dotnet test ProjectBrain.slnx --logger "trx;LogFileName=mcp-onboarding.trx"`: PASS, 65 domain/parser + 43 integration/service/HTTP = **108 tests**, no skips. `dotnet format whitespace ProjectBrain.slnx --no-restore --verify-no-changes`: PASS. The API image was rebuilt and healthy, protocol smoke passed against the running service, and all 25 projects / 29,997 indexed files survived restart.
- Agent review also revealed that nested Unity/Unreal fixtures could override a parent repository's own root manifest. Root manifests now take precedence, and rescans correct legacy classifications even when file hashes are unchanged. Five regression cases pass. Incremental refresh `26b82738443645eba0a031b4e4b25575` subsequently **succeeded**, with 30,004 observed files, 524 hashed files, 19 file changes and no error. Current inventory: **27 projects / 30,004 files**; ProjectBrain is now correctly classified `dotnet`. API and web are healthy, source available, and no jobs remain queued. Job evidence: `.local/classification-refresh.json`.
- Added copy-ready English `docs/agent-rules/AGENTS.projectbrain.md` and updated onboarding docs. An already running Codex desktop app must restart to load the user environment/tool connection. New Codex sessions are verified; Copilot/Claude model-driven sessions remain outside this validation.

### Public GitHub publication — 2026-10-25

- Prepared an English public README with a complete clone/configure/start flow, distinct human/agent token initialization, a synthetic UI screenshot, MCP setup, validation status, and documented limitations. All 13 local README targets resolve; its PowerShell credential example passed in an isolated ignored directory and did not change the personal `.env`.
- Expanded Git/Docker exclusions for private environment variants, SQLite files and Python caches. Reviewed the existing history (128 historical file blobs) for local credentials, private keys and accidentally versioned data; no matches were found. The earlier local development history is retained locally; the public `main` snapshot uses the owner's GitHub noreply identity.
- Published public repository: [Mad-Mange/ProjectBrain](https://github.com/Mad-Mange/ProjectBrain), default branch `main`. Initial public commit: `5efcd5903977fa2c162fd0421da2dfc0bd7e6d9f`. GitHub's anonymous API confirms public visibility, all 130 intended files, a noreply commit identity, and no `.env`, local reports or databases in the public tree. The previous development branch remains local.
- Verified the exact README and both images through unauthenticated public requests and checked the rendered GitHub page in Chromium. Local publication evidence is in ignored `.local/github-publication.json` and `.local/github-readme.png`.
- [First GitHub Actions run](https://github.com/Mad-Mange/ProjectBrain/actions/runs/40237162079): **PASS** on Ubuntu, including restore/build, all 108 .NET tests, frontend build and 3 React tests, both Playwright journeys, 8 SQLite schema checks, and Compose configuration validation. Full container startup/restart tests remain separately verified by the earlier Docker Desktop smoke run.

### Previously documented limits

- PB-505 / A23: actual Copilot/Claude sessions remain unverified. Codex and A26 pass through a real new-project model session; instruction adherence can still vary by model/task.
- Retries repeat directory traversal, reuse noncritical read checkpoints, and publish only complete inventories. File/depth limits require raising settings; audit mode deliberately rereads all content. Large-library throughput has not been benchmarked.
- Git worktree redirects are intentionally not followed. Binary engine assets remain metadata-only, as required. Shared-folder Unreal source is attributed to each manifest, not inferred to belong exclusively to one.
- Ambiguous project moves are retained for review without merging; a dedicated identity-resolution screen is not implemented. Markdown import/semantic embeddings and external enrichment remain outside the core workflow.
- NuGet/npm auditing reports no known vulnerable packages at the validation time; this is not a guarantee against future advisories.

Earlier blocked results were superseded by real runtime verification. The previous log is archived in ignored `.local/IMPLEMENTATION_PROGRESS.before-runtime.md` for local reference.
