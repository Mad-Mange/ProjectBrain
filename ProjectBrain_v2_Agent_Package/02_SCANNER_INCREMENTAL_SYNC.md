# 02 — Scanner, Scheduled Sync and Change Detection

> **v2 rule:** scanner manages project source evidence and profiles only; never rewrite manual Quick Capture, experiment runs or human statuses. If dependent evidence changes, flag `needsReview` and if current passing proof is invalidated set `validationStatus=stale`, retaining immutable experiment result history.


## 1. Operating principle
**Read-only source + periodic differential indexing + append-only historical meaning.** Treat every directory as untrusted content. The system does not execute or import project code. Source-of-truth is files on disk, but knowledge review decisions are independent user data.

## 2. Schedule and triggers

| Mode | Trigger | Work |
|---|---|---|
| Initial inventory | First explicit import | Discover all eligible projects, process all eligible manifest files, generate profiles |
| Incremental | Every 6h by default | Inventory metadata, hash candidates, update only changed projects; do not enrich unchanged content |
| Manual incremental | UI/API click | Queue a normal differential scan immediately |
| Full hash audit | Every 7 days by default | Recompute SHA-256 of all eligible small text files even if mtimes unchanged |
| Manual rebuild | User explicit command | Rebuild search index/profiles from stored manifest or actual files; separate controls |
| Startup catch-up | Service restarts after interval elapsed | Queue exactly one pending catch-up job, unless paused |

All internal times UTC, UI displays Europe/Stockholm by default. Prevent overlap using database-backed job lease/lock and a single scan consumer. Schedule interval from **end of last successful scan** (or chosen documented policy), prevent failed scans from tight retry loops. Configurable scan pause and time window. Allow cancellation between files and project phases.

### Why not rely on file watchers?
Docker Desktop / WSL2 cannot reliably expose all native Windows-host file system events into Linux containers. Polling metadata is primary; watchers are optional hints only. Never make correct behavior depend on inotify for Windows bind mounts. If source scale requires better performance, a later host scanner may push signed change batches to the same API.

## 3. Root configuration / containment

- Docker mounts `PROJECTS_ROOT` to `/sources/projects` with `read_only: true`; optionally add named extra roots in Compose later.
- `ScanRoot` stores only a validated subpath underneath an already-mounted source, such as `/sources/projects` or `/sources/projects/Games`.
- Canonicalize existing paths and validate each path segment before traversal; skip symbolic links, junctions and reparse-point equivalents by default (Linux symlink check). Do not follow links outside root.
- Reject `/`, `/app`, `/app/data`, `/proc`, `/sys`, network scheme URLs and unapproved paths as scan roots.
- If the root does not exist or is not accessible: mark `unavailable` and emit alert; **do not** cascade-delete projects or mark all files removed.
- Scope indexing to known file types; skip hidden credential folders, tools caches and build output.

## 4. Default exclusions

Directory patterns, wherever encountered:

```
.git/objects; .svn; .hg; node_modules; .venv; venv; __pycache__;
.pytest_cache; .mypy_cache; .tox; bin; obj; .vs; .idea;
.next; dist; build; coverage; target; .gradle; Library;
Temp; Logs; UserSettings; DerivedDataCache; Intermediate;
Binaries; Saved; shader-cache; Cache; caches; .cache;
.git/lfs/objects; .terraform; .nuget/packages
```

**Exception:** `.git` *metadata* may be read safely in a narrow read-only routine for HEAD/commit and top-level status; do not recursively scan objects. Some folder names (`Library`, `Saved`, `build`) may be meaningful in non-engine projects: allow project-specific exclusion overrides with tests.

Always exclude files by secret patterns: `.env`, `.env.*` (allow `.env.example` with names-only redaction), `*.pfx`, `*.pem`, `id_rsa*`, `*.key`, `.npmrc`, `credentials*`, `secrets.json`, private-key files and token stores. Redact high-entropy tokens discovered in other configs. Preserve safe config **names** and framework clues only; never persist secret values or source lines containing secrets.

Ignore read-heavy binaries/content: `*.uasset`, `*.umap`, `*.fbx`, `*.blend`, `*.blend1`, videos/audio, `*.zip`, `*.7z`, large datasets, `*.db`, `*.sqlite`, `*.dll`, `*.exe`, model weights (`*.pt`, `*.pth`, `*.onnx`, `*.safetensors`). Store names, sizes, relative paths and extension aggregates where appropriate; do not hash/read large binary content except optional bounded verification.

## 5. Detect project boundaries deterministically

1. Enumerate candidates with stable case-aware normalized relative paths. Use detection precedence: Unity root > Unreal `.uproject` > .NET solution > web/package > Python > generic.
2. A nested `.csproj` under `Unity/` is a component. Multiple standalone `.csproj` in the same solution map to one logical solution project; individual modules are components.
3. Multiple `.uproject` files in one parent may represent separate Unreal projects; do not merge blindly.
4. A folder with `.blend` only may be a `modeling_collection`; multiple files in one folder do not necessarily mean separate projects.
5. An archived project moved under another folder should preserve identity by stable signature (Git remote if safely accessible, relative canonical key, manifest fingerprint). Ambiguous moves require review, not destructive auto-merge.
6. Assign durable random project ID on first discover; root alias + relative path stored separately and may change.

## 6. File fingerprinting algorithm

Store for every eligible file:
- root ID, project ID, relative path, file kind, byte count, last-write UTC precision, optional inode/file-id if available, SHA-256, current scan observation timestamp, changed timestamp and parser status.
- Hash content only for eligible and bounded files. Never use timestamp alone as proof of actual content.
- For normal incremental run: `(size, mtime)` equal -> `unchanged` fast path, except selected critical config files and audit due items. `(size, mtime)` differs -> calculate hash; if content hash equal, update metadata but do not reparse.
- Important files (`*.csproj`, `.uproject`, `package.json`, `pyproject.toml`, Dockerfiles, `README*`, `.git/HEAD`) may be rehashed each run, within budgets.
- During 7-day audit: recompute eligible text SHA-256 even when `(size,mtime)` unchanged. Prevent missed same-size/same-mtime modifications.
- For large binary metadata (default metadata only): treat changed size/mtime as modified; flag `weak_fingerprint` and never pretend content equality verified.
- File timestamps may be missing or coarse; handle gracefully.

### Delta state table

| Prior state | Current observation | Event | Action |
|---|---|---|---|
| Unseen | File exists | `file_added` | Hash/parse if eligible |
| Known | Metadata equal, no audit due | None | Skip content |
| Known | Metadata differs, SHA equal | `metadata_changed` optional | Update metadata only |
| Known | SHA differs | `file_modified` | Parse file; mark related evidence needs review |
| Known | Not seen after **successful full traversal** | `file_removed` | Mark missing, update profile; retain historical evidence |
| Known | Root inaccessible | `root_unavailable` | No deletion or missing events |
| One removed + one added | Same SHA + plausible context | `file_renamed` | Preserve history and remap evidence when safe |
| One removed + one added | Different hashes | Separate events | Do not guess rename |
| Known | Extension/adapter changed | `parser_version_changed` | Reparse relevant file even if content unchanged |

**Avoid false deletions:** mark removed only after a fully completed, successful inventory of that root. Failed/cancelled scans must never imply deletions.

## 7. Project change classification

Use evidence-based heuristics; report candidates, not unearned conclusions:
- `cosmetic_or_docs`: README-only changes, comment-only if parser can establish.
- `implementation`: normal `.cs`, `.py`, `.cpp`, `.ts` changes.
- `architecture`: dependency versions, manifests, project files, entry points, interfaces/services, plugin/module config, networking/API contract and schema changes.
- `asset`: .blend/.uasset/.prefab etc. metadata changed; no semantic contents inferred.
- `unknown`: when evidence insufficient.

For each project revision persist `summary`, `classification`, `added_files`, `modified_files`, `removed_files`, `renamed_files`, `git_head_old/new`, `source_scan_id`, `profile_before/after_reference`, `detected_change_reasons`. If Git exists, record safe HEAD hash and branch; *optional* read-only `git log`/`git diff --name-status` must be bounded, timeout-protected and executed only in explicitly trusted local worktrees with hooks/external filters disabled; default can rely entirely on file manifests. Never rely solely on Git because many old projects are uncommitted or not version-controlled.

## 8. Incremental processing pseudocode

```text
RunScan(rootId, mode):
  acquire per-root lock; create ScanJob status=running
  if root inaccessible:
      finish ScanJob status=unavailable without marking files removed
      release lock; return

  discover candidate projects; enumerate eligible files using exclusions
  for each encountered file:
      ensure valid descendant path and cap traversal budgets
      observe metadata
      prior = lookup(rootId, relativePath)
      forceHash = mode==FullAudit OR criticalFile OR parserVersionChanged
      if prior and metadataEqual and not forceHash:
          mark seen; continue
      newHash = boundedHashIfAllowed(file)
      if prior and prior.sha256 == newHash and newHash exists:
          update metadata/seen; continue
      record file_added/file_modified; reparse only affected input

  if traversal completed successfully:
      candidates = previously seen but absent files
      pair removed/added candidates by strong matching SHA when feasible
      emit rename or added/removed events; DO NOT HARD DELETE history
      refresh profiles for affected projects only
      flag knowledge evidence impacted by content changes
      upsert search index for affected profiles and knowledge
      persist scan events and job metrics within bounded transactions
  else:
      preserve prior file existence flags; mark job partial/failed

  finalize scan job and root last-success timestamp if complete
  release lock
```

Use batches, bounded memory, cancellation token, queue backpressure, progress checkpoints, crash-safe state transitions. On startup mark abandoned `running` jobs as `interrupted` then safely reschedule; do not leave locks permanently held. Make rescans idempotent; same manifest produces no duplicate change events or proposals.

## 9. Profile refresh and knowledge invalidation

- Cache normalized parser outputs per file with `parser_version`; profiles aggregate unchanged cached results plus newly parsed files.
- Keep versioned profile snapshot summaries with timestamps and `ProjectRevision` references.
- Evidence records link to `FileIndex` + SHA + relative path + optional symbol/line span. If changed, set `needs_review=true` on knowledge item; do not remove it or rewrite human-approved text automatically.
- If a solution is still present at a new path after rename, rewrite **linkage** and record previous path in provenance history, not original knowledge claim.
- Relevance search should display a warning if evidence is stale or project currently unavailable.
- Automatic generated summaries must be `discovered` or `proposed`; agent/human determines what becomes reusable.

## 10. Limits and safety

Default values, editable:

```json
{
  "scanIntervalHours": 6,
  "fullHashAuditDays": 7,
  "maxTextFileMiB": 1,
  "maxTreeDepth": 20,
  "maxFilesPerRoot": 300000,
  "maxBytesReadPerScanGiB": 2,
  "maxScanDurationMinutes": 120,
  "parallelHashWorkers": 2,
  "maxIndexTextCharactersPerFile": 12000,
  "followSymlinks": false,
  "scanOnStartupIfOverdue": true
}
```

`maxBytesReadPerScanGiB` should be an actual consumed-I/O budget and include any full-audit read. If exceeded, mark job **partial** and do not infer removals. Large root mounts may require user adjustment after measuring.

## 11. Blender / Unreal / Unity nuances

- Unreal: inspect `.uproject`, `.uplugin`, `Source`, `Config`; treat cooked assets, shader cache and content package internals as opaque binaries. Display engine version when present; missing engine association is unknown, not guessed.
- Unity: `Packages/manifest.json`, `ProjectSettings` and text serializations may be parsed with safe text readers; no Unity Editor invocation, no scene import/build. Ignore `Library` and `Temp`.
- Blender: index `.blend` file names, dates, size, sidecar `.py`, `.fbx`, `.obj`, `.gltf` names; do not claim object/material information without opening the file through a trusted optional adapter. In phase 2, use Blender `--background --disable-autoexec` in an isolated opt-in environment, with no network, read-only input, CPU/time limits, and treat its scripting capability as a risk. Never run arbitrary embedded scripts or user-provided addons.

## 12. Acceptance checks specific to change tracking

- Edit one `.cs` file: one file delta, one project revision, no unrelated project reparsed.
- Touch mtime without changing bytes: metadata update only, no semantic revision.
- Modify same byte count and restore mtime: weekly audit detects change.
- Rename file preserving bytes: event suggests/preserves rename where unambiguous.
- Unplug/miss root: `unavailable`, **zero** delete events; knowledge stays queryable.
- Cancel scan halfway: `partial/cancelled`, **zero** delete events.
- Rename/move project: identity stable when evidence strong; ambiguous move surfaced for review.
- Run unchanged incremental twice: idempotent result, no duplicate proposals.
- Update evidence supporting approved lesson: lesson survives with `needs_review=true`.
- UI manual pause: scheduled runs cease; manual scanning can still work if expressly requested.
