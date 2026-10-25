using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProjectBrain.Domain;
namespace ProjectBrain.Infrastructure;

public sealed class ScanEngine(Database db, ProjectService projects, TimeProvider clock)
{
    private readonly SemaphoreSlim singleConsumer = new(1);
    private string Now => clock.GetUtcNow().ToString("O");
    private sealed record Observation(string RootPath, string ProjectPath, string SourcePath, string RelativePath, long Bytes, long Mtime, string? Hash, string Extract);
    public object Enqueue(string rootId, string mode, string actor = "human", string? key = null)
    {
        Rules.Choice(mode, ["incremental", "full_audit"]);
        return db.Mutate("scan-enqueue:" + rootId, key, new { rootId, mode }, c =>
        {
            c.One("SELECT id FROM scan_roots WHERE id=$id", ("id", rootId));
            var active = c.Rows("SELECT id,status FROM scan_jobs WHERE root_id=$id AND status IN ('queued','running')", ("id", rootId)).FirstOrDefault();
            if (active is not null) return (object)new { id = active.S("id"), status = active.S("status"), deduplicated = true };
            var id = Rules.NewId(); c.Exec("INSERT INTO scan_jobs(id,root_id,mode,status,created_utc) VALUES($id,$root,$mode,'queued',$now)", ("id", id), ("root", rootId), ("mode", mode), ("now", Now));
            c.Exec("UPDATE scan_roots SET enabled=CASE WHEN last_success_utc IS NULL THEN 1 ELSE enabled END WHERE id=$id", ("id", rootId));
            db.Audit(c, id, actor, "scan_queued", null, new { id, rootId, mode }); return new { id, status = "queued", deduplicated = false };
        });
    }
    public void Recover()
    {
        var interrupted = db.Write(c =>
        {
            var jobs = c.Rows("SELECT root_id,mode FROM scan_jobs WHERE status='running' AND cancel_requested=0");
            c.Exec("UPDATE scan_jobs SET status=CASE WHEN cancel_requested=1 THEN 'cancelled' ELSE 'interrupted' END,ended_utc=$now,error_summary='Service stopped before publication; previous inventory preserved.' WHERE status='running'", ("now", Now));
            return jobs;
        });
        foreach (var job in interrupted) Enqueue(job.S("root_id"), job.S("mode"), "recovery");
    }
    public object Cancel(string id, string? key = null, string actor = "human")
    {
        return db.Mutate("cancel-scan:" + id, key, new { id }, c =>
        {
            c.One("SELECT id FROM scan_jobs WHERE id=$id", ("id", id));
            c.Exec("UPDATE scan_jobs SET cancel_requested=1,status=CASE WHEN status='queued' THEN 'cancelled' ELSE status END,ended_utc=CASE WHEN status='queued' THEN $now ELSE ended_utc END WHERE id=$id AND status IN ('queued','running')", ("id", id), ("now", Now));
            return (object)new { id };
        });
    }
    public void Schedule()
    {
        var settings = projects.Settings(); if (settings.Paused) return;
        var now = clock.GetUtcNow();
        if (settings.QuietStartHourUtc is int start && settings.QuietEndHourUtc is int end && (start < end ? now.Hour >= start && now.Hour < end : now.Hour >= start || now.Hour < end)) return;
        using var c = db.Open();
        foreach (var root in c.Rows("SELECT * FROM scan_roots WHERE enabled=1 AND (next_scan_utc IS NULL OR next_scan_utc<=$now)", ("now", Now)))
        {
            var audit = !DateTimeOffset.TryParse(root.S("last_audit_utc"), out var lastAudit) || now - lastAudit >= TimeSpan.FromDays(settings.AuditDays);
            Enqueue(root.S("id"), audit ? "full_audit" : "incremental", "scheduler");
        }
    }
    public async Task<bool> ProcessNext(CancellationToken token)
    {
        await singleConsumer.WaitAsync(token);
        try
        {
            var job = db.Write(c =>
            {
                var pending = c.Rows("SELECT * FROM scan_jobs WHERE status='queued' ORDER BY created_utc LIMIT 1").FirstOrDefault();
                if (pending is not null) c.Exec("UPDATE scan_jobs SET status='running',started_utc=$now WHERE id=$id", ("id", pending.S("id")), ("now", Now));
                return pending;
            });
            if (job is null) return false;
            await Task.Run(() => Run(job, token), CancellationToken.None); return true;
        }
        finally { singleConsumer.Release(); }
    }
    private static void Check(SqliteConnection c, string jobId, CancellationToken token, Stopwatch timer, ScanSettings settings)
    {
        token.ThrowIfCancellationRequested();
        if (timer.Elapsed > TimeSpan.FromMinutes(settings.MaxMinutes)) throw new ScanLimitException();
        if (Convert.ToInt64(c.Scalar("SELECT cancel_requested FROM scan_jobs WHERE id=$id", ("id", jobId))) != 0) throw new OperationCanceledException();
    }
    private void Finish(string id, string status, object counts, string? error = null)
    {
        db.Write(c =>
        {
            c.Exec("UPDATE scan_jobs SET status=$status,ended_utc=$now,counts_json=$counts,error_summary=$error WHERE id=$id", ("id", id), ("status", status), ("now", Now), ("counts", JsonSerializer.Serialize(counts, Rules.Json)), ("error", error));
            c.Exec("UPDATE scan_roots SET next_scan_utc=$next WHERE id=(SELECT root_id FROM scan_jobs WHERE id=$id)", ("id", id), ("next", clock.GetUtcNow().AddHours(projects.Settings().IntervalHours).ToString("O")));
            return 0;
        });
    }
    private void Run(Dictionary<string, object?> job, CancellationToken token)
    {
        var id = job.S("id"); var settings = projects.Settings(); var timer = Stopwatch.StartNew(); long bytesRead = 0; var hashed = 0; var skipped = 0; var observed = 0; var resumed = 0;
        var phase = "discovery"; var currentPath = "."; var discovered = 0; var enumerated = 0; long lastProgressMs = 0;
        object Progress() => new { phase, currentPath = SourceSafety.DisplayPath(currentPath), discovered, enumerated, observed, bytesRead, hashed, skipped, elapsedMs = timer.ElapsedMilliseconds };
        void ReportProgress()
        {
            if (timer.ElapsedMilliseconds - lastProgressMs < 1000) return;
            db.Write(w => w.Exec("UPDATE scan_jobs SET counts_json=$counts WHERE id=$id", ("id", id), ("counts", JsonSerializer.Serialize(Progress(), Rules.Json))));
            lastProgressMs = timer.ElapsedMilliseconds;
        }
        try
        {
            using var c = db.Open(); var root = c.One("SELECT * FROM scan_roots WHERE id=$id", ("id", job.S("root_id")));
            var rootPath = root.S("path");
            if (!Directory.Exists(rootPath)) { Unavailable(id, root.S("id")); return; }
            SourceSafety.Contained(projects.Mount, rootPath);
            var known = c.Rows("SELECT f.*,coalesce(p.source_relative_path,p.relative_path) AS project_path FROM file_index f JOIN projects p ON p.id=f.project_id WHERE p.root_id=$root", ("root", root.S("id")))
                .GroupBy(r => r.S("project_path") == "." ? r.S("rel_path") : r.S("project_path") + "/" + r.S("rel_path"), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.L("removed")).First(), StringComparer.Ordinal);
            var checkpoints = c.Rows("SELECT * FROM scan_checkpoints WHERE root_id=$root", ("root", root.S("id"))).ToDictionary(r => r.S("rel_path"), StringComparer.Ordinal);
            var paths = new List<string>(); var directories = new Stack<(string Path, int Depth)>(); directories.Push((rootPath, 0));
            while (directories.TryPop(out var entry))
            {
                currentPath = SourceSafety.Relative(rootPath, entry.Path);
                Check(c, id, token, timer, settings);
                SourceSafety.Contained(rootPath, entry.Path);
                var entries = new DirectoryInfo(entry.Path).EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
                // Virtual environments can have arbitrary names; their installed packages are not project source.
                if (entries.Any(e => e.Name.Equals("pyvenv.cfg", StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
                foreach (var info in entries)
                {
                    var item = info.FullName;
                    currentPath = SourceSafety.Relative(rootPath, item); enumerated++;
                    ReportProgress();
                    Check(c, id, token, timer, settings);
                    var attr = info.Attributes;
                    if ((attr & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if ((attr & FileAttributes.Directory) != 0)
                    {
                        if (SourceSafety.SkipDirectory(Path.GetFileName(item), SourceSafety.Relative(rootPath, item), settings.IncludeDirectories)) { skipped++; continue; }
                        if (entry.Depth >= settings.MaxDepth) throw new ScanLimitException();
                        directories.Push((item, entry.Depth + 1)); continue;
                    }
                    if (!SourceSafety.Eligible(item)) { skipped++; continue; }
                    if (paths.Count >= settings.MaxFiles) throw new ScanLimitException();
                    paths.Add(SourceSafety.Relative(rootPath, item));
                    discovered = paths.Count;
                }
            }
            paths.Sort(StringComparer.Ordinal); var boundaries = Boundaries(paths); var observations = new List<Observation>();
            var splitProjects = paths.Where(p => p.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => p.Contains('/') ? p[..p.LastIndexOf('/')] : ".", StringComparer.Ordinal)
                .Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            foreach (var relative in paths)
            {
                phase = "reading"; currentPath = relative; ReportProgress();
                Check(c, id, token, timer, settings);
                var absolute = SourceSafety.Contained(rootPath, Path.Combine(rootPath, relative));
                var info = new FileInfo(absolute); var size = info.Length; var mtime = info.LastWriteTimeUtc.Ticks; known.TryGetValue(relative, out var prior);
                // Retry only unchanged noncritical text. Audits always read bytes again.
                if (job.S("mode") != "full_audit" && !SourceSafety.Critical(relative) && size <= settings.MaxTextBytes && checkpoints.TryGetValue(relative, out var checkpoint)
                    && checkpoint.L("bytes") == size && checkpoint.L("mtime") == mtime && checkpoint.L("parser_version") == 1)
                {
                    prior = new Dictionary<string, object?>(checkpoint) { ["removed"] = 0 };
                    resumed++;
                }
                var projectPath = Owner(relative, boundaries); var local = projectPath == "." ? relative : relative[(projectPath.Length + 1)..];
                var same = prior is not null && prior.L("removed") == 0 && prior.L("bytes") == size && prior.L("mtime") == mtime && prior.L("parser_version") == 1;
                string? hash = prior?.GetValueOrDefault("sha256") as string; var extract = prior?.S("extract") ?? "";
                if (!(same && job.S("mode") != "full_audit" && !SourceSafety.Critical(relative)))
                {
                    if (SourceSafety.IsText(relative) && size <= settings.MaxTextBytes)
                    {
                        if (bytesRead + size > settings.MaxBytes) throw new ScanLimitException();
                        // Re-check every ancestor immediately before opening; never follow an observed link.
                        SourceSafety.Contained(rootPath, absolute);
                        using var stream = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (stream.Length != size) throw new IOException("Source changed while opening.");
                        var content = new byte[(int)size]; stream.ReadExactly(content); bytesRead += size; hashed++;
                        if (stream.ReadByte() != -1 || File.GetLastWriteTimeUtc(absolute).Ticks != mtime) throw new IOException("Source changed during read.");
                        SourceSafety.Contained(rootPath, absolute);
                        hash = Convert.ToHexString(SHA256.HashData(content));
                        if (prior is null || prior.S("sha256") != hash || prior.L("parser_version") != 1) extract = Extractor.Extract(relative, Encoding.UTF8.GetString(content));
                        db.Write(w => w.Exec("INSERT INTO scan_checkpoints(root_id,rel_path,bytes,mtime,sha256,extract) VALUES($root,$path,$bytes,$mtime,$hash,$extract) ON CONFLICT(root_id,rel_path) DO UPDATE SET bytes=excluded.bytes,mtime=excluded.mtime,sha256=excluded.sha256,extract=excluded.extract,parser_version=1",
                            ("root", root.S("id")), ("path", relative), ("bytes", size), ("mtime", mtime), ("hash", hash), ("extract", extract)));
                    }
                    else { hash = null; extract = "[Metadata only; contents not read; weak fingerprint]"; }
                }
                if (splitProjects.TryGetValue(projectPath, out var manifests))
                {
                    // Shared source is evidence for each manifest, never evidence of one merged project.
                    foreach (var manifest in manifests.Where(m => !manifests.Contains(relative) || m == relative))
                        observations.Add(new(relative, manifest, projectPath, local, size, mtime, hash, extract));
                }
                else observations.Add(new(relative, projectPath, projectPath, local, size, mtime, hash, extract));
                observed++;
                ReportProgress();
            }
            Check(c, id, token, timer, settings);
            var gitHeads = new Dictionary<string, string?>();
            foreach (var project in observations.Select(o => o.SourcePath).Distinct())
            {
                phase = "git_metadata"; currentPath = project; ReportProgress();
                Check(c, id, token, timer, settings);
                gitHeads[project] = GitMetadata.ReadHead(Path.Combine(rootPath, project), count =>
                {
                    if (bytesRead + count > settings.MaxBytes) throw new ScanLimitException();
                    bytesRead += count;
                });
            }
            phase = "publishing"; currentPath = "."; ReportProgress();
            var changed = Publish(root, job, observations, gitHeads);
            Finish(id, "succeeded", new { observed, bytesRead, hashed, skipped, resumed, changed, elapsedMs = timer.ElapsedMilliseconds });
        }
        catch (OperationCanceledException) { Finish(id, "cancelled", new { observed, bytesRead, hashed, skipped }, "Cancelled before publication; previous inventory preserved."); }
        catch (ScanLimitException) { Finish(id, "partial", Progress(), "Scan budget reached; previous inventory preserved. Increase limits and retry."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DomainException)
        { Finish(id, "partial", Progress(), $"Source unavailable or changed during {phase} at '{SourceSafety.DisplayPath(currentPath)}' ({ex.GetType().Name}, code 0x{ex.HResult:X8}); previous inventory preserved."); }
        catch (Exception ex) { Finish(id, "failed", Progress(), $"Scan failed during {phase} ({ex.GetType().Name}); previous inventory preserved. Check diagnostics before retrying."); }
    }
    private void Unavailable(string job, string root)
    {
        db.Write(c => c.Exec("UPDATE scan_roots SET availability='unavailable' WHERE id=$id", ("id", root)));
        Finish(job, "unavailable", new { changed = 0 }, "Source mount unavailable; no removal events published.");
    }
    public static string[] Boundaries(IEnumerable<string> files)
    {
        var paths = files.ToArray(); var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path); var dir = path.Contains('/') ? path[..path.LastIndexOf('/')] : ".";
            if (path.EndsWith("ProjectSettings/ProjectVersion.txt", StringComparison.Ordinal)) { var n = path[..^"ProjectSettings/ProjectVersion.txt".Length].TrimEnd('/'); candidates.Add(n.Length == 0 ? "." : n); }
            else if (name == "package.json" || name == "pyproject.toml" || Path.GetExtension(name) is ".sln" or ".slnx" or ".csproj" or ".fsproj" or ".uproject" || name.StartsWith("Dockerfile") || name is "compose.yaml" or "compose.yml") candidates.Add(dir);
        }
        // A parent project owns its modules (including Unity generated .csproj files).
        return candidates.Where(p => !candidates.Any(parent => parent != p && (parent == "." || p.StartsWith(parent + "/", StringComparison.Ordinal)))).Order(StringComparer.Ordinal).ToArray();
    }
    private static string Owner(string relative, string[] boundaries) => boundaries.FirstOrDefault(p => p == "." || relative.StartsWith(p + "/", StringComparison.Ordinal)) ?? (relative.Contains('/') ? relative[..relative.IndexOf('/')] : ".");
    private static string Signature(IEnumerable<Observation> files) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).Select(f => f.RelativePath + ":" + (f.Hash ?? $"{f.Bytes}:{f.Mtime}"))))));
    private int Publish(Dictionary<string, object?> root, Dictionary<string, object?> job, List<Observation> observations, Dictionary<string, string?> gitHeads)
    {
        return db.Write(c =>
        {
            if (Convert.ToInt64(c.Scalar("SELECT cancel_requested FROM scan_jobs WHERE id=$id", ("id", job.S("id")))) != 0) throw new OperationCanceledException();
            var oldProjects = c.Rows("SELECT * FROM projects WHERE root_id=$id", ("id", root.S("id")));
            var groups = observations.GroupBy(o => o.ProjectPath).ToArray(); var currentPaths = groups.Select(g => g.Key).ToHashSet(StringComparer.Ordinal); var touched = new HashSet<string>(); var changed = 0;
            foreach (var group in groups)
            {
                var moved = false; var signature = Signature(group); var p = oldProjects.FirstOrDefault(p => p.S("relative_path") == group.Key);
                if (p is null)
                {
                    var matches = oldProjects.Where(p => !currentPaths.Contains(p.S("relative_path")) && p.S("signature") == signature && !touched.Contains(p.S("id"))).ToArray();
                    if (matches.Length == 1)
                    {
                        p = matches[0]; moved = true; c.Exec("UPDATE projects SET relative_path=$path WHERE id=$id", ("id", p.S("id")), ("path", group.Key));
                        Event(c, job.S("id"), p.S("id"), "project_moved", p.S("relative_path"), group.Key, null, null);
                    }
                    else if (matches.Length > 1) c.Exec("INSERT INTO project_link_reviews(candidate_project_ids_json,reason) VALUES($ids,'Ambiguous project move; identities not merged')", ("ids", JsonSerializer.Serialize(matches.Select(p => p.S("id")))));
                }
                var projectId = p?.S("id") ?? Rules.NewId(); touched.Add(projectId);
                var sourcePath = group.First().SourcePath;
                var gitHead = gitHeads[sourcePath];
                var oldHead = p is not null && JsonSerializer.Deserialize<JsonElement>(p.S("profile_json")).TryGetProperty("gitHead", out var previousHead) ? previousHead.GetString() : null;
                var headChanged = p is not null && oldHead != gitHead;
                if (headChanged) Event(c, job.S("id"), projectId, "git_head_changed", null, null, oldHead, gitHead);
                var kind = Extractor.Kind(group.Select(f => f.RelativePath)); var name = group.Key == "." ? root.S("alias") : group.Key != sourcePath ? Path.GetFileNameWithoutExtension(group.Key) : group.Key.Split('/').Last();
                if (p is null) c.Exec("INSERT INTO projects(id,root_id,relative_path,name,kind,signature,created_utc,updated_utc) VALUES($id,$root,$path,$name,$kind,$sig,$now,$now)", ("id", projectId), ("root", root.S("id")), ("path", group.Key), ("name", name), ("kind", kind), ("sig", signature), ("now", Now));
                c.Exec("UPDATE projects SET source_relative_path=$source WHERE id=$id", ("id", projectId), ("source", sourcePath));
                var prior = c.Rows("SELECT * FROM file_index WHERE project_id=$id", ("id", projectId)); var active = prior.Where(r => r.L("removed") == 0).ToArray();
                var missing = active.Where(r => !group.Any(f => f.RelativePath == r.S("rel_path"))).ToList(); var additions = group.Where(f => !active.Any(r => r.S("rel_path") == f.RelativePath)).ToArray();
                var kindChanged = p is not null && p.S("kind") != kind; var projectChanged = moved || kindChanged; var architecture = kindChanged;
                foreach (var file in group)
                {
                    var old = prior.FirstOrDefault(r => r.S("rel_path") == file.RelativePath);
                    var renamed = old is null && file.Hash is not null ? missing.Where(r => r.S("sha256") == file.Hash).ToArray() : [];
                    if (renamed.Length == 1 && additions.Count(f => f.Hash == file.Hash) == 1)
                    {
                        old = renamed[0]; missing.Remove(old);
                        Event(c, job.S("id"), projectId, "file_renamed", old.S("rel_path"), file.RelativePath, file.Hash, file.Hash);
                        // Keep immutable experiment evidence at its old path; request recheck instead of rewriting it.
                        Stale(c, projectId, old.S("rel_path"));
                        c.Exec("UPDATE file_index SET rel_path=$new WHERE id=$id", ("new", file.RelativePath), ("id", old.L("id"))); projectChanged = true; changed++;
                    }
                    var contentChanged = old is null || old.L("removed") != 0 || (file.Hash is not null ? old.S("sha256") != file.Hash : old.L("bytes") != file.Bytes || old.L("mtime") != file.Mtime);
                    if (contentChanged)
                    {
                        Event(c, job.S("id"), projectId, old is null || old.L("removed") != 0 ? "file_added" : "file_modified", old?.S("rel_path"), file.RelativePath, old?.S("sha256"), file.Hash);
                        if (old is not null) Stale(c, projectId, file.RelativePath);
                        projectChanged = true; architecture |= SourceSafety.Critical(file.RelativePath); changed++;
                    }
                    if (contentChanged || old is null || old.L("bytes") != file.Bytes || old.L("mtime") != file.Mtime)
                        c.Exec("INSERT INTO file_index(project_id,rel_path,bytes,mtime,sha256,extract) VALUES($project,$path,$bytes,$mtime,$hash,$extract) ON CONFLICT(project_id,rel_path) DO UPDATE SET bytes=excluded.bytes,mtime=excluded.mtime,sha256=excluded.sha256,extract=excluded.extract,removed=0,parser_version=1",
                            ("project", projectId), ("path", file.RelativePath), ("bytes", file.Bytes), ("mtime", file.Mtime), ("hash", file.Hash), ("extract", file.Extract));
                }
                foreach (var removed in missing) { Remove(c, job.S("id"), projectId, removed); projectChanged = true; changed++; }
                if (projectChanged || headChanged || p is null)
                {
                    var revision = (p?.L("revision") ?? 0) + 1;
                    var profile = JsonSerializer.Serialize(new { summary = $"Observed {kind} project with {group.Count()} eligible files. Static inventory; runtime behavior and success are unverified.", technology = kind, gitHead, components = group.Where(f => SourceSafety.Critical(f.RelativePath)).Take(30).Select(f => new { path = f.RelativePath, metadata = Rules.Clip(f.Extract, 1000) }), folders = group.Select(f => f.RelativePath.Contains('/') ? f.RelativePath.Split('/')[0] : ".").Distinct().Take(30), fileCount = group.Count(), generatedBy = "deterministic", extractionVersion = 1, observedUtc = Now }, Rules.Json);
                    c.Exec("UPDATE projects SET name=$name,kind=$kind,signature=$sig,revision=$revision,profile_json=$profile,analysis_due=1,status='available',updated_utc=$now WHERE id=$id", ("id", projectId), ("name", name), ("kind", kind), ("sig", signature), ("revision", revision), ("profile", profile), ("now", Now));
                    c.Exec("INSERT INTO project_revisions(project_id,scan_job_id,revision,classification,profile_json,created_utc) VALUES($project,$job,$revision,$class,$profile,$now)", ("project", projectId), ("job", job.S("id")), ("revision", revision), ("class", architecture ? "architecture" : "implementation"), ("profile", profile), ("now", Now));
                    ProfileKnowledge(c, projectId, name, kind, group.ToArray(), profile);
                }
            }
            foreach (var p in oldProjects.Where(p => !touched.Contains(p.S("id"))))
            {
                var missing = c.Rows("SELECT * FROM file_index WHERE project_id=$id AND removed=0", ("id", p.S("id")));
                foreach (var f in missing) { Remove(c, job.S("id"), p.S("id"), f); changed++; }
                if (missing.Count > 0)
                {
                    var revision = p.L("revision") + 1;
                    var profile = JsonSerializer.Serialize(new { summary = "Project files are absent after a complete traversal. Historical snapshots and knowledge are retained.", components = Array.Empty<object>(), folders = Array.Empty<string>(), fileCount = 0, observedUtc = Now }, Rules.Json);
                    c.Exec("UPDATE projects SET status='missing',revision=$revision,profile_json=$profile,analysis_due=1,updated_utc=$now WHERE id=$id", ("id", p.S("id")), ("revision", revision), ("profile", profile), ("now", Now));
                    c.Exec("INSERT INTO project_revisions(project_id,scan_job_id,revision,classification,profile_json,created_utc) VALUES($project,$job,$revision,'unknown',$profile,$now)", ("project", p.S("id")), ("job", job.S("id")), ("revision", revision), ("profile", profile), ("now", Now));
                }
            }
            c.Exec("UPDATE scan_roots SET availability='available',last_success_utc=$now,last_audit_utc=CASE WHEN $audit=1 THEN $now ELSE last_audit_utc END WHERE id=$id", ("id", root.S("id")), ("now", Now), ("audit", job.S("mode") == "full_audit" ? 1 : 0));
            c.Exec("DELETE FROM scan_checkpoints WHERE root_id=$root", ("root", root.S("id")));
            return changed;
        });
    }
    private void Event(SqliteConnection c, string job, string project, string type, string? oldPath, string? newPath, string? oldHash, string? newHash) => c.Exec(
        "INSERT INTO change_events(scan_job_id,project_id,event_type,old_path,new_path,old_hash,new_hash,created_utc) VALUES($job,$project,$type,$old,$new,$oldHash,$newHash,$now)",
        ("job", job), ("project", project), ("type", type), ("old", oldPath), ("new", newPath), ("oldHash", oldHash), ("newHash", newHash), ("now", Now));
    private void Remove(SqliteConnection c, string job, string project, Dictionary<string, object?> file)
    {
        c.Exec("UPDATE file_index SET removed=1 WHERE id=$id", ("id", file.L("id")));
        Event(c, job, project, "file_removed", file.S("rel_path"), null, file.S("sha256"), null); Stale(c, project, file.S("rel_path"));
    }
    private void Stale(SqliteConnection c, string project, string path)
    {
        var affected = c.Rows("SELECT DISTINCT knowledge_id FROM knowledge_evidence WHERE project_id=$project AND rel_path=$path AND stale_flag=0", ("project", project), ("path", path));
        c.Exec("UPDATE knowledge_evidence SET stale_flag=1 WHERE project_id=$project AND rel_path=$path", ("project", project), ("path", path));
        foreach (var row in affected)
        {
            c.Exec("UPDATE knowledge_items SET needs_review=1,needs_recheck=1,validation_status=CASE WHEN validation_status='passed' THEN 'stale' ELSE validation_status END,content_version=content_version+1,updated_utc=$now WHERE id=$id", ("id", row.L("knowledge_id")), ("now", Now));
            db.Audit(c, "knowledge:" + row.L("knowledge_id"), "scanner", "evidence_stale", null, new { projectId = project, relativePath = path });
        }
    }
    private void ProfileKnowledge(SqliteConnection c, string project, string name, string kind, Observation[] files, string profile)
    {
        var old = c.Rows("SELECT id,review_status FROM knowledge_items WHERE project_id=$project AND kind='project_profile' AND proposed_by='scanner' ORDER BY id DESC LIMIT 1", ("project", project)).FirstOrDefault();
        // Human-reviewed generated records are historical claims; scanner never overwrites them.
        if (old is not null && old.S("review_status") != "draft") return;
        var body = Rules.Clip(string.Join("\n", files.Where(f => f.Hash is not null).Take(40).Select(f => f.RelativePath + "\n" + Rules.Clip(f.Extract, 1000))), 16000);
        var summary = $"Static {kind} inventory. Observations require review; no execution or successful outcome inferred.";
        long id;
        if (old is null)
        {
            c.Exec("INSERT INTO knowledge_items(kind,title,summary,body_md,state,proposed_by,project_id,created_utc,updated_utc) VALUES('project_profile',$title,$summary,$body,'discovered','scanner',$project,$now,$now)", ("title", Rules.SafeText(name)), ("summary", summary), ("body", body), ("project", project), ("now", Now));
            id = Convert.ToInt64(c.Scalar("SELECT last_insert_rowid()"));
        }
        else { id = old.L("id"); c.Exec("UPDATE knowledge_items SET summary=$summary,body_md=$body,content_version=content_version+1,updated_utc=$now WHERE id=$id", ("id", id), ("summary", summary), ("body", body), ("now", Now)); }
        c.Exec("INSERT OR IGNORE INTO knowledge_tags VALUES($id,$tag)", ("id", id), ("tag", kind));
        foreach (var f in files.Where(f => f.Hash is not null).Take(10))
            if (c.Scalar("SELECT id FROM knowledge_evidence WHERE knowledge_id=$id AND rel_path=$path AND file_sha256=$hash", ("id", id), ("path", f.RelativePath), ("hash", f.Hash)) is null)
                c.Exec("INSERT INTO knowledge_evidence(knowledge_id,project_id,rel_path,file_sha256,evidence_note) VALUES($id,$project,$path,$hash,'Static observation; not a tested claim')", ("id", id), ("project", project), ("path", f.RelativePath), ("hash", f.Hash));
        KnowledgeService.Index(c, id);
    }
    private sealed class ScanLimitException : Exception;
}


