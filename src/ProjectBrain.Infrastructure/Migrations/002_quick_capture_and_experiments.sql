-- Design migration template for ProjectBrain v2.
-- Assumes `001_initial.sql` created v1 `knowledge_items` INTEGER PK, `projects` TEXT PK,
-- `knowledge_evidence` and `knowledge_reviews` tables. Implement in versioned migration
-- and verify with tests; DO NOT run manually against an unknown database.
-- Do not wrap this file in a second BEGIN/COMMIT if the migration runner handles transactions.

ALTER TABLE knowledge_items ADD COLUMN review_status TEXT NOT NULL DEFAULT 'draft'
  CHECK (review_status IN ('draft','proposed','approved','rejected','deprecated'));
ALTER TABLE knowledge_items ADD COLUMN validation_status TEXT NOT NULL DEFAULT 'untested'
  CHECK (validation_status IN ('untested','planned','running','passed','failed','inconclusive','stale'));
ALTER TABLE knowledge_items ADD COLUMN intent_flag TEXT NOT NULL DEFAULT 'none'
  CHECK (intent_flag IN ('none','explore','try_next','reuse_when_relevant'));
ALTER TABLE knowledge_items ADD COLUMN needs_recheck INTEGER NOT NULL DEFAULT 0 CHECK (needs_recheck IN (0,1));

-- Older v1 state was a blended lifecycle enum. Never infer real test evidence from it.
UPDATE knowledge_items SET review_status = CASE state
  WHEN 'discovered' THEN 'draft'
  WHEN 'proposed' THEN 'proposed'
  WHEN 'approved' THEN 'approved'
  WHEN 'verified' THEN 'approved'
  WHEN 'rejected' THEN 'rejected'
  WHEN 'deprecated' THEN 'deprecated'
  ELSE 'draft' END;
-- Previously 'verified' must be reviewed for actual experiment evidence, not blindly passed.
UPDATE knowledge_items SET needs_recheck=1 WHERE state='verified';

CREATE TABLE IF NOT EXISTS captures (
  id TEXT PRIMARY KEY,
  knowledge_id INTEGER NOT NULL UNIQUE REFERENCES knowledge_items(id) ON DELETE RESTRICT,
  original_text TEXT NOT NULL CHECK (length(original_text) > 0),
  source_url TEXT NULL,
  source_type TEXT NOT NULL DEFAULT 'manual',
  creator TEXT NOT NULL,
  idempotency_key TEXT NULL UNIQUE,
  content_version INTEGER NOT NULL DEFAULT 1,
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_captures_created ON captures(created_utc DESC);

CREATE TABLE IF NOT EXISTS experiment_runs (
  id TEXT PRIMARY KEY,
  knowledge_id INTEGER NOT NULL REFERENCES knowledge_items(id) ON DELETE RESTRICT,
  project_id TEXT NULL REFERENCES projects(id) ON DELETE SET NULL,
  status TEXT NOT NULL CHECK (status IN ('proposed','approved','running','completed','cancelled')),
  outcome TEXT NOT NULL DEFAULT 'unknown' CHECK (outcome IN ('unknown','passed','failed','inconclusive')),
  hypothesis TEXT NOT NULL,
  success_criteria TEXT NOT NULL,
  procedure_md TEXT NOT NULL DEFAULT '',
  observed_result_md TEXT NOT NULL DEFAULT '',
  limitations_md TEXT NOT NULL DEFAULT '',
  environment_json TEXT NOT NULL DEFAULT '{}',
  evidence_json TEXT NOT NULL DEFAULT '[]',
  created_by TEXT NOT NULL,
  reviewed_by TEXT NULL,
  started_utc TEXT NULL,
  ended_utc TEXT NULL,
  created_utc TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  content_version INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_experiments_knowledge ON experiment_runs(knowledge_id, created_utc DESC);
CREATE INDEX IF NOT EXISTS ix_experiments_project ON experiment_runs(project_id, created_utc DESC);

CREATE TABLE IF NOT EXISTS experiment_result_proposals (
  id TEXT PRIMARY KEY,
  experiment_id TEXT NOT NULL REFERENCES experiment_runs(id) ON DELETE RESTRICT,
  submitted_by TEXT NOT NULL,
  proposed_outcome TEXT NOT NULL CHECK(proposed_outcome IN ('passed','failed','inconclusive')),
  observations_md TEXT NOT NULL,
  evidence_json TEXT NOT NULL DEFAULT '[]',
  review_status TEXT NOT NULL DEFAULT 'pending' CHECK(review_status IN ('pending','accepted','rejected')),
  reviewer TEXT NULL,
  review_reason TEXT NULL,
  idempotency_key TEXT NOT NULL UNIQUE,
  created_utc TEXT NOT NULL,
  reviewed_utc TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_experiment_proposals_pending ON experiment_result_proposals(review_status, created_utc DESC);

CREATE TABLE IF NOT EXISTS knowledge_activity (
  id TEXT PRIMARY KEY,
  knowledge_id INTEGER NOT NULL REFERENCES knowledge_items(id) ON DELETE RESTRICT,
  actor TEXT NOT NULL,
  action TEXT NOT NULL,
  previous_json TEXT NULL,
  new_json TEXT NOT NULL,
  reason TEXT NULL,
  created_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_activity_knowledge ON knowledge_activity(knowledge_id, created_utc DESC);
CREATE INDEX IF NOT EXISTS ix_knowledge_intent ON knowledge_items(intent_flag, review_status);
CREATE INDEX IF NOT EXISTS ix_knowledge_review_validation ON knowledge_items(review_status, validation_status);
