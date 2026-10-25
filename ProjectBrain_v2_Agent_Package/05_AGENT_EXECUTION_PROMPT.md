# 05 — COPY THIS TASK INTO CODEX / COPILOT / CLAUDE

Implement a **complete runnable ProjectBrain v2 application** from the accompanying numbered specification files. This is a build request, not a request to produce another planning document. The supplied ZIP contains docs and templates only, not built code. Respect all locked stack choices, keep historic project folders read-only, and follow milestones in `07_ATOMIC_TASK_BACKLOG.md`.

## Required features
1. .NET 10 ASP.NET Core REST API + official C# MCP SDK Streamable HTTP `/mcp` + single background scanner; SQLite + FTS5 and versioned migrations; React + TypeScript frontend; Docker Compose with API/web and persistent volume.
2. Initial inventory across C#/Python/JS/TS/Unreal/Unity/Blender/Docker with deterministic safe metadata extraction (no arbitrary source execution).
3. **Reliable 6-hour incremental rescans**, weekly SHA-256 audit, root absent protection, file/project revisions, stale knowledge evidence, manual trigger, scan cancellation/recovery.
4. Cross-project searchable architecture, patterns, lessons and solutions with source provenance and clear uncertainty.
5. **Quick Capture** modal/page: paste idea / best practice / lesson / reusable solution; immediately save raw note even offline/no AI; optional tags, source URL, project, `Want to try` flag; Inbox, Try Next list.
6. **Experiment lifecycle**: independent `kind`, `reviewStatus`, `validationStatus`, `intentFlag`; experiment plan, proposal, user review, passing/failing evidence, derivation of `Tested & Works`, retained history and recheck after source changed.
7. REST + MCP for searching and proposing knowledge and experiment results. Agent tools MUST NOT directly approve, verify or publish tests. Duplicate detection suggests, never auto-merges.
8. Global MCP onboarding for Codex, GitHub Copilot CLI/VS Code, Claude Code; human-readable instructions. Authentication/localhost isolation, no secrets checked into repo.
9. Unit/integration/frontend/security/Compose tests and clear operator docs. No external model dependency by default.

## Work discipline
- Read `00_README_FIRST.md`, the blueprint and all v2 normative docs before coding. If old docs conflict with v2, v2 wins.
- Create `IMPLEMENTATION_PROGRESS.md` with atomic PB task IDs + acceptance IDs A01–A28, statuses and actual test commands.
- Build milestones in sequence; run build/test/lint at each gate; fix failing tests, document environmental blockers.
- Copy/adapt `templates/` into implemented repo; **do not assume templates compile as-is**. Implement `001_initial.sql` plus v2 migration, test clean and upgrade paths.
- Use synthetic fixtures; test paths are read-only. Never execute code or run build tools in historical source projects.
- Keep agent context/tokens efficient; reuse services between MCP and REST, return bounded results and summaries not full raw code.
- Capture user notes quickly, never silently send data to third-party models or fetch external URLs. Do not store keys in UI assets or logs.
- If Docker is unavailable report NOT RUN rather than PASS. If incomplete, deliver working partial code + accurate open tasks; don't stall asking broad clarifications.

## Finish report
Include source tree paths, complete runnable commands, Docker service states, test results tied to acceptance IDs, demonstrated Quick Capture and Try Next, documented human-approved Tested & Works test, scan-change test, one MCP agent test, data persistence across restart, remaining known limitations, and any ADR deviations. Do not say the old project library was scanned if you only scanned fixtures.

## One-line starter message
> Read `05_AGENT_EXECUTION_PROMPT.md` and the numbered specs. Implement ProjectBrain v2 end-to-end using Docker, including Quick Capture, flags, experiments, continuous rescans and global MCP. Write code, build, run tests and maintain `IMPLEMENTATION_PROGRESS.md`; do not merely plan.
