"""Real SQLite/FTS5 migration and persistence tests; no application runtime or mocks."""
import sqlite3
import re
import json
from contextlib import closing
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = ROOT / "src/ProjectBrain.Infrastructure/Migrations"

def migrate(db, version=4):
    db.execute("PRAGMA foreign_keys=ON")
    for path in sorted(MIGRATIONS.glob("*.sql"))[:version]:
        db.executescript(path.read_text(encoding="utf-8-sig"))

def knowledge(db, state="draft"):
    db.execute("INSERT INTO knowledge_items(kind,title,summary,body_md,state,proposed_by,created_utc,updated_utc) VALUES('idea','Repository pattern','Separate persistence','Example notes',?,'human','2026-10-23','2026-10-23')", (state,))
    return db.execute("SELECT last_insert_rowid()").fetchone()[0]

class SchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")

    def tearDown(self):
        self.db.close()

    def test_production_sql_statements_prepare_against_real_schema(self):
        migrate(self.db)
        self.db.execute("CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_utc TEXT)")
        count = 0
        pattern = re.compile(r'\.(?:Exec|Rows|One|Scalar)\("((?:\\.|[^"\\])*)"(?!\s*\+)')
        for path in (ROOT / "src").rglob("*.cs"):
            for match in pattern.finditer(path.read_text(encoding="utf-8-sig")):
                sql = json.loads('"' + match.group(1) + '"')
                if ";" in sql or not sql.startswith(("SELECT ", "INSERT ", "UPDATE ", "DELETE ")):
                    continue
                params = {key: None for key in re.findall(r'\$([a-zA-Z_][a-zA-Z0-9_]*)', sql)}
                with self.subTest(file=path.name, sql=sql):
                    self.db.execute("EXPLAIN " + sql, params).fetchall()
                count += 1
        self.assertGreater(count, 70)

    def test_clean_migrations_integrity_and_axes(self):
        migrate(self.db)
        kid = knowledge(self.db)
        self.assertEqual(("draft", "untested", "none"), self.db.execute("SELECT review_status,validation_status,intent_flag FROM knowledge_items WHERE id=?", (kid,)).fetchone())
        self.assertEqual("ok", self.db.execute("PRAGMA integrity_check").fetchone()[0])

    def test_upgrade_never_promotes_old_verified(self):
        migrate(self.db, 1)
        kid = knowledge(self.db, "verified")
        self.db.executescript((MIGRATIONS / "002_quick_capture_and_experiments.sql").read_text())
        self.assertEqual(("approved", "untested", 1), self.db.execute("SELECT review_status,validation_status,needs_recheck FROM knowledge_items WHERE id=?", (kid,)).fetchone())

    def test_invalid_axes_rejected(self):
        migrate(self.db)
        knowledge(self.db)
        for column, value in [("kind", "verified"), ("review_status", "passed"), ("validation_status", "approved"), ("intent_flag", "verified")]:
            with self.assertRaises(sqlite3.IntegrityError):
                self.db.execute(f"UPDATE knowledge_items SET {column}=?", (value,))

    def test_fts_update_transaction_rollback(self):
        migrate(self.db)
        kid = knowledge(self.db)
        self.db.execute("INSERT INTO knowledge_fts(rowid,title,summary,body,tags) VALUES(?, 'Repository pattern','','','csharp')", (kid,))
        self.db.commit()
        self.assertEqual(kid, self.db.execute("SELECT rowid FROM knowledge_fts WHERE knowledge_fts MATCH ?", ('"repository"',)).fetchone()[0])
        self.db.execute("DELETE FROM knowledge_fts WHERE rowid=?", (kid,))
        self.db.rollback()
        self.assertEqual(1, self.db.execute("SELECT count(*) FROM knowledge_fts WHERE knowledge_fts MATCH 'repository'").fetchone()[0])

    def test_capture_original_idempotency_foreign_keys(self):
        migrate(self.db)
        kid = knowledge(self.db)
        raw = "  <script>alert(1)</script>\n    keep whitespace  "
        self.db.execute("INSERT INTO captures(id,knowledge_id,original_text,creator,idempotency_key,created_utc,updated_utc) VALUES('capture',?,?,'human','retry','now','now')", (kid, raw))
        self.assertEqual(raw, self.db.execute("SELECT original_text FROM captures").fetchone()[0])
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("DELETE FROM knowledge_items WHERE id=?", (kid,))

    def test_one_active_job_per_root(self):
        migrate(self.db)
        self.db.execute("INSERT INTO scan_roots(id,alias,path) VALUES('root','fixture','/fixture')")
        self.db.execute("INSERT INTO scan_jobs(id,root_id,mode,status,created_utc) VALUES('a','root','incremental','running','now')")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO scan_jobs(id,root_id,mode,status,created_utc) VALUES('b','root','incremental','queued','now')")
        self.db.execute("UPDATE scan_jobs SET status='interrupted' WHERE id='a'")
        self.db.execute("INSERT INTO scan_jobs(id,root_id,mode,status,created_utc) VALUES('b','root','incremental','queued','now')")

    def test_backup_restart_retains_capture_and_experiment(self):
        with tempfile.TemporaryDirectory() as directory:
            original = sqlite3.connect(str(Path(directory)/"original.db"))
            migrate(original)
            kid = knowledge(original)
            original.execute("INSERT INTO captures(id,knowledge_id,original_text,creator,created_utc,updated_utc) VALUES('cap',?,'untouched','human','now','now')", (kid,))
            original.execute("INSERT INTO experiment_runs(id,knowledge_id,status,hypothesis,success_criteria,created_by,created_utc,updated_utc) VALUES('run',?,'approved','hypothesis','measurable','human','now','now')", (kid,))
            original.commit()
            with closing(sqlite3.connect(str(Path(directory)/"backup.db"))) as backup:
                original.backup(backup)
            original.close()
            with closing(sqlite3.connect(str(Path(directory)/"backup.db"))) as restored:
                self.assertEqual("untouched", restored.execute("SELECT original_text FROM captures").fetchone()[0])
                self.assertEqual("approved", restored.execute("SELECT status FROM experiment_runs").fetchone()[0])
                self.assertEqual("ok", restored.execute("PRAGMA integrity_check").fetchone()[0])

if __name__ == "__main__":
    unittest.main(verbosity=2)


