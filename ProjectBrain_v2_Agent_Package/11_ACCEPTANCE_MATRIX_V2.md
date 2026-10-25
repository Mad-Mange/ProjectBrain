# 11 — Acceptance Tests and Definition of Done (v2 — normative)

## Strategy
Core tests use temporary synthetic fixtures, not user's actual old projects. Deterministic import/search/capture must work without network or LLM keys. UI acceptance via Playwright or equivalent. SQLite tests must use a real SQLite test DB, not mock in-memory EF store, to verify FTS and transactions. Docker smoke on Windows when available. Record commands/results, never claim a test ran without executing it.

## Mandatory scenarios

| ID | Scenario | Pass requirement |
|---|---|---|
| A01 | New installation | `docker compose config`, builds, two containers healthy, app reachable |
| A02 | Read-only source | writes in source container path fail; scanner never writes originals |
| A03 | Initial import | Detect project kinds, basic profile and evidence |
| A04 | No-change rescan | No duplicated projects, knowledge or unnecessary extract jobs |
| A05 | Content edit | Detect delta, update revision and file evidence; unaffected project untouched |
| A06 | File rename/removal | Event history accurate; approved knowledge retained with stale reference |
| A07 | Root disappears | Mark root unavailable, do not delete projects/evidence |
| A08 | Scanner restart/cancellation | No partial publication; job retriable, locks released |
| A09 | Quick Capture offline | Paste with no tags/no AI, persisted; visible Inbox |
| A10 | Paste hostile HTML | Stored inert; rendered sanitized; no XSS/script execution |
| A11 | Classification | Changing `kind` to best_practice alone does NOT verify |
| A12 | Try Next flag | Idea appears Try Next; remains untested; can clear flag |
| A13 | Agent proposes lesson | Review queue populated; cannot auto-approve or self-verify |
| A14 | Agent proposes experiment result | Pending result; original status not `passed` until human review |
| A15 | Human confirms passed | `Tested & Works` derived only with proof + approval |
| A16 | Conflicting/failed result | Flag needsReview or inconclusive, preserve history |
| A17 | Change used source | Previous passed evidence stale; don't delete outcomes |
| A18 | Search | FTS includes source provenance and clear kind/review/validation/flag |
| A19 | Duplicate suggestions | Similar records linked but never auto-merged |
| A20 | Persistence | Restart containers, data intact, backup and restore recovers captures and runs |
| A21 | Auth | 401 on protected endpoints/MCP without token; no hardcoded secret in browser images |
| A22 | Input boundaries | Invalid enum/version/oversize, unsafe path, traversal rejected |
| A23 | Global MCP | Codex/Copilot/Claude compatible on local host; real `search_knowledge` call logged |
| A24 | No LLM configured | Import, inbox, search, scheduling and reviews work |
| A25 | Large scan limits | Time/byte/file ceilings enforced with resumable warnings |
| A26 | Agent onboarding | In a new empty project, after global setup an agent can find prior knowledge without per-project config |
| A27 | Safe URL capture | URL stored as text; no network fetch/SSRF |
| A28 | Retry idempotency | Same clientProposalId/mutation key not duplicated |

## Golden journey
1. Import fixture projects `BasicDotNet`, `TinyUnity`, `UnrealModule`, `PythonML`, `BlendAssets` through read-only mounts.
2. Open React UI: projects discovered with tech tags and source paths relative to root; no false claims of test success.
3. Quick Capture an Unreal idea. Choose `Idea`, flag `Want to try`, leave project empty.
4. Find idea through `Try Next` and MCP tool `list_try_next`.
5. Agent adds project association, proposes an experiment and submits a result (after tests in a TEMPORARY active project, never scanning runner).
6. Confirm result is **not** verified until human approves, and that a human-reviewed passed result with evidence shows `Tested & Works`.
7. Change referenced test file and run incremental scan; see `Needs recheck`, previous history preserved.
8. Verify duplicate similar capture does not auto-overwrite original text.
9. Restart compose; see same idea, tags, flag, revisions, evidence, experiments and review history.
10. Start a brand-new project and verify an actual MCP search call.

## CI/agent quality gates
M0: formatting, .NET build, frontend build, compose config.
M1: root validation + parser fixture tests.
M2: differential scan and error-recovery tests.
M3: schema migration, search, approval, security tests.
M4: Quick Capture UX + sanitized paste + experiment tests.
M5: MCP integration client and tool schema tests.
M6: end-to-end Docker smoke, backup/restore, Windows path validation and docs.

## Non-goals / explicitly deferred
- Autonomous LLM expense or cloud provider credential setup.
- Auto-execution of scanned project code or Blender scripts.
- Browser-based OS-global hotkeys (`Ctrl+Alt+K` tray client later).
- Embeddings/vector database until FTS5 quality measurements justify them.
- Guaranteed semantic correctness of agent-generated lessons; review is mandatory.

## Agent completion report
List each acceptance ID PASS/FAIL/NOT RUN and exact commands/outputs where possible; unexecuted tests are NOT RUN. Provide Docker service status and actual screenshot path if screenshots produced. Include known limitations, next tasks and decisions in `IMPLEMENTATION_PROGRESS.md`.
