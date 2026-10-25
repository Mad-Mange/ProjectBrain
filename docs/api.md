# API and schema

All protected endpoints are under /api/v1 and require Authorization: Bearer. JSON uses camelCase. Unknown body fields are rejected, including attempted isVerified or validationStatus writes on capture/proposal contracts. Errors are problem-details responses: 400 invalid input, 401 missing credential, 403 disallowed actor/origin, 404 missing reference, 409 version/idempotency conflict, 429 rate limit, 503 missing source.

Human knowledge edits, review, intent, plans, experiment transitions, relation writes and result reviews require Idempotency-Key and contentVersion. Capture accepts an optional Idempotency-Key. Agent proposals require clientProposalId. Retrying an identical stored mutation returns its original response; reusing a key with different content returns 409. Root registration deduplicates canonical path; scan enqueue deduplicates active jobs. Settings and root toggles also require contentVersion and Idempotency-Key. Read their current version before updating.

## Main routes

| Route | Behavior |
|---|---|
| GET /dashboard, /diagnostics | Counts and local health |
| GET/POST /roots; PATCH /roots/{id} | Register relative mounted subpaths; schedule enablement |
| GET /projects; GET /projects/{id}; GET /projects/{id}/changes | Inventory, cached metadata, provenance and deltas |
| POST /scans; GET /scans; GET /scans/{id}; POST /scans/{id}/cancel | Queued incremental/full_audit scan jobs |
| POST /captures; GET /captures; GET/PATCH /captures/{id} | Raw capture, Inbox, metadata revisions |
| POST /captures/{id}/suggest; GET /captures/{id}/similar | Deterministic duplicate suggestions, no merge |
| GET /knowledge/search, /knowledge/try-next, /knowledge/review-queue | FTS and lifecycle filters |
| GET/PATCH /knowledge/{id}; POST /knowledge/proposals | Detail, human edits and agent proposals |
| POST /knowledge/{id}/review; PATCH /knowledge/{id}/intent | Human review and intent |
| POST /knowledge/{id}/experiments; GET /knowledge/{id}/experiments | Accepted plans and history |
| GET /experiments; PATCH /experiments/{id} | List, start or cancel |
| POST /experiments/{id}/result-proposals | Pending result with indexed test/log evidence |
| POST /experiments/{id}/review-result | Human accepts/rejects with evidence confirmation |
| POST /knowledge/{id}/resolve-validation | Human resolves conflicting/current scope with a reviewed run |
| POST /knowledge/{id}/relations | Explicit non-destructive relation |
| GET /activity; GET/PATCH /settings | Audit and persisted scanner settings |
| POST /backups; GET /backups/{id}; GET /export | Online backup and portable JSON reference export |

Search parameters: q, kind, reviewStatus, validationStatus, intentFlag, tag, projectId, needsReview, page and limit. Page size is capped at 50; MCP knowledge results are capped at 10 and 16k serialized characters. SQL parameters and literal FTS lexemes prevent query-language injection.

Captures retain at most 64 KiB UTF-8 of original text, 200 title characters, 30 tags of at most 60 characters, and an HTTP(S) source reference of at most 2048 characters. Source references are never fetched. Markdown/code is rendered as escaped plain text.

Evidence must reference a known project ID and project-relative indexed text path. Absolute paths, traversal, weak binary fingerprints, unknown files and mismatched hashes are rejected. The server fills a missing hash from the current indexed manifest; this is a recorded observation, not proof of test correctness.

## Schema migrations

001_initial.sql creates roots, jobs, stable project IDs, cached file extracts, revision snapshots, changes, base knowledge, tags, relations, evidence, reviews, audit, settings, retry receipts and FTS5.

002_quick_capture_and_experiments.sql adapts the supplied v2 migration: independent axes, immutable raw captures, experiment plans, result proposals and historical activity. Old state=verified becomes approved/untested with needs_recheck, never automatically passed.

Connection settings enable foreign keys, WAL and a 30-second busy timeout. Writers are serialized in process, with BEGIN IMMEDIATE transactions. FTS updates share their knowledge mutation transaction. File extracts are cached in file_index; component lists are in immutable project profile snapshots.

Experiment runs are mutable only while accepted/running. Once completed, their result and evidence remain immutable through the API. Result review metadata is appended/recorded separately. Current validation may become stale/inconclusive without changing historical outcomes.


003_scan_checkpoints.sql adds durable unpublished read checkpoints. 004_project_source_paths.sql distinguishes logical manifest identities from physical evidence directories. Current schema version is 4. Restore accepts versions 1 through 4 into a new destination; startup applies any pending migrations.
