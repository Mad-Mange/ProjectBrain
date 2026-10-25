# 09 — Type, Review, Intent, Validation and Experiment Evidence (v2 — normative)

## Core distinction — never collapse these axes

| Field | Enum | Meaning |
|---|---|---|
| `kind` | `idea`, `best_practice`, `lesson_learned`, `reusable_solution`, `project_profile` | What it **is**, not whether true |
| `reviewStatus` | `draft`, `proposed`, `approved`, `rejected`, `deprecated` | Human review / suitability for reuse |
| `validationStatus` | `untested`, `planned`, `running`, `passed`, `failed`, `inconclusive`, `stale` | Empirical evaluation and current evidence quality |
| `intentFlag` | `none`, `explore`, `try_next`, `reuse_when_relevant` | Human next-action preference |
| `confidence` | `low`, `medium`, `high` | Evidence quality, not a substitute for tests |
| `needsReview` | boolean | Evidence changes, possible stale result or disputed findings |

### Derived UI badges
- **Idea**: `kind=idea` (can be tested or untested).
- **Want to try**: `intentFlag=try_next` (regardless of current validation status, but show both labels).
- **Tested & Works**: ONLY `reviewStatus=approved` AND `validationStatus=passed` AND at least one human-confirmed positive experiment with evidence that is NOT stale. This is a **derived** property, never an agent-writable enum.
- **Best Practice**: `kind=best_practice`, display review and validation separately. An untested Best Practice candidate cannot appear as verified.
- **Needs recheck** if evidence changes, even when history contains passed runs.

## Allowed workflow and ownership
- Human Quick Capture -> `draft + untested`.
- Agent `propose_knowledge` -> `proposed + untested` (unless existing reviewed evidence is linked, but cannot mark verified).
- Human approve/reject/deprecate -> `reviewStatus` transition with audit entry and reason.
- Human set/clear `intentFlag`. Agent can **propose** an intent change, cannot silently alter it.
- Experiment proposal -> `planned` only upon human acceptance; running is set by human or authorized agent orchestration after acceptance.
- Agent `propose_experiment_result` -> **pending result proposal**; DO NOT set `validationStatus=passed` from this alone.
- Human review outcome -> `passed`, `failed`, `inconclusive` with a linked immutable experiment run and evidence. Multiple conflicting results: status `inconclusive` or `needsReview=true` until resolved.
- Source edit affecting evidence -> `needsReview=true`, `validationStatus=stale` when current proof no longer valid; preserve run results and old source hashes. Reverify to restore `passed`.
- Agent tools never invoke `approve_knowledge`, `set_validation_passed` or other human decision operations.
- All mutations require `contentVersion` optimistic concurrency, idempotency key, actor, timestamp and audit.

## Experiment run data
```
experiment_run_id UUID
knowledge_id FK
project_id FK nullable
status proposed | approved | running | completed | cancelled
outcome passed | failed | inconclusive | unknown
hypothesis TEXT
success_criteria TEXT (defined before run)
procedure_md TEXT
observed_result_md TEXT
limitations_md TEXT
started_utc, ended_utc nullable
actor, reviewer nullable
environment_json (engine/runtime/framework/version/OS)
evidence_json: links to tests/log paths/git commit/file hashes, NO raw secrets
```
Add `experiment_result_proposals`: submitted_by, project, run_id, proposed_outcome, evidence, review_status, idempotency_key, created_utc. A proposal is not a reviewed result.

## REST endpoints (under `/api/v1`)
- `POST /knowledge/{id}/experiments`: human creates accepted experiment plan with measurable success criteria.
- `GET /knowledge/{id}/experiments`: list plans/results including evidence and reviewer.
- `POST /experiments/{id}/result-proposals`: agent or human submits proposed outcomes; 202/201; no auto-verify.
- `POST /experiments/{id}/review-result`: human decision `accept|reject` and explicit outcome/evidence check.
- `PATCH /knowledge/{id}/intent`: human sets `try_next|explore|reuse_when_relevant|none`.
- `POST /knowledge/{id}/review`: human approves/rejects/deprecates content.
- `GET /experiments?status=&outcome=&projectId=`: list running/awaiting review/completed.
- `GET /activity`: combined audit for captures, changes, review and experiment transitions.
- Existing v1 REST and MCP from `03_...` remain in effect except older `state` is superseded by `reviewStatus` + `validationStatus` + `intentFlag`. Avoid returning `verified` as a mutable state. `isVerified` is computed.

## MCP tool contracts (v2 supersedes v1 variants)
- `search_knowledge({query, technologies?, kinds?, includeIdeas?:false, statusFilter?:"trusted|all", limit?:5})`: ranked bounded results; default trusted approved results; include ideas only if explicitly requested or they are strongly relevant, clearly flagged; if none, state zero matches.
- `get_knowledge_item({knowledgeId})`: raw note only if authorized, bounded to max content; evidence, statuses, intent, and source revision.
- `find_similar_solutions({problem, technologies?, limit?:5})`: suggest solutions with reliability info.
- `get_project_profile({projectId})`: current project profile and change summary.
- `list_analysis_candidates({changedOnly?,limit?})` and `get_project_analysis_context({projectId,maxChars?})`: for retrospective offline review with redaction.
- `propose_knowledge({kind,title,summary,body,projectIds?,tags?,evidence?,clientProposalId})`: always sets `reviewStatus=proposed`, no self-approval.
- `list_try_next({technologies?,projectId?,limit?})`: read-only user-selected experiment ideas.
- `propose_experiment_result({knowledgeId,experimentId,proposedOutcome,observations,evidence,clientProposalId})`: draft result for human review only.

Keep tool count small by consolidating as needed. All responses must include bounded length, truncation metadata, `isVerified` computed, type/status/flag, stale source indicator, no absolute filesystem paths by default.

### Sample MCP search result
```json
{
  "results": [{
    "id": "kn-42", "kind": "idea", "title": "Headless Unreal training core",
    "summary": "Consider extracting deterministic gameplay rules for DQN",
    "reviewStatus": "draft", "validationStatus": "untested", "intentFlag": "try_next",
    "isVerified": false, "needsReview": false, "confidence": "low",
    "evidence": [], "sourceProjects": []
  }],
  "total": 1, "truncated": false
}
```

## Duplicates / consolidation
- Detect similarity (FTS now, embeddings future); show similarity candidates without auto merge.
- Track `related_to`, `duplicate_of`, `supersedes`, `contradicts` as relation types.
- Never replace a human's raw capture with generated summary; keep append-only provenance.
- Different versions of tooling/engines (e.g. Unity vs Unreal) may share a pattern, but do not imply code is portable.

## SQLite implementation detail
Use existing v1 tables and add fields/tables in `templates/schema/002_quick_capture_and_experiments.sql`; agent MUST adapt and create a complete `001_initial.sql` to support new databases, version migration, FTS5 updates and transaction-safe audit. The SQL file is a **migration design template** (not verified against a built app).
