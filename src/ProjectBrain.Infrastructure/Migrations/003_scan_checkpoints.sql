-- Unpublished read cache: safe retry never exposes a partial inventory.
CREATE TABLE scan_checkpoints (
    root_id TEXT NOT NULL REFERENCES scan_roots(id),
    rel_path TEXT NOT NULL,
    bytes INTEGER NOT NULL,
    mtime INTEGER NOT NULL,
    sha256 TEXT NOT NULL,
    extract TEXT NOT NULL,
    parser_version INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY(root_id, rel_path)
);
