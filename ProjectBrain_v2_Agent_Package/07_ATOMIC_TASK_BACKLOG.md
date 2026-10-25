# 07 — V2 Atomic Implementation Backlog — build in this order

Each PB task needs DONE/IN_PROGRESS/BLOCKED and actual test output in `IMPLEMENTATION_PROGRESS.md`. Gates map to `11_ACCEPTANCE_MATRIX_V2.md`. Create small coherent commits. Never fabricate test results.

## M0 — Repo, Docker and security baseline
- [ ] PB-001 Create .NET 10 solution: Domain/Application/Infrastructure/Api; React/TS/Vite and tests.
- [ ] PB-002 Implement two-service Docker Compose, read-only source bind mount, named SQLite volume, loopback ports, non-root and read-only container filesystem where possible.
- [ ] PB-003 Health live/ready, nginx proxy and SPA; robust startup, environment validation, logs and schema migrations.
- [ ] PB-004 Add local bearer authentication for REST/MCP, origin/host checks, avoid leaking token, safe browser token entry.
- [ ] PB-005 CI checks .NET build/test and frontend build/typecheck + Docker Compose configuration validation.
Gate: A01, A02, A21.

## M1 — Core schema and initial inventory
- [ ] PB-101 Implement `001_initial.sql` and migration runner; apply v2 extension template (see schema docs), integrity + WAL + online backups.
- [ ] PB-102 Register allowed mounted scan root(s), root path canonicalization/symlink escape protections.
- [ ] PB-103 Enumerate safely with exclusions, size, duration, file count and total byte limits.
- [ ] PB-104 Detect C#/.NET, Python, JS/TS, Docker, Unreal, Unity and Blender metadata project roots and components.
- [ ] PB-105 Save projects/files/profiles with stable IDs and source evidence.
- [ ] PB-106 Add scan job UI, project cards, profile details and basic search.
Gate: A03, A22, A24.

## M2 — Incremental change tracking
- [ ] PB-201 File fingerprint manifest using mtime/size fast path and SHA-256 audit.
- [ ] PB-202 Detect added/changed/renamed/deleted files, project revisions and change events.
- [ ] PB-203 Schedule every 6h, weekly full hash audit, manual Scan Now, on-start catch-up and cancellation.
- [ ] PB-204 Missing root + transient failure protections, resumability/idempotency and no destructive reindex.
- [ ] PB-205 When source of approved knowledge changes, stale proof and request review while preserving history.
Gate: A04–A08, A17, A25.

## M3 — Knowledge, provenance and FTS5
- [ ] PB-301 Implement knowledge records, tags, relations, review history, optimistic versioning and evidence.
- [ ] PB-302 Add SQLite FTS5 full text, ranked results and project/tech/status filters.
- [ ] PB-303 Safe deterministic extraction only; proposed or discovered is never inferred to be successful.
- [ ] PB-304 Add Knowledge UI and Review Queue with approval/rejection/deprecation and duplicate suggestions.
- [ ] PB-305 Test versioned migrations, secret redaction, stale evidence, FTS and no auto-merge.
Gate: A11, A13, A18, A19, A22.

## M4 — NEW Quick Capture, flags and experiment lifecycle
- [ ] PB-401 Implement capture persistence for full original paste, optional title/url/tags/project and idempotency; no AI needed.
- [ ] PB-402 React `+ Quick Capture` with keyboard shortcut **within web app**, Inbox, safe markdown/code rendering.
- [ ] PB-403 Add independent kind/review/validation/intent fields and validated transitions.
- [ ] PB-404 Implement `Want to try` flag, Try Next list and multi-filter search.
- [ ] PB-405 Implement experiment plans + success criteria, immutable result proposals, human review and evidence.
- [ ] PB-406 Derive `Tested & Works` ONLY after reviewed passed test with non-stale evidence. Handle failures, conflicts, versions and stale proof.
- [ ] PB-407 Add audit log and API endpoints per docs 08/09; show clear UI badges and provenance.
- [ ] PB-408 Tests capture offline, huge/malicious content, duplicate retries, try-next, review, stale and restore.
Gate: A09–A17, A19, A20, A27, A28.

## M5 — MCP integration and global agent onboarding
- [ ] PB-501 Add `ModelContextProtocol.AspNetCore` Streamable HTTP at `/mcp` sharing application layer.
- [ ] PB-502 Read tools: search_knowledge, get_knowledge_item, find_similar_solutions, get_project_profile, list_try_next, list_analysis_candidates, get_project_analysis_context.
- [ ] PB-503 Safe proposals: propose_knowledge and propose_experiment_result; human-only final verification.
- [ ] PB-504 Auth, safe bounded excerpts, no secrets, no prompt-injection trust, idempotency, consistent schema.
- [ ] PB-505 Document and validate Codex, Copilot CLI/VS Code, Claude Code global client setup. Include global instructions and sample project onboarding.
- [ ] PB-506 Agent retrospective review flow with no paid provider required; acknowledge unverifiable inference.
Gate: A13, A14, A21, A23, A26, A28.

## M6 — Operations and end-to-end quality
- [ ] PB-601 Backup/restore (SQLite online backup) including raw captures, experiment evidence/history, knowledge and scan data.
- [ ] PB-602 Settings, scan history, diagnostics, audit activity and project onboarding.
- [ ] PB-603 Docker Desktop Windows smoke, source-read-only check, service restart persistence, security and limits.
- [ ] PB-604 Documentation, sample run and validation results per A01–A28; no NOT RUN disguised as PASS.
Gate: all A01–A28 demonstrable or explicitly BLOCKED/NOT RUN.

## M7 — Optional future work, NOT blocking v2
- Embeddings/semantic search and consolidation suggestions.
- Host Windows tray client with global keyboard shortcut; browser shortcut remains in v2.
- Safe optional LLM summarization behind explicit cost/privacy budget and no private data upload by default.
- Networked remote access/TLS/RBAC and multi-user permissions.
- Version-safe advanced Blender scene inspection and binary Unreal asset analysis in isolated, opt-in workers.
