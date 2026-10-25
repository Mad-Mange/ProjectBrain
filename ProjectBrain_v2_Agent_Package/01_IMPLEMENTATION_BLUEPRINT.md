# 01 — ProjectBrain Implementation Blueprint (updated for v2)

> **V2 precedence:** The milestone numbering in this historical blueprint is superseded by `07_ATOMIC_TASK_BACKLOG.md` v2; Quick Capture/Experiments is M4 and global MCP is M5. Product scope also includes Quick Capture, Idea Inbox, Try Next, Experiments and separate Type/Review/Validation/Intent. Implement documents `08`, `09`, `10`, `11`, `12` as normative. The historical v1 lifecycle shorthand below is superseded by doc `09`.

## 1. Product brief

ProjectBrain is a private, local knowledge platform which continuously maps the user's software/game/modeling projects and makes past architectures, implementation patterns, problems and proven resolutions searchable by human and coding agents.

### Core user stories
- **US-01 Import:** As a user, I select my old project folder and receive an inventory without manually tagging projects.
- **US-02 Explain:** I can open a project and see detected technologies, high-level architecture, important subsystems and evidence paths.
- **US-03 Change sync:** After I update an existing project, ProjectBrain notices file/commit changes and refreshes affected project profiles; unchanged projects are skipped.
- **US-04 Historical context:** A project timeline records major changes, renames and metadata, preserving previous knowledge rather than overwriting provenance.
- **US-05 Discover:** When beginning work, my Codex/Copilot/Claude agent can search existing relevant solutions and receive concise citations instead of a dump of source code.
- **US-06 Contribute:** Agents can propose a new lesson (what/why/solution/evidence) without publishing it as verified.
- **US-07 Approve:** I can accept, reject, merge, tag, edit and deprecate proposed lessons.
- **US-08 Consolidate:** Similar solutions from different projects can be cross-linked and optionally consolidated into a best practice.
- **US-09 Privacy:** No project contents are transmitted outside the local system unless I explicitly enable and authorize an external AI provider.
- **US-10 Recover:** I can back up/export the database and restore it without losing the history or approval decisions.
- **US-11 Quick Capture:** I can paste thoughts/snippets/web references in seconds with no mandatory AI, project, title or tags.
- **US-12 Try Next:** I can flag an untested idea I want to test and find it later.
- **US-13 Test & Verify:** I can plan experiments, assess agent-proposed outcomes and explicitly confirm successes with evidence.
- **US-14 Automatic availability:** With global MCP instructions my agents can seek previous learning in completely new projects.

### Non-goals for MVP
- Do **not** execute imported projects, restore dependencies, run arbitrary build scripts or run code inside them.
- Do **not** auto-commit, modify, delete, or refactor source files in scanned projects.
- Do **not** infer build success or lesson quality solely from code presence.
- Do **not** require Azure, remote vector DB, cloud LLM, paid tokens, Kubernetes or GitHub account.
- Do **not** implement live Blender geometry parsing or Unreal asset binary decoding in MVP.
- Do **not** make a system-wide service that runs as administrator.
- Do **not** let an indexing error silently erase approved knowledge.

## 2. Architecture

```
               Windows 11 project directories (read only)
           D:\Projects\...     E:\Archive\... (optional later)
                     |              |
             Docker bind mounts :ro (only approved roots)
                     |
      +--------------v------------------------------------+
      | ProjectBrain.Api (.NET 10, ASP.NET Core)           |
      |                                                   |
      |  REST API + HTTP MCP at /mcp                      |
      |  Application services: Search/Knowledge/Projects  |
      |  Hosted scanner: Scheduler / Discovery / Extract  |
      |  Scan queue (one active scan per root)            |
      |  SQLite + FTS5 /app/data/projectbrain.db          |
      +---------------+-----------------------------------+
                      | internal Docker network
      +---------------v-----------------------------------+
      | React/TypeScript app, Nginx                        |
      | Home | Projects | Project Detail | Changes        |
      | Knowledge | Inbox | Try Next | Experiments | Review Queue | Settings | Scan Jobs    |
      +---------------------------------------------------+
        ^ browser http://127.0.0.1:4318

      Codex / Copilot / Claude ---> localhost:4317/mcp
```

Keep the domain independent of UI, HTTP, LLM or disk traversal. Use interfaces and dependency injection, but resist needless microservices.

### Solution repository layout

```
ProjectBrain/
  ProjectBrain.slnx (or .sln)
  src/
    ProjectBrain.Domain/           # entities, enums, invariants
    ProjectBrain.Application/      # use cases, contracts, validation
    ProjectBrain.Infrastructure/   # SQLite, migrations, filesystem adapters, Git metadata
    ProjectBrain.Api/              # controllers/minimal API, MCP handlers, hosted scheduler
  frontend/
    src/
      app/
      components/
      pages/
      api/
    package.json / package-lock.json / vite.config.ts
  tests/
    ProjectBrain.UnitTests/
    ProjectBrain.IntegrationTests/
    fixtures/                    # synthetic C#, Python, Unity, Unreal, Blender-like project fixtures
  docs/
    decisions/                    # ADRs for departures
    schema.md
    operations.md
    threat-model.md
  compose.yaml
  Dockerfile.api
  Dockerfile.web
  nginx.conf
  .env.example
  .gitignore
  README.md
  AGENTS.md
```

### Architecture boundaries
- **Domain:** project identities, file change events, knowledge states, evidence, scan job state.
- **Application:** ScanRootService, ProjectDiscoveryService, FingerprintService, IndexingService, KnowledgeReviewService, SearchService, BackupService.
- **Infrastructure:** SQLite/EF Core migrations and SQL FTS5, safe readonly filesystem reader, optional read-only Git commands (or lightweight Git metadata parser), parsers/adapters.
- **API:** routes, validation, lifecycle, health, MCP tool mapping; `BackgroundService` for scheduled jobs + queues. Serialize scan jobs and database writing in v1.
- **Frontend:** thin React UI with typed API client; request polling, not permanent websocket infrastructure.

### Libraries
- .NET 10 / ASP.NET Core; official `ModelContextProtocol.AspNetCore` package for Streamable HTTP.
- SQLite storage with `Microsoft.Data.Sqlite` (or EF Core SQLite + migrations; explicitly handle FTS5 via SQL migrations).
- JSON: `System.Text.Json`; validation via built-in API or appropriate established library.
- `Microsoft.Extensions.Hosting` for scheduler, `TimeProvider` for testable clocks.
- React + Vite + TypeScript; test via Vitest + React Testing Library.
- xUnit (or NUnit; select one) for .NET tests; integration tests should use temporary SQLite database.

No embeddings required at first. Search uses FTS5 (`bm25`), filters and boosting. Design `ISemanticSearchProvider` behind feature flag for phase 2.

## 3. Scanner design overview

- Discover *project boundaries*, not every `.csproj` or `.blend` as a separate top-level project.
- Parse using deterministic adapters by file types; never invoke source projects.
- Produce a `ProjectProfile` and indexed file manifest with source references.
- Refresh changed projects through fingerprints + selective reparse.
- Maintain scan timeline and every knowledge item's evidence, with stale/reverification flags when supporting sources change.
- Background interval 6h, configured audit interval 7 days. Scan on demand; optional scan on startup if previous scan is old, but not if user explicitly paused scans.

The exact state machine and decision table are in `02_SCANNER_INCREMENTAL_SYNC.md`.

## 4. Knowledge lifecycle

**DEPRECATED v1 shorthand**: do not implement one `state` ladder. The v2 data model has independent `kind`, `reviewStatus`, `validationStatus`, `intentFlag` and `needsReview`. `Tested & Works` must be derived only from human-reviewed successful experiment evidence; see `09_LIFECYCLE_EXPERIMENTS_AND_CONTRACTS.md`.

Knowledge categories:
- `project_profile` — summary of structure, platform, components, dependencies.
- `reusable_solution` — implementation/pattern, prerequisites and limitations.
- `lesson_learned` — problem, symptoms, root cause (if known), resolution, outcome, verification.
- `best_practice` — cross-project consolidation with citations and exceptions.
- `architecture_decision` — rationale and trade-offs, not simply a code layout.

Each record: title, short summary, detailed description, technology tags, applicable contexts, quality/state, confidence, source project(s), source file line/path/hash/commit, date, author type, revision and supersedes relation.

### Avoid bad auto-generated knowledge
- Deterministic scan can say `uses Serilog`, but **not** `Serilog was successful` without evidence.
- Agent proposals require concrete observations, relevant source paths and optionally test evidence.
- Deduplication computes normalized title/topics + keyword overlap; candidates are suggestions, not destructive auto-merges.
- An update to project files that invalidates evidence sets `needs_review`, not `deleted`.

## 5. Frontend pages / user flows

### Dashboard
- Total indexed projects, last successful scan, pending proposals, changed projects, unread scan issues.
- **Scan Now** (manual incremental), **Full Audit** (hash all eligible files), **Pause Automatic Scans**.
- Scan progress and recent job results (file counts, project counts, skipped count, parse errors).

### Projects
- Search + tags + type filter; each card shows name, platform, last modified, last scanned, status.
- Project detail: detected frameworks and subsystems, important files, dependencies, snippets/evidence, change history, associated knowledge.
- Changes: Added/Modified/Removed/Renamed and previous/current digest; highlight architecture-affecting changes.

### Knowledge
- Search results with context, confidence, state and source evidence; filter by engine/language/category.
- Review Queue: edit, approve, reject, link/merge, human-review experiment outcomes, deprecate; all decisions audited. Agent cannot mark verified.
- Best Practice suggestion: show grouped items; never auto-merge.

### Settings
- Approved paths within pre-mounted roots; scan interval and quiet hours; limits; exclusion patterns; optional local/cloud AI provider; export and backup.
- If user asks to add a Windows folder not mapped into container: show clear guidance to update `compose.yaml` and recreate the API container; the UI must **not** pretend it can mount arbitrary host folders.
- No API keys in settings screen unless secure storage flow exists.

### UI principles
- Responsive, simple and readable dashboard, minimal dependencies, loading/error/empty states, paginated results.
- Show provenance and *how confident is this?* instead of confidently invented conclusions.
- Settings allow scan interval to be changed without recompiling, persisted safely.

## 6. Project detectors (v1)

| Project type | Detection | Deterministic extraction |
|---|---|---|
| .NET | `.sln`, `.slnx`, `.csproj`, `.fsproj` | SDK target framework, packages, project references, hosted services, API entrypoints (heuristic) |
| Python | `pyproject.toml`, `requirements*.txt`, `Pipfile`, `.py` | dependencies, packaging, key modules, AI/ML libraries, services |
| JS/TS/React | `package.json`, `vite.config.*`, `tsconfig.json` | dependencies, scripts (do not execute), framework, route/component layout |
| Unreal | `.uproject`, `.uplugin` | engine/plugins/modules, `Source/`, `Config/`, asset name/type counts, C++ classes |
| Unity | `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` | engine version, packages, `Assets/**/*.cs`, scenes/prefabs metadata |
| Blender | `.blend`, `.blend1`, `.py`, export files | metadata (names/counts/sizes/dates) and Python scripts; no geometry parser in v1 |
| Docker | `compose.y*ml`, `Dockerfile*` | service, dependency, volume, healthcheck and env variable *names*; no secret values |
| Generic | README, build scripts, config, source files | title, language mix and folder architecture |

Nested Unity `.csproj` is a component of the Unity root, not a separate project. Unreal plugin boundaries likewise follow parent project unless independently marked.

## 7. Phases with acceptance gates

### M0 — Repo + Docker hello-world
- Create solution, API `/health/live`, `/health/ready`, empty React shell, Dockerfiles, Nginx config, Compose, README and `.env.example`.
- Docker config valid, containers build/boot; volumes read-only; browser loads on 4318.
- Quality: format/lint/test hooks, CI-equivalent local scripts.

### M1 — Database + roots + inventory
- Implement migrations, roots, file manifest, scan job state, path guards and ignore defaults.
- Scanner recognizes fixture projects across languages, works without API keys.
- UI list detected projects; 100% unchanged second scan reports zero changed eligible files.

### M2 — Incremental sync + history
- Scheduler every 6h, manual scan, weekly hash audit, job cancellation/recovery.
- File events `added/modified/removed/renamed`, project revisions, unavailable root semantics, stale evidence.
- Unit/integration test for edit, rename, remove, move, inaccessible root and interruption.

### M3 — Project profiles + knowledge extraction
- Implement adapters, profile recomputation, evidence store, discovered knowledge candidates, FTS indexing.
- UI profile page, knowledge list and review workflow.
- Never index secret values; no binary loading; no code execution.

### M4 — MCP + agent instructions
- HTTP MCP tools (`search_knowledge`, `find_similar_solutions`, `get_project_profile`, `get_knowledge_item`, `propose_lesson`, optional `report_outcome`).
- Same service/repository API used by REST and MCP, not duplicate business logic.
- Sample client configs and verified calls from at least one available client; template docs for remaining clients.

### M5 — Operator polish and robust tests
- Health, diagnostics, retry, job progress/cancel, structured logs, backup/export/restore, settings and performance bounds.
- E2E demonstration: change an old project -> incremental scan -> updated profile -> agent finds changed solution -> proposes lesson -> UI approval.

### M6 — Optional enhanced intelligence (separate toggle)
- Curated LLM summarization with provider adapter, redact previews, cost budgets; optional embeddings and consolidated best practices.
- Later optional Blender background metadata worker **isolated** and user opt-in.
- Optional Windows host-native lightweight indexing agent if large Windows mounts cause slow scans.

**Stop after each milestone** to run tests; continue if green. If an external credential or engine executable is unavailable, complete deterministic MVP and mark the optional feature skipped rather than blocking delivery.

## 8. Performance goals (not unverified promises)
- A rescan of unchanged projects should avoid full contents of unchanged files, avoid LLM calls and avoid unnecessary DB rewrites.
- File inventory uses batched reads, bounded concurrency and stable sorting for deterministic tests.
- Search results limited by default to 5–10 short, ranked entries and capped payload size (default 16k characters) to save coding-agent tokens.
- Default parser maximum 1 MiB per text file (configurable), separate per-run aggregate byte/time budget. Keep metadata for larger skipped files.
- Record scan counts and timings and present them; optimize based on measured large-folder performance, not guessed numbers.
- UI and read-only MCP should remain responsive while long scans execute.

## 9. Data ownership and portability
- SQLite named volume persists across container restart. Prefer backup using SQLite online backup API or `VACUUM INTO` in a coordinated snapshot, **not** copying a live WAL DB file on its own.
- Export knowledge as versioned Markdown/JSON bundles with source references; import supports duplicate-safe stable IDs.
- Do not place database file under scanned projects.
- Knowledge remains available if original drive is unplugged.
- Document how to upgrade schema and restore after migration failure.

## 10. Definition of done
- From clean clone and user-provided source root, `docker compose up --build` starts UI/API and data persists after restart.
- Initial inventory and subsequent incremental rescan operate against multi-language synthetic fixtures.
- Real project paths are only read, never modified.
- New/changed/missing source handling is reliable and non-destructive.
- Quick Capture, Try Next and experiment lifecycle work without an LLM, and all review/validation axes stay separate.
- MCP can search across previous projects, expose bounded/safe context for agent-driven retrospective analysis and propose a lesson.
- User can review, approve, and verify lessons with provenance, never blind promotion.
- Tests + documented run steps demonstrate all above; agent reports exact pass/fail results.
