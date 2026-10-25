using System.Text.Json;
using ProjectBrain.Domain;
using ProjectBrain.Infrastructure;
namespace ProjectBrain.Tests;

public sealed class ReliabilityTests
{
    [Fact]
    public async Task RescanCorrectsLegacyFixtureClassificationWithoutInventingFileChanges()
    {
        using var w = new TestWorkspace(); w.File("App/App.slnx", "<Solution />");
        w.File("App/tests/Unity/ProjectSettings/ProjectVersion.txt", "m_EditorVersion: synthetic");
        var root = w.Root(); await w.Scan(root); using var c = w.Db.Open();
        var project = c.One("SELECT * FROM projects");
        c.Exec("UPDATE projects SET kind='unity' WHERE id=$id", ("id", project.S("id")));
        var events = c.Scalar("SELECT count(*) FROM change_events");
        await w.Scan(root);
        Assert.Equal("dotnet", c.Scalar("SELECT kind FROM projects"));
        Assert.Equal(project.L("revision") + 1, c.Scalar("SELECT revision FROM projects"));
        Assert.Equal(events, c.Scalar("SELECT count(*) FROM change_events"));
        Assert.Equal("dotnet", JsonDocument.Parse(c.One("SELECT profile_json FROM projects").S("profile_json")).RootElement.GetProperty("technology").GetString());
    }
    [Fact]
    public void ContainmentChecksExistingFilesAndMissingCandidates()
    {
        using var w = new TestWorkspace(); w.File("App/source.py", "x=1");
        var source = Path.Combine(w.Sources, "App", "source.py");
        Assert.Equal(source, SourceSafety.Contained(w.Sources, source));
        var missing = Path.Combine(w.Sources, "App", "missing", "file.py");
        Assert.Equal(503, Assert.Throws<DomainException>(() => SourceSafety.Contained(w.Sources, missing)).Status);
        Assert.Equal(missing, SourceSafety.Contained(w.Sources, missing, false));
    }
    [Fact]
    public async Task GeneratedSecurityCachesAndCustomPythonEnvironmentsAreExcluded()
    {
        using var w = new TestWorkspace();
        w.File("App/main.py", "print('source')");
        w.File("App/artifacts/ci/trivy-cache/fanal/generated.json", "{}");
        w.File("App/vendor/site-packages/library.py", "installed_dependency = True");
        w.File("App/tools/custom-env/pyvenv.cfg", "home = synthetic-python");
        w.File("App/tools/custom-env/Lib/dependency.py", "installed_dependency = True");
        w.File("App/artifacts/results.txt", "Keep experiment evidence outside dependency caches.");
        var root = w.Root();
        Assert.Equal("succeeded", (await w.Scan(root)).GetProperty("status").GetString());
        using var c = w.Db.Open();
        Assert.Equal(2L, c.Scalar("SELECT count(*) FROM file_index"));
        Assert.Equal(1L, c.Scalar("SELECT count(*) FROM file_index WHERE rel_path='artifacts/results.txt'"));
        await w.Scan(root);
        Assert.Equal(1L, c.Scalar("SELECT revision FROM projects WHERE name='App'"));
    }
    [Fact]
    public async Task MultipleUnrealManifestsHaveSeparateIdentitiesAndSharedSourceEvidence()
    {
        using var w = new TestWorkspace(); w.File("Games/Alpha.uproject", "{\"EngineAssociation\":\"5.6\"}");
        w.File("Games/Beta.uproject", "{\"EngineAssociation\":\"5.5\"}"); w.File("Games/Source/Rules.cpp", "class Rules {};");
        var root = w.Root(); await w.Scan(root); using var c = w.Db.Open();
        Assert.Equal(2L, c.Scalar("SELECT count(*) FROM projects"));
        Assert.Equal(2L, c.Scalar("SELECT count(*) FROM file_index WHERE rel_path='Source/Rules.cpp'"));
        Assert.Equal(1L, c.Scalar("SELECT count(*) FROM file_index WHERE rel_path='Alpha.uproject'"));
        await w.Scan(root); Assert.Equal(2L, c.Scalar("SELECT sum(revision) FROM projects"));
        w.File("Games/Source/Rules.cpp", "class ChangedRules {};"); await w.Scan(root);
        Assert.Equal(4L, c.Scalar("SELECT sum(revision) FROM projects"));
        w.File("Games/Alpha.uproject", "{\"EngineAssociation\":\"5.7\"}"); await w.Scan(root);
        Assert.Equal(3L, c.Scalar("SELECT revision FROM projects WHERE name='Alpha'"));
        Assert.Equal(2L, c.Scalar("SELECT revision FROM projects WHERE name='Beta'"));
    }
    [Fact]
    public async Task DirectoryExceptionsAreProjectScopedAndCannotIncludeCredentials()
    {
        using var w = new TestWorkspace(); w.File("Tool/build/source.py", "x=1"); w.File("Other/build/source.py", "y=1"); w.File("Tool/build/.env", "SECRET_CANARY=private");
        var root = w.Root(); w.Projects.SetSettings(new ScanSettings(IncludeDirectories: ["Tool/build"])); await w.Scan(root);
        using var c = w.Db.Open(); Assert.Equal(1L, c.Scalar("SELECT count(*) FROM file_index"));
        Assert.Equal("Tool", c.Scalar("SELECT name FROM projects"));
        Assert.Throws<DomainException>(() => w.Projects.SetSettings(new ScanSettings(ContentVersion: 2, IncludeDirectories: ["Tool/.ssh"])));
        Assert.Throws<DomainException>(() => w.Projects.SetSettings(new ScanSettings(ContentVersion: 2, IncludeDirectories: ["../build"])));
    }
    [Fact]
    public async Task GitHeadChangesRevisionWithoutReadingObjectsOrExecutingHooks()
    {
        using var w = new TestWorkspace(); w.File("App/main.py", "x=1");
        w.File("App/.git/HEAD", "ref: refs/heads/main"); w.File("App/.git/refs/heads/main", new string('a', 40));
        w.File("App/.git/objects/secret.txt", "NEVER_READ_OBJECTS");
        var root = w.Root(); await w.Scan(root); using var c = w.Db.Open();
        var profile = JsonSerializer.Deserialize<JsonElement>(c.One("SELECT profile_json FROM projects").S("profile_json"));
        Assert.Equal(new string('A', 40), profile.GetProperty("gitHead").GetString());
        w.File("App/.git/refs/heads/main", new string('b', 40)); await w.Scan(root);
        Assert.Equal(2L, c.Scalar("SELECT revision FROM projects"));
        Assert.Equal(1L, c.Scalar("SELECT count(*) FROM change_events WHERE event_type='git_head_changed'"));
        Assert.Equal(1L, c.Scalar("SELECT count(*) FROM file_index"));
        w.File("App/.git/HEAD", "ref: refs/../../outside");
        Assert.Null(GitMetadata.ReadHead(Path.Combine(w.Sources, "App"), _ => { }));
    }
    [Fact]
    public async Task ByteBudgetRetryReusesDurableReadsWithoutPublishingPartialInventory()
    {
        using var w = new TestWorkspace();
        w.File("App/a.cs", "class A {}"); w.File("App/b.cs", "class B {}");
        var root = w.Root(); w.Projects.SetSettings(new ScanSettings(MaxBytes: 10));
        Assert.Equal("partial", (await w.Scan(root)).GetProperty("status").GetString());
        using var c = w.Db.Open();
        Assert.Equal(0L, c.Scalar("SELECT count(*) FROM projects"));
        Assert.Equal(1L, c.Scalar("SELECT count(*) FROM scan_checkpoints"));
        var restarted = new ScanEngine(w.Db, w.Projects, TimeProvider.System);
        var id = TestWorkspace.Json(restarted.Enqueue(root, "incremental")).GetProperty("id").GetString();
        await restarted.ProcessNext(CancellationToken.None);
        var result = c.One("SELECT * FROM scan_jobs WHERE id=$id", ("id", id));
        Assert.Equal("succeeded", result.S("status"));
        Assert.Equal(1, JsonDocument.Parse(result.S("counts_json")).RootElement.GetProperty("resumed").GetInt32());
        Assert.Equal(2L, c.Scalar("SELECT count(*) FROM file_index"));
        Assert.Equal(0L, c.Scalar("SELECT count(*) FROM scan_checkpoints"));
    }

    [Fact]
    public async Task ChangedCheckpointIsReadAgainAndAuditNeverUsesCheckpointAsProof()
    {
        using var w = new TestWorkspace(); w.File("App/a.cs", "class A {}"); w.File("App/b.cs", "class B {}");
        var root = w.Root(); w.Projects.SetSettings(new ScanSettings(MaxBytes: 10)); await w.Scan(root);
        w.File("App/a.cs", "class Changed {}");
        w.Projects.SetSettings(new ScanSettings(ContentVersion: 2));
        var result = await w.Scan(root, "full_audit");
        var counts = JsonDocument.Parse(result.GetProperty("counts_json").GetString()!).RootElement;
        Assert.Equal(2, counts.GetProperty("hashed").GetInt32()); Assert.Equal(0, counts.GetProperty("resumed").GetInt32());
        using var c = w.Db.Open(); Assert.Contains("Changed", c.One("SELECT extract FROM file_index WHERE rel_path='a.cs'").S("extract"));
    }

    [Fact]
    public async Task CancelRequestedBeforeCrashIsNotRescheduled()
    {
        using var w = new TestWorkspace(); var root = w.Root();
        var id = TestWorkspace.Json(w.Scanner.Enqueue(root, "incremental")).GetProperty("id").GetString()!;
        using var c = w.Db.Open(); c.Exec("UPDATE scan_jobs SET status='running',cancel_requested=1 WHERE id=$id", ("id", id));
        w.Scanner.Recover(); Assert.Equal("cancelled", c.Scalar("SELECT status FROM scan_jobs WHERE id=$id", ("id", id)));
        Assert.False(await w.Scanner.ProcessNext(CancellationToken.None));
    }

    [Fact]
    public async Task ScheduleIsSixHourlyWeeklyAuditedPausedAndDeduplicated()
    {
        using var w = new TestWorkspace(); w.File("App/a.py", "x=1"); var root = w.Root();
        var clock = new ManualClock(); var projects = new ProjectService(w.Db, w.Sources, clock); var scanner = new ScanEngine(w.Db, projects, clock);
        scanner.Schedule(); Assert.False(await scanner.ProcessNext(CancellationToken.None)); // First import is explicit.
        scanner.Enqueue(root, "full_audit"); await scanner.ProcessNext(CancellationToken.None);
        clock.Utc = clock.Utc.AddHours(5); scanner.Schedule(); Assert.False(await scanner.ProcessNext(CancellationToken.None));
        clock.Utc = clock.Utc.AddHours(1); scanner.Schedule(); scanner.Schedule();
        using var c = w.Db.Open(); Assert.Equal(1L, c.Scalar("SELECT count(*) FROM scan_jobs WHERE status='queued'"));
        Assert.Equal("incremental", c.Scalar("SELECT mode FROM scan_jobs WHERE status='queued'")); await scanner.ProcessNext(CancellationToken.None);
        projects.SetSettings(new ScanSettings(Paused: true)); clock.Utc = clock.Utc.AddDays(8); scanner.Schedule(); Assert.False(await scanner.ProcessNext(CancellationToken.None));
        projects.SetSettings(new ScanSettings(ContentVersion: 2)); clock.Utc = clock.Utc.AddHours(6); scanner.Schedule();
        Assert.Equal("full_audit", c.Scalar("SELECT mode FROM scan_jobs WHERE status='queued'"));
    }

    [Fact]
    public async Task ConflictingResultsRequireExplicitResolutionAndSurviveBackup()
    {
        using var w = new TestWorkspace(); w.File("Active/results.txt", "Synthetic passing and failing outcomes for different scopes"); var root = w.Root(); await w.Scan(root);
        using var c = w.Db.Open(); var project = c.One("SELECT id FROM projects").S("id"); var kid = w.Capture();
        w.Knowledge.Review(kid, new DecisionRequest(1, "approved", "Useful hypothesis"), "approve");
        string Complete(string outcome)
        {
            var version = w.Item(kid).GetProperty("contentVersion").GetInt64();
            var run = TestWorkspace.Json(w.Knowledge.Plan(kid, new PlanRequest(version, "Scope " + outcome, "Document observed outcome", ProjectId: project), "plan-" + outcome)).GetProperty("experimentId").GetString()!;
            var request = new ResultRequest(outcome, "Observed " + outcome, [new Evidence(project, "results.txt")], "proposal-" + outcome);
            var proposal = TestWorkspace.Json(w.Knowledge.ProposeResult(run, request, "agent"));
            Assert.Equal(proposal.GetRawText(), TestWorkspace.Json(w.Knowledge.ProposeResult(run, request, "agent")).GetRawText());
            w.Knowledge.ReviewResult(run, new ResultReviewRequest(1, proposal.GetProperty("proposalId").GetString()!, "accept", "Checked synthetic evidence", true), "review-" + outcome);
            return run;
        }
        var passed = Complete("passed"); Assert.True(w.Item(kid).GetProperty("isVerified").GetBoolean());
        Complete("failed"); Assert.Equal("inconclusive", w.Item(kid).GetProperty("validationStatus").GetString()); Assert.False(w.Item(kid).GetProperty("isVerified").GetBoolean());
        w.Knowledge.ResolveValidation(kid, new ResolveValidationRequest(w.Item(kid).GetProperty("contentVersion").GetInt64(), passed, "Only the passing environment applies to this claim", true), "resolve");
        Assert.True(w.Item(kid).GetProperty("isVerified").GetBoolean());
        var backup = Path.Combine(w.DirectoryPath, "restored.db"); w.Db.BackupTo(backup);
        var restored = new Database(backup); restored.Migrate(); using var rc = restored.Open();
        Assert.Equal(2L, rc.Scalar("SELECT count(*) FROM experiment_runs")); Assert.Equal(2L, rc.Scalar("SELECT count(*) FROM experiment_result_proposals"));
        Assert.True(TestWorkspace.Json(new KnowledgeService(restored, TimeProvider.System).Get(kid)).GetProperty("isVerified").GetBoolean());
    }

    [Fact]
    public void ProductionMigrationUpgradesLegacyVerificationWithoutInventingProof()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pb-upgrade-" + Guid.NewGuid().ToString("N"));
        try
        {
            var db = new Database(Path.Combine(folder, "brain.db")); db.Migrate(1);
            using (var c = db.Open()) c.Exec("INSERT INTO knowledge_items(kind,title,summary,body_md,state,proposed_by,created_utc,updated_utc) VALUES('idea','Old claim','Unknown proof','Retain original','verified','human','now','now')");
            db.Migrate(); db.Migrate(); using var current = db.Open();
            Assert.Equal((long)Database.SchemaVersion, current.Scalar("SELECT max(version) FROM schema_migrations"));
            var item = TestWorkspace.Json(new KnowledgeService(db, TimeProvider.System).Get(1));
            Assert.False(item.GetProperty("isVerified").GetBoolean()); Assert.True(item.GetProperty("needsReview").GetBoolean());
            Assert.True(System.IO.File.Exists(db.Path + ".before-upgrade"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }
}
