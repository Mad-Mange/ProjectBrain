using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ProjectBrain.Application;
using ProjectBrain.Domain;
namespace ProjectBrain.Infrastructure;

public sealed class KnowledgeService(Database db, TimeProvider clock) : IKnowledgeService
{
    public long ExperimentKnowledgeId(string id) { using var c = db.Open(); return c.One("SELECT knowledge_id FROM experiment_runs WHERE id=$id", ("id", id)).L("knowledge_id"); }
    private string Now => clock.GetUtcNow().ToString("O");
    public object Mutate(string scope, string? key, object request, Func<SqliteConnection, object> action) => db.Mutate(scope, key, request, action);
    private static void Project(SqliteConnection c, string? id)
    {
        if (id is not null && c.Scalar("SELECT id FROM projects WHERE id=$id", ("id", id)) is null) throw new DomainException("Unknown project.");
    }
    private long Insert(SqliteConnection c, string kind, string title, string summary, string body, string actor, string review, string intent, string[]? tags, string? projectId)
    {
        Rules.Choice(kind, Rules.Kinds); Rules.Choice(intent, Rules.Intents); Rules.Required(title, 800);
        if (title.Length > 200 || summary is null || summary.Length > 4000) throw new DomainException("Title or summary is too long.");
        Rules.Required(body); Project(c, projectId);
        c.Exec("INSERT INTO knowledge_items(kind,title,summary,body_md,proposed_by,review_status,intent_flag,project_id,created_utc,updated_utc) VALUES($kind,$title,$summary,$body,$actor,$review,$intent,$project,$now,$now)",
            ("kind", kind), ("title", Rules.SafeText(title)), ("summary", Rules.SafeText(summary)), ("body", Rules.SafeText(body)), ("actor", actor), ("review", review), ("intent", intent), ("project", projectId), ("now", Now));
        var id = Convert.ToInt64(c.Scalar("SELECT last_insert_rowid()")); if (projectId is not null) c.Exec("INSERT INTO knowledge_projects VALUES($id,$project)", ("id", id), ("project", projectId)); SetTags(c, id, tags); Index(c, id); return id;
    }
    private static void SetTags(SqliteConnection c, long id, string[]? tags)
    {
        c.Exec("DELETE FROM knowledge_tags WHERE knowledge_id=$id", ("id", id));
        foreach (var tag in Rules.Tags(tags)) c.Exec("INSERT INTO knowledge_tags VALUES($id,$tag)", ("id", id), ("tag", Rules.SafeText(tag)));
    }
    public static void Index(SqliteConnection c, long id)
    {
        c.Exec("DELETE FROM knowledge_fts WHERE rowid=$id", ("id", id));
        c.Exec("INSERT INTO knowledge_fts(rowid,title,summary,body,tags) SELECT id,title,summary,body_md,coalesce((SELECT group_concat(tag,' ') FROM knowledge_tags WHERE knowledge_id=k.id),'') FROM knowledge_items k WHERE id=$id", ("id", id));
    }
    public object Capture(CaptureRequest input, string? key) => Mutate("capture", key, input, c =>
    {
        Rules.Required(input.OriginalText); var url = Rules.Url(input.SourceUrl);
        var title = input.Title ?? Rules.Clip(input.OriginalText.Trim().Split('\n')[0], 100);
        var id = Insert(c, input.Kind, title, Rules.Clip(Rules.SafeText(input.OriginalText), 400), input.OriginalText, "human", "draft", input.IntentFlag, input.Tags, input.ProjectId);
        var captureId = Rules.NewId();
        c.Exec("INSERT INTO captures(id,knowledge_id,original_text,source_url,creator,idempotency_key,created_utc,updated_utc) VALUES($id,$kid,$text,$url,'human',$key,$now,$now)",
            ("id", captureId), ("kid", id), ("text", input.OriginalText), ("url", url), ("key", key), ("now", Now));
        return new { captureId, knowledgeId = id, reviewStatus = "draft", validationStatus = "untested", contentVersion = 1 };
    });
    public object Propose(ProposalRequest input) => Mutate("agent:propose", Rules.Required(input.ClientProposalId, 200), input, c =>
    {
        if (input.ProjectIds?.Length > 20) throw new DomainException("Too many projects.");
        foreach (var project in input.ProjectIds ?? []) Project(c, project);
        var id = Insert(c, input.Kind, input.Title, input.Summary, input.Body, "agent", "proposed", "none", input.Tags, input.ProjectIds?.FirstOrDefault());
        foreach (var project in input.ProjectIds ?? []) c.Exec("INSERT OR IGNORE INTO knowledge_projects VALUES($id,$project)", ("id", id), ("project", project));
        foreach (var e in ValidateEvidence(c, input.Evidence ?? []))
            c.Exec("INSERT INTO knowledge_evidence(knowledge_id,project_id,rel_path,file_sha256,evidence_note) VALUES($id,$project,$path,$hash,$note)", ("id", id), ("project", e.ProjectId), ("path", e.RelativePath), ("hash", e.FileSha256), ("note", e.Note ?? ""));
        return new { proposalId = id, knowledgeId = id, reviewStatus = "proposed", validationStatus = "untested", isVerified = false, requiresHumanReview = true };
    });
    public static Evidence[] ValidateEvidence(SqliteConnection c, Evidence[]? evidence)
    {
        if (evidence is null) throw new DomainException("Evidence array is required.");
        if (evidence.Length > 30) throw new DomainException("At most 30 evidence references.");
        return evidence.Select(e =>
        {
            if (string.IsNullOrWhiteSpace(e.RelativePath) || e.RelativePath.Contains('\\') || e.RelativePath.StartsWith('/') || e.RelativePath.Split('/').Any(p => p is ".." or "." or "") || e.RelativePath.Contains(':')) throw new DomainException("Evidence must reference an indexed relative path.");
            var file = c.Rows("SELECT sha256 FROM file_index WHERE project_id=$project AND rel_path=$path AND removed=0", ("project", e.ProjectId), ("path", e.RelativePath)).FirstOrDefault() ?? throw new DomainException("Evidence file is not indexed.");
            if (string.IsNullOrEmpty(file.S("sha256"))) throw new DomainException("Evidence requires a strong text fingerprint.");
            if (e.FileSha256 is not null && !e.FileSha256.Equals(file.S("sha256"), StringComparison.OrdinalIgnoreCase)) throw new DomainException("Evidence hash no longer matches.", 409);
            return e with { FileSha256 = file.S("sha256"), Note = Rules.Clip(Rules.SafeText(e.Note ?? ""), 2000) };
        }).ToArray();
    }
    private static bool CurrentProof(SqliteConnection c, long id)
    {
        foreach (var run in c.Rows("SELECT evidence_json FROM experiment_runs WHERE knowledge_id=$id AND outcome='passed' AND status='completed' AND reviewed_by IS NOT NULL", ("id", id)))
        {
            var evidence = JsonSerializer.Deserialize<Evidence[]>(run.S("evidence_json"), Rules.Json) ?? [];
            if (evidence.Length > 0 && evidence.All(e => c.Scalar("SELECT id FROM file_index WHERE project_id=$project AND rel_path=$path AND sha256=$hash AND removed=0", ("project", e.ProjectId), ("path", e.RelativePath), ("hash", e.FileSha256)) is not null)) return true;
        }
        return false;
    }
    public object Get(long id) { using var c = db.Open(); return Detail(c, id, true); }
    public Dictionary<string, object?> Detail(SqliteConnection c, long id, bool full)
    {
        var row = c.One("SELECT * FROM knowledge_items WHERE id=$id", ("id", id));
        var result = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["kind"] = row.S("kind"),
            ["title"] = row.S("title"),
            ["summary"] = row.S("summary"),
            ["reviewStatus"] = row.S("review_status"),
            ["validationStatus"] = row.S("validation_status"),
            ["intentFlag"] = row.S("intent_flag"),
            ["confidence"] = row.S("confidence"),
            ["needsReview"] = row.L("needs_review") != 0 || row.L("needs_recheck") != 0,
            ["contentVersion"] = row.L("content_version"),
            ["projectId"] = row.GetValueOrDefault("project_id"),
            ["createdUtc"] = row.S("created_utc"),
            ["updatedUtc"] = row.S("updated_utc"),
            ["tags"] = c.Rows("SELECT tag FROM knowledge_tags WHERE knowledge_id=$id", ("id", id)).Select(t => t.S("tag")).ToArray(),
            ["evidence"] = c.Rows("SELECT project_id AS projectId,rel_path AS relativePath,file_sha256 AS fileSha256,evidence_note AS note,stale_flag AS stale FROM knowledge_evidence WHERE knowledge_id=$id LIMIT 30", ("id", id)),
            ["sourceUnavailable"] = Convert.ToInt64(c.Scalar("SELECT count(*) FROM projects p JOIN scan_roots r ON p.root_id=r.id WHERE (p.id=$project OR p.id IN (SELECT project_id FROM knowledge_evidence WHERE knowledge_id=$id)) AND r.availability='unavailable'", ("id", id), ("project", row.GetValueOrDefault("project_id")))) > 0
        };
        result["sourceProjects"] = c.Rows("SELECT id,name,status FROM projects WHERE id=$project OR id IN (SELECT project_id FROM knowledge_projects WHERE knowledge_id=$id) OR id IN (SELECT project_id FROM knowledge_evidence WHERE knowledge_id=$id) LIMIT 30", ("id", id), ("project", row.GetValueOrDefault("project_id")));
        result["isVerified"] = Rules.Verified(row.S("review_status"), row.S("validation_status"), (bool)result["needsReview"]!, CurrentProof(c, id));
        if (full)
        {
            result["body"] = row.S("body_md");
            result["capture"] = c.Rows("SELECT id,original_text AS originalText,source_url AS sourceUrl FROM captures WHERE knowledge_id=$id", ("id", id)).FirstOrDefault();
            result["activity"] = c.Rows("SELECT actor,action,reason,created_utc AS createdUtc FROM audit WHERE entity_id LIKE $suffix ORDER BY id DESC LIMIT 100", ("suffix", "%:" + id));
            result["relations"] = c.Rows("SELECT to_id AS knowledgeId,relation_type AS relationType FROM knowledge_relations WHERE from_id=$id", ("id", id));
        }
        return result;
    }
    public object Search(SearchRequest input)
    {
        if (input.Query is null || input.Query.Length > 500 || input.Page < 1 || input.Page > 10000) throw new DomainException("Invalid search.");
        if (input.Kind is not null) Rules.Choice(input.Kind, Rules.Kinds);
        if (input.ReviewStatus is not null) Rules.Choice(input.ReviewStatus, Rules.Reviews);
        if (input.ValidationStatus is not null) Rules.Choice(input.ValidationStatus, Rules.Validations);
        if (input.IntentFlag is not null) Rules.Choice(input.IntentFlag, Rules.Intents);
        if (input.Tag?.Length > 60) throw new DomainException("Tag filter is too long.");
        using var c = db.Open(); var args = new List<(string, object?)>(); var predicates = new List<string> { "1=1" };
        var words = Regex.Matches(input.Query, @"[\p{L}\p{N}_]+").Cast<Match>().Take(20).Select(m => "\"" + m.Value + "\"").ToArray();
        var join = "";
        if (words.Length > 0) { join = " JOIN knowledge_fts ON knowledge_fts.rowid=k.id"; predicates.Add("knowledge_fts MATCH $query"); args.Add(("query", string.Join(" OR ", words))); }
        foreach (var (column, value) in new[] { ("kind", input.Kind), ("review_status", input.ReviewStatus), ("validation_status", input.ValidationStatus), ("intent_flag", input.IntentFlag), ("project_id", input.ProjectId) }) if (value is not null) { predicates.Add(column == "project_id" ? "(k.project_id=$project_id OR EXISTS(SELECT 1 FROM knowledge_projects WHERE knowledge_id=k.id AND project_id=$project_id) OR EXISTS(SELECT 1 FROM knowledge_evidence WHERE knowledge_id=k.id AND project_id=$project_id))" : $"k.{column}=${column}"); args.Add((column, value)); }
        if (input.Tag is not null) { predicates.Add("EXISTS(SELECT 1 FROM knowledge_tags WHERE knowledge_id=k.id AND tag=$tag)"); args.Add(("tag", input.Tag.ToLowerInvariant())); }
        if (input.NeedsReview is not null) { predicates.Add("(k.needs_review=1 OR k.needs_recheck=1)=$needs"); args.Add(("needs", input.NeedsReview.Value ? 1 : 0)); }
        if (!input.IncludeIdeas) predicates.Add("k.kind<>'idea'");
        if (input.Kinds is { Length: > 0 })
        {
            if (input.Kinds.Length > 5) throw new DomainException("Too many kinds.");
            var clauses = new List<string>();
            for (var i = 0; i < input.Kinds.Length; i++) { Rules.Choice(input.Kinds[i], Rules.Kinds); clauses.Add("k.kind=$kind" + i); args.Add(("kind" + i, input.Kinds[i])); }
            predicates.Add("(" + string.Join(" OR ", clauses) + ")");
        }
        if (input.Technologies is { Length: > 0 })
        {
            var tags = Rules.Tags(input.Technologies); var clauses = new List<string>();
            for (var i = 0; i < tags.Length; i++) { clauses.Add("tag=$tech" + i); args.Add(("tech" + i, tags[i])); }
            predicates.Add("EXISTS(SELECT 1 FROM knowledge_tags WHERE knowledge_id=k.id AND (" + string.Join(" OR ", clauses) + "))");
        }
        var from = " FROM knowledge_items k" + join + " WHERE " + string.Join(" AND ", predicates);
        var total = Convert.ToInt64(c.Scalar("SELECT count(*)" + from, args.ToArray()));
        var limit = Math.Clamp(input.Limit, 1, 50); args.Add(("limit", limit)); args.Add(("offset", (input.Page - 1) * limit));
        var ids = c.Rows("SELECT k.id" + from + " ORDER BY (k.review_status='approved') DESC,(k.validation_status='passed' AND k.needs_review=0) DESC," + (words.Length > 0 ? "bm25(knowledge_fts)," : "") + "k.id DESC LIMIT $limit OFFSET $offset", args.ToArray());
        return new { results = ids.Select(r => Detail(c, r.L("id"), false)).ToArray(), total, page = input.Page, pageSize = limit, truncated = total > input.Page * limit, searchMode = "fts5" };
    }
    public object Edit(long id, EditRequest input, string key) => Mutate("edit:" + id, key, input, c =>
    {
        var before = c.One("SELECT * FROM knowledge_items WHERE id=$id", ("id", id)); Rules.Version(before.L("content_version"), input.ContentVersion);
        Rules.Required(input.Title, 800); if (input.Title.Length > 200 || input.Summary is null || input.Summary.Length > 4000) throw new DomainException("Title or summary too long."); Rules.Required(input.Body); Rules.Choice(input.Kind, Rules.Kinds); Project(c, input.ProjectId);
        c.Exec("UPDATE knowledge_items SET title=$title,kind=$kind,summary=$summary,body_md=$body,project_id=$project,content_version=content_version+1,updated_utc=$now,needs_review=CASE WHEN validation_status='passed' THEN 1 ELSE needs_review END,validation_status=CASE WHEN validation_status='passed' THEN 'stale' ELSE validation_status END WHERE id=$id",
            ("id", id), ("title", Rules.SafeText(input.Title)), ("kind", input.Kind), ("summary", Rules.SafeText(input.Summary)), ("body", Rules.SafeText(input.Body)), ("project", input.ProjectId), ("now", Now));
        SetTags(c, id, input.Tags); Index(c, id); db.Audit(c, "knowledge:" + id, "human", "edit", before, Detail(c, id, false)); return Detail(c, id, true);
    });
    public object Intent(long id, IntentRequest input, string key) => Mutate("intent:" + id, key, input, c =>
    {
        var row = c.One("SELECT content_version FROM knowledge_items WHERE id=$id", ("id", id)); Rules.Version(row.L("content_version"), input.ContentVersion); Rules.Choice(input.IntentFlag, Rules.Intents);
        c.Exec("UPDATE knowledge_items SET intent_flag=$intent,content_version=content_version+1,updated_utc=$now WHERE id=$id", ("id", id), ("intent", input.IntentFlag), ("now", Now)); return Detail(c, id, false);
    });
    public object Review(long id, DecisionRequest input, string key) => Mutate("review:" + id, key, input, c =>
    {
        var row = c.One("SELECT * FROM knowledge_items WHERE id=$id", ("id", id)); Rules.Version(row.L("content_version"), input.ContentVersion);
        Rules.Choice(input.ReviewStatus, ["approved", "rejected", "deprecated"]); Rules.Required(input.Reason, 2000);
        c.Exec("UPDATE knowledge_items SET review_status=$status,content_version=content_version+1,updated_utc=$now WHERE id=$id", ("id", id), ("status", input.ReviewStatus), ("now", Now));
        c.Exec("INSERT INTO knowledge_reviews(knowledge_id,from_state,to_state,actor,reason,timestamp_utc) VALUES($id,$old,$new,'human',$reason,$now)", ("id", id), ("old", row.S("review_status")), ("new", input.ReviewStatus), ("reason", Rules.SafeText(input.Reason)), ("now", Now));
        db.Audit(c, "knowledge:" + id, "human", "review", row, Detail(c, id, false), Rules.SafeText(input.Reason)); return Detail(c, id, false);
    });
    public object Plan(long id, PlanRequest input, string key) => Mutate("plan:" + id, key, input, c =>
    {
        var row = c.One("SELECT * FROM knowledge_items WHERE id=$id", ("id", id)); Rules.Version(row.L("content_version"), input.ContentVersion);
        Rules.Required(input.Hypothesis, 4000); Rules.Required(input.SuccessCriteria, 4000); Project(c, input.ProjectId);
        if (input.Procedure is null || input.Environment is null || input.Limitations is null || input.Procedure.Length > 16000 || input.Environment.Length > 4000 || input.Limitations.Length > 4000) throw new DomainException("Experiment text too long.");
        var runId = Rules.NewId();
        c.Exec("INSERT INTO experiment_runs(id,knowledge_id,project_id,status,hypothesis,success_criteria,procedure_md,environment_json,limitations_md,created_by,created_utc,updated_utc) VALUES($run,$id,$project,'approved',$hypothesis,$criteria,$procedure,$environment,$limitations,'human',$now,$now)",
            ("run", runId), ("id", id), ("project", input.ProjectId), ("hypothesis", Rules.SafeText(input.Hypothesis)), ("criteria", Rules.SafeText(input.SuccessCriteria)), ("procedure", Rules.SafeText(input.Procedure)), ("environment", JsonSerializer.Serialize(new { description = Rules.SafeText(input.Environment) })), ("limitations", Rules.SafeText(input.Limitations)), ("now", Now));
        c.Exec("UPDATE knowledge_items SET validation_status=CASE WHEN validation_status IN ('untested','planned') THEN 'planned' ELSE validation_status END,content_version=content_version+1,updated_utc=$now WHERE id=$id", ("id", id), ("now", Now));
        return new { experimentId = runId, knowledgeId = id, status = "approved", contentVersion = 1 };
    });
    public object ProposeResult(string id, ResultRequest input, string actor) => Mutate((actor == "agent" ? "agent:result:" : "result:") + id, Rules.Required(input.ClientProposalId, 200), input, c =>
    {
        var run = c.One("SELECT * FROM experiment_runs WHERE id=$id", ("id", id));
        if (run.S("status") is not ("approved" or "running")) throw new DomainException("Experiment is not open for results.", 409);
        Rules.Choice(input.ProposedOutcome, Rules.Outcomes); Rules.Required(input.Observations, 16000);
        var evidence = ValidateEvidence(c, input.Evidence); if (evidence.Length == 0) throw new DomainException("Result needs indexed test/log evidence.");
        var proposal = Rules.NewId();
        c.Exec("INSERT INTO experiment_result_proposals(id,experiment_id,submitted_by,proposed_outcome,observations_md,evidence_json,idempotency_key,created_utc) VALUES($id,$run,$actor,$outcome,$observations,$evidence,$key,$now)",
            ("id", proposal), ("run", id), ("actor", actor), ("outcome", input.ProposedOutcome), ("observations", Rules.SafeText(input.Observations)), ("evidence", JsonSerializer.Serialize(evidence, Rules.Json)), ("key", input.ClientProposalId), ("now", Now));
        return new { proposalId = proposal, experimentId = id, reviewStatus = "pending", isVerified = false, requiresHumanReview = true };
    });
    public object ResolveValidation(long id, ResolveValidationRequest input, string key) => Mutate("resolve-validation:" + id, key, input, c =>
    {
        var item = c.One("SELECT * FROM knowledge_items WHERE id=$id", ("id", id)); Rules.Version(item.L("content_version"), input.ContentVersion);
        Rules.Required(input.Reason, 2000); if (!input.EvidenceChecked) throw new DomainException("Explicit evidence confirmation is required.");
        var run = c.One("SELECT * FROM experiment_runs WHERE id=$run AND knowledge_id=$id AND status='completed' AND reviewed_by='human'", ("run", input.ExperimentId), ("id", id));
        ValidateEvidence(c, JsonSerializer.Deserialize<Evidence[]>(run.S("evidence_json"), Rules.Json));
        c.Exec("UPDATE knowledge_items SET validation_status=$outcome,needs_review=0,needs_recheck=0,content_version=content_version+1,updated_utc=$now WHERE id=$id", ("id", id), ("outcome", run.S("outcome")), ("now", Now));
        db.Audit(c, "knowledge:" + id, "human", "resolve_validation", item, new { experimentId = input.ExperimentId, outcome = run.S("outcome") }, Rules.SafeText(input.Reason));
        return Detail(c, id, false);
    });
    public object ReviewResult(string id, ResultReviewRequest input, string key) => Mutate("result-review:" + id, key, input, c =>
    {
        var run = c.One("SELECT * FROM experiment_runs WHERE id=$id", ("id", id)); Rules.Version(run.L("content_version"), input.ContentVersion);
        if (run.S("status") is "completed" or "cancelled") throw new DomainException("Experiment has already ended.", 409);
        var proposal = c.One("SELECT * FROM experiment_result_proposals WHERE id=$id AND experiment_id=$run", ("id", input.ProposalId), ("run", id));
        if (proposal.S("review_status") != "pending") throw new DomainException("Proposal has already been reviewed.", 409);
        Rules.Choice(input.Decision, ["accept", "reject"]); Rules.Required(input.Reason, 2000);
        if (input.Decision == "accept")
        {
            if (!input.EvidenceChecked) throw new DomainException("Explicitly confirm the evidence check.");
            var evidence = ValidateEvidence(c, JsonSerializer.Deserialize<Evidence[]>(proposal.S("evidence_json"), Rules.Json) ?? []);
            if (evidence.Length == 0) throw new DomainException("Evidence is required.");
            c.Exec("UPDATE experiment_runs SET status='completed',outcome=$outcome,observed_result_md=$observed,evidence_json=$evidence,reviewed_by='human',ended_utc=$now,updated_utc=$now,content_version=content_version+1 WHERE id=$id",
                ("id", id), ("outcome", proposal.S("proposed_outcome")), ("observed", proposal.S("observations_md")), ("evidence", proposal.S("evidence_json")), ("now", Now));
            var kid = run.L("knowledge_id");
            foreach (var e in evidence) c.Exec("INSERT INTO knowledge_evidence(knowledge_id,project_id,rel_path,file_sha256,evidence_note) VALUES($kid,$project,$path,$hash,$note)", ("kid", kid), ("project", e.ProjectId), ("path", e.RelativePath), ("hash", e.FileSha256), ("note", e.Note ?? "Experiment evidence"));
            var conflict = Convert.ToInt64(c.Scalar("SELECT count(DISTINCT outcome) FROM experiment_runs WHERE knowledge_id=$id AND status='completed' AND outcome IN ('passed','failed')", ("id", kid))) > 1;
            c.Exec("UPDATE knowledge_items SET validation_status=$outcome,needs_review=$conflict,needs_recheck=0,content_version=content_version+1,updated_utc=$now WHERE id=$id",
                ("id", kid), ("outcome", conflict ? "inconclusive" : proposal.S("proposed_outcome")), ("conflict", conflict ? 1 : 0), ("now", Now));
        }
        else c.Exec("UPDATE experiment_runs SET content_version=content_version+1,updated_utc=$now WHERE id=$id", ("id", id), ("now", Now));
        c.Exec("UPDATE experiment_result_proposals SET review_status=$status,reviewer='human',review_reason=$reason,reviewed_utc=$now WHERE id=$id",
            ("id", input.ProposalId), ("status", input.Decision == "accept" ? "accepted" : "rejected"), ("reason", Rules.SafeText(input.Reason)), ("now", Now));
        db.Audit(c, "knowledge:" + run.L("knowledge_id"), "human", "experiment_review", run, proposal, Rules.SafeText(input.Reason));
        return Detail(c, run.L("knowledge_id"), false);
    });
}



