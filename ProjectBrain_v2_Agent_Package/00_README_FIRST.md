# ProjectBrain v2 — Complete Agent Implementation Specification

**Date:** 2026-10-23 · **Version:** 2.0 · **Artifact:** Detailed build specification, working design examples and Docker/MCP starter templates; **NOT** the implemented app.

## Objective
A private, local Docker-based knowledge hub for an entire Windows project library (C#, Python, React, Docker, Unreal, Unity, Blender). Import historic project structure safely, periodically synchronize changes, retrieve reusable designs through Codex/Copilot/Claude MCP, capture ideas and best-practice candidates manually, flag **Want to try**, and track experiments to **human-reviewed Tested & Works** outcomes. Existing projects are read-only.

## What is new in v2
1. Quick Capture: paste unstructured notes, code, lessons, links or best-practice candidates without any mandatory metadata beyond text.
2. Idea Inbox, Try Next, Experiments, Review Queue, lifecycle and evidence audit.
3. **Independent axes**: `kind` vs `reviewStatus` vs `validationStatus` vs `intentFlag`; never confuse an idea with verified success.
4. `propose_experiment_result` MCP tool; agents cannot self-approve verification.
5. Global agent onboarding and optional new-project registration without editing every project.
6. Tests for all added workflows, hardened localhost bearer authentication and safe URL capture.
7. Incremental rescans every six hours remain central; weekly SHA audit, missing-drive handling, stale evidence. Quick Capture is independent of scanning.

## Locked stack
- ASP.NET Core **.NET 10 LTS**, `ModelContextProtocol.AspNetCore` for HTTP MCP.
- React + TypeScript + Vite, static site served via nginx unprivileged.
- SQLite + FTS5 and versioned migrations in a Docker named volume.
- Single API hosted worker for scans. Docker Compose with API + Web; Windows project roots are bind-mounted read-only. No mandatory external LLM or cloud connection.
- Host bindings: Web `127.0.0.1:4318`, API/MCP `127.0.0.1:4317`.
- Bearer token required for protected REST/MCP endpoints. Human actions in UI are authenticated; agent write tools create **proposals only**.

## READ ORDER (agent)
1. `05_AGENT_EXECUTION_PROMPT.md`: master build mandate and sequencing.
2. `01_IMPLEMENTATION_BLUEPRINT.md`: architecture, scanners, layout; v2 note takes precedence over its historical v1 life-cycle shorthand.
3. `02_SCANNER_INCREMENTAL_SYNC.md`: mandatory differential rescans.
4. `03_DATA_MODEL_AND_CONTRACTS.md`: base schema/search and v2 amendments.
5. `08_QUICK_CAPTURE_AND_IDEA_INBOX.md`: complete UX and capture API.
6. `09_LIFECYCLE_EXPERIMENTS_AND_CONTRACTS.md`: **authoritative** v2 enums, MCP and experiment contracts.
7. `10_ONBOARDING_DOCKER_AND_MCP.md`: global agent, auth, local Docker.
8. `04_SECURITY_AND_TESTS.md` and `11_ACCEPTANCE_MATRIX_V2.md`.
9. `12_UI_INFORMATION_ARCHITECTURE.md` and `07_ATOMIC_TASK_BACKLOG.md`.
10. `templates/` contains **starter examples only**. Adapt to generated repo, test and do not claim it was already built.

## Priority/precedence for conflicts
**v2 authoritative files 08–12 > v2 updated top sections of 00/03/05/07 > old base v1 details in 01/02/03/04/06 > starter templates.** New client SDK docs override obsolete syntax after verification. Historical `knowledge_items.state` is superseded by independent v2 fields; no agent may set `verified` directly. A v1 `verified` entry without auditable tests must require review, not be blindly promoted.

## Deliverable from implementing agent
A new working Git repo containing .NET/React source code, migrations and tests, Docker Compose, scanner, UI, MCP, Quick Capture, experiments, traceability, global integration instructions, configuration and verified build/test logs. An implementation checklist tied to acceptance IDs A01–A28 must be kept. Do not generate only another plan.

## Windows first run (after code is implemented)
1. Install Docker Desktop with WSL 2.
2. Set `.env` `PROJECTS_ROOT=D:/Projects` to your real location and provide random `PROJECTBRAIN_AUTH_TOKEN` (never commit).
3. `docker compose config --quiet` then `docker compose up -d --build`.
4. Open `http://127.0.0.1:4318`, log in with token and run Initial Inventory.
5. Test Quick Capture and Scan Changed, then configure Codex/Copilot/Claude MCP globally and verify a real tool call.

## Explicit non-goals
Do not modify or execute historic project code. Do not open .blend files via arbitrary code or parse binary .uasset/.umap deeply in v2. No automatic internet URL fetching; no external LLM key required. No unconditional promotion of ideas, best practices or agent experiment claims. Windows OS-wide Quick Capture hotkey is deferred to a separate tray app.
