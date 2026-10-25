using System.Security.Cryptography;
using System.Text.Json;
using ProjectBrain.Domain;
using ProjectBrain.Infrastructure;
public static class ApiRoutes
{
    private static string Key(HttpContext context) => Rules.Required(context.Request.Headers["Idempotency-Key"].FirstOrDefault(), 200);
    public static void MapBrainApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/dashboard", (Database db) =>
        {
            using var c = db.Open();
            return new { projects = c.Scalar("SELECT count(*) FROM projects"), captures = c.Scalar("SELECT count(*) FROM captures"), tryNext = c.Scalar("SELECT count(*) FROM knowledge_items WHERE intent_flag='try_next'"), pendingReviews = c.Scalar("SELECT count(*) FROM knowledge_items WHERE review_status='proposed' OR needs_review=1"), pendingResults = c.Scalar("SELECT count(*) FROM experiment_result_proposals WHERE review_status='pending'"), roots = c.Rows("SELECT id,alias,availability,last_success_utc AS lastSuccessUtc,next_scan_utc AS nextScanUtc FROM scan_roots"), recentScans = c.Rows("SELECT id,status,mode,counts_json AS countsJson,error_summary AS errorSummary,created_utc AS createdUtc FROM scan_jobs ORDER BY created_utc DESC LIMIT 5") };
        });
        api.MapGet("/roots", (ProjectService s) => s.Roots());
        api.MapPost("/roots", (RootRequest input, HttpContext ctx, ProjectService s) => s.Register(input.RelativePath, input.Alias, Key(ctx)));
        api.MapPatch("/roots/{id}", (string id, RootToggle input, HttpContext ctx, Database db) => db.Mutate("root:" + id, Key(ctx), input, c =>
        {
            var root = c.One("SELECT content_version FROM scan_roots WHERE id=$id", ("id", id)); Rules.Version(root.L("content_version"), input.ContentVersion); c.Exec("UPDATE scan_roots SET enabled=$enabled,content_version=content_version+1 WHERE id=$id", ("id", id), ("enabled", input.Enabled ? 1 : 0)); db.Audit(c, id, "human", "root_schedule", null, input); return new { id, input.Enabled };
        }));
        api.MapGet("/projects", (string? query, string? kind, int? page, ProjectService s) => s.Projects(query ?? "", kind, page ?? 1));
        api.MapGet("/projects/{id}", (string id, ProjectService s) => s.Profile(id));
        api.MapGet("/projects/{id}/changes", (string id, ProjectService s) => s.Changes(id));
        api.MapPost("/scans", (ScanRequest input, HttpContext ctx, ScanEngine s) => Results.Accepted("/api/v1/scans", s.Enqueue(input.RootId, input.Mode, ctx.Items["actor"]?.ToString() ?? "human", Key(ctx))));
        api.MapGet("/scans", (Database db) => { using var c = db.Open(); return c.Rows("SELECT id,root_id AS rootId,mode,status,started_utc AS startedUtc,ended_utc AS endedUtc,counts_json AS countsJson,error_summary AS errorSummary FROM scan_jobs ORDER BY created_utc DESC LIMIT 50"); });
        api.MapGet("/scans/{id}", (string id, Database db) => { using var c = db.Open(); return c.One("SELECT id,status,mode,counts_json AS countsJson,error_summary AS errorSummary FROM scan_jobs WHERE id=$id", ("id", id)); });
        api.MapPost("/scans/{id}/cancel", (string id, HttpContext ctx, ScanEngine s) => s.Cancel(id, Key(ctx)));
        api.MapPost("/captures", (CaptureRequest input, HttpContext ctx, KnowledgeService s) => Results.Created("/api/v1/captures", s.Capture(input, ctx.Request.Headers["Idempotency-Key"].FirstOrDefault())));
        api.MapGet("/captures", (string? query, string? status, int? page, Database db, KnowledgeService s) =>
        {
            using var c = db.Open(); var rows = c.Rows("SELECT cap.id,cap.knowledge_id FROM captures cap JOIN knowledge_items k ON k.id=cap.knowledge_id WHERE ($status IS NULL OR k.review_status=$status) AND (k.title LIKE $query OR k.body_md LIKE $query) ORDER BY cap.created_utc DESC LIMIT 50 OFFSET $offset", ("status", status), ("query", "%" + Rules.Clip(query ?? "", 200) + "%"), ("offset", Math.Clamp((page ?? 1) - 1, 0, 10000) * 50));
            return rows.Select(r => new { captureId = r.S("id"), item = s.Detail(c, r.L("knowledge_id"), false) }).ToArray();
        });
        api.MapGet("/captures/{id}", (string id, Database db, KnowledgeService s) => { using var c = db.Open(); return s.Get(c.One("SELECT knowledge_id FROM captures WHERE id=$id", ("id", id)).L("knowledge_id")); });
        api.MapPatch("/captures/{id}", (string id, EditRequest input, HttpContext ctx, Database db, KnowledgeService s) => { using var c = db.Open(); return s.Edit(c.One("SELECT knowledge_id FROM captures WHERE id=$id", ("id", id)).L("knowledge_id"), input, Key(ctx)); });
        api.MapGet("/captures/{id}/similar", (string id, Database db, KnowledgeService s) => Similar(id, db, s));
        api.MapPost("/captures/{id}/suggest", (string id, Database db, KnowledgeService s) => Similar(id, db, s));
        api.MapGet("/knowledge/search", (HttpContext ctx, KnowledgeService s) =>
        {
            var q = ctx.Request.Query; int.TryParse(q["page"], out var page); int.TryParse(q["limit"], out var limit);
            string? Value(string key) => q[key].FirstOrDefault() is { Length: > 0 } v ? v : null;
            return s.Search(new SearchRequest(Value("q") ?? "", Value("kind"), Value("reviewStatus"), Value("validationStatus"), Value("intentFlag"), Value("tag"), Value("projectId"), bool.TryParse(q["needsReview"], out var needs) ? needs : null, limit == 0 ? 20 : limit, page == 0 ? 1 : page));
        });
        api.MapGet("/knowledge/try-next", (KnowledgeService s) => s.Search(new SearchRequest(IntentFlag: "try_next")));
        api.MapGet("/knowledge/review-queue", (Database db, KnowledgeService s) =>
        {
            using var c = db.Open(); return c.Rows("SELECT id FROM knowledge_items WHERE review_status='proposed' OR needs_review=1 OR needs_recheck=1 ORDER BY updated_utc DESC LIMIT 50").Select(r => s.Detail(c, r.L("id"), false)).ToArray();
        });
        api.MapGet("/knowledge/{id:long}", (long id, KnowledgeService s) => s.Get(id));
        api.MapPatch("/knowledge/{id:long}", (long id, EditRequest input, HttpContext ctx, KnowledgeService s) => s.Edit(id, input, Key(ctx)));
        api.MapPost("/knowledge/proposals", (ProposalRequest input, KnowledgeService s) => Results.Created("/api/v1/knowledge/review-queue", s.Propose(input)));
        api.MapPost("/knowledge/{id:long}/review", (long id, DecisionRequest input, HttpContext ctx, KnowledgeService s) => s.Review(id, input, Key(ctx)));
        api.MapPost("/knowledge/{id:long}/resolve-validation", (long id, ResolveValidationRequest input, HttpContext ctx, KnowledgeService s) => s.ResolveValidation(id, input, Key(ctx)));
        api.MapPatch("/knowledge/{id:long}/intent", (long id, IntentRequest input, HttpContext ctx, KnowledgeService s) => s.Intent(id, input, Key(ctx)));
        api.MapPost("/knowledge/{id:long}/experiments", (long id, PlanRequest input, HttpContext ctx, KnowledgeService s) => s.Plan(id, input, Key(ctx)));
        api.MapGet("/knowledge/{id:long}/experiments", (long id, Database db) => Experiments(db, id, null, null, null));
        api.MapGet("/experiments", (string? status, string? outcome, string? projectId, Database db) => Experiments(db, null, status, outcome, projectId));
        api.MapPost("/experiments/{id}/result-proposals", (string id, ResultRequest input, HttpContext ctx, KnowledgeService s) => Results.Accepted("/api/v1/experiments", s.ProposeResult(id, input, ctx.Items["actor"]?.ToString() ?? "human")));
        api.MapPost("/experiments/{id}/review-result", (string id, ResultReviewRequest input, HttpContext ctx, KnowledgeService s) => s.ReviewResult(id, input, Key(ctx)));
        api.MapPatch("/experiments/{id}", (string id, RunTransition input, HttpContext ctx, KnowledgeService k) => k.Mutate("run:" + id, Key(ctx), input, c =>
        {
            var run = c.One("SELECT * FROM experiment_runs WHERE id=$id", ("id", id)); Rules.Version(run.L("content_version"), input.ContentVersion);
            if (run.S("status") is not ("approved" or "running")) throw new DomainException("Experiment has ended.", 409);
            Rules.Choice(input.Status, ["running", "cancelled"]);
            c.Exec("UPDATE experiment_runs SET status=$status,started_utc=coalesce(started_utc,$now),updated_utc=$now,content_version=content_version+1 WHERE id=$id", ("id", id), ("status", input.Status), ("now", DateTimeOffset.UtcNow.ToString("O")));
            if (input.Status == "running") c.Exec("UPDATE knowledge_items SET validation_status=CASE WHEN validation_status='planned' THEN 'running' ELSE validation_status END,content_version=content_version+1 WHERE id=$id", ("id", run.L("knowledge_id")));
            return new { id, status = input.Status };
        }));
        api.MapPost("/knowledge/{id:long}/relations", (long id, RelationRequest input, HttpContext ctx, KnowledgeService k) => k.Mutate("relation:" + id, Key(ctx), input, c =>
        {
            var row = c.One("SELECT content_version FROM knowledge_items WHERE id=$id", ("id", id)); Rules.Version(row.L("content_version"), input.ContentVersion); Rules.Choice(input.RelationType, ["related_to", "duplicate_of", "supersedes", "contradicts"]);
            if (id == input.KnowledgeId) throw new DomainException("Cannot link an item to itself.");
            c.Exec("INSERT OR IGNORE INTO knowledge_relations VALUES($from,$to,$type)", ("from", id), ("to", input.KnowledgeId), ("type", input.RelationType)); c.Exec("UPDATE knowledge_items SET content_version=content_version+1 WHERE id=$id", ("id", id)); return new { id, input.KnowledgeId, input.RelationType };
        }));
        api.MapGet("/activity", (Database db) => { using var c = db.Open(); return c.Rows("SELECT id,entity_id AS entityId,actor,action,reason,created_utc AS createdUtc FROM audit ORDER BY id DESC LIMIT 100"); });
        api.MapGet("/settings", (ProjectService s) => s.Settings());
        api.MapPatch("/settings", (ScanSettings input, HttpContext ctx, ProjectService s) => s.SetSettings(input, Key(ctx)));
        api.MapGet("/diagnostics", (Database db) => { using var c = db.Open(); return new { schemaVersion = c.Scalar("SELECT max(version) FROM schema_migrations"), files = c.Scalar("SELECT count(*) FROM file_index WHERE removed=0"), queuedJobs = c.Scalar("SELECT count(*) FROM scan_jobs WHERE status='queued'"), aiEnabled = false, scannerVersion = 1 }; });
        api.MapPost("/backups", (Database db) =>
        {
            var id = Rules.NewId(); var directory = Path.Combine(Path.GetDirectoryName(db.Path)!, "backups"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, id + ".db"); db.BackupTo(path);
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            db.Write(c => { db.Audit(c, id, "human", "backup", null, new { id, sha256 = sha }); return 0; });
            return new { id, sha256 = sha, schemaVersion = Database.SchemaVersion, downloadUrl = "/api/v1/backups/" + id };
        });
        api.MapGet("/backups/{id}", (string id, Database db) =>
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new DomainException("Invalid backup ID.");
            var path = Path.Combine(Path.GetDirectoryName(db.Path)!, "backups", id + ".db");
            return File.Exists(path) ? Results.File(path, "application/vnd.sqlite3", "projectbrain-" + id + ".db") : Results.NotFound();
        });
        api.MapGet("/export", (Database db) =>
        {
            using var c = db.Open(); return Results.File(JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = Database.SchemaVersion, exportedUtc = DateTimeOffset.UtcNow, knowledge = c.Rows("SELECT * FROM knowledge_items"), captures = c.Rows("SELECT * FROM captures"), evidence = c.Rows("SELECT * FROM knowledge_evidence"), experiments = c.Rows("SELECT * FROM experiment_runs"), results = c.Rows("SELECT * FROM experiment_result_proposals"), reviews = c.Rows("SELECT * FROM knowledge_reviews") }, Rules.Json), "application/json", "projectbrain-export.json");
        });
    }
    private static object Similar(string id, Database db, KnowledgeService s)
    {
        using var c = db.Open(); var capture = c.One("SELECT k.title FROM captures cap JOIN knowledge_items k ON k.id=cap.knowledge_id WHERE cap.id=$id", ("id", id));
        return new { suggestedTitle = capture.S("title"), possibleDuplicates = s.Search(new SearchRequest(Query: capture.S("title"), Limit: 5)), automaticallyMerged = false };
    }
    private static object Experiments(Database db, long? knowledgeId, string? status, string? outcome, string? project)
    {
        using var c = db.Open();
        var rows = c.Rows("SELECT id,knowledge_id AS knowledgeId,project_id AS projectId,status,outcome,hypothesis,success_criteria AS successCriteria,procedure_md AS procedure,observed_result_md AS observations,limitations_md AS limitations,environment_json AS environmentJson,evidence_json AS evidenceJson,reviewed_by AS reviewedBy,content_version AS contentVersion,created_utc AS createdUtc FROM experiment_runs WHERE ($kid IS NULL OR knowledge_id=$kid) AND ($status IS NULL OR status=$status) AND ($outcome IS NULL OR outcome=$outcome) AND ($project IS NULL OR project_id=$project) ORDER BY created_utc DESC LIMIT 50", ("kid", knowledgeId), ("status", status), ("outcome", outcome), ("project", project));
        foreach (var row in rows) row["proposals"] = c.Rows("SELECT id,proposed_outcome AS proposedOutcome,observations_md AS observations,evidence_json AS evidenceJson,review_status AS reviewStatus,review_reason AS reviewReason FROM experiment_result_proposals WHERE experiment_id=$id ORDER BY created_utc", ("id", row.S("id")));
        return rows;
    }
}
public record RootRequest(string RelativePath, string Alias);
public record RootToggle(bool Enabled, long ContentVersion);
public record ScanRequest(string RootId, string Mode = "incremental");
public record RunTransition(long ContentVersion, string Status);
public record RelationRequest(long ContentVersion, long KnowledgeId, string RelationType);


