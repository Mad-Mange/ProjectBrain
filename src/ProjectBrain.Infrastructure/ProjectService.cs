using System.Text.Json;
using ProjectBrain.Domain;
namespace ProjectBrain.Infrastructure;

public sealed class ProjectService(Database db, string mount, TimeProvider clock)
{
    public string Mount { get; } = Path.GetFullPath(mount);
    public object Register(string relativePath, string alias, string? key = null)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') || relativePath.Split('/', '\\').Any(p => p == "..")) throw new DomainException("Use a relative subdirectory of the configured source mount. Add other host folders through Compose.");
        var path = SourceSafety.Contained(Mount, Path.Combine(Mount, relativePath));
        Rules.Required(alias, 100);
        return db.Mutate("register-root", key, new { relativePath, alias }, c =>
        {
            var existing = c.Rows("SELECT id FROM scan_roots WHERE path=$path", ("path", path)).FirstOrDefault(); if (existing is not null) return (object)new { id = existing.S("id") };
            var id = Rules.NewId(); c.Exec("INSERT INTO scan_roots(id,alias,path) VALUES($id,$alias,$path)", ("id", id), ("alias", alias), ("path", path));
            db.Audit(c, id, "human", "register_root", null, new { id, alias }); return new { id };
        });
    }
    public ScanSettings Settings()
    {
        using var c = db.Open(); var json = c.Scalar("SELECT value_json FROM settings WHERE key='scan'") as string;
        return json is null ? new ScanSettings() : JsonSerializer.Deserialize<ScanSettings>(json, Rules.Json)!;
    }
    public object SetSettings(ScanSettings settings, string? key = null)
    {
        if (settings.IncludeDirectories?.Length > 100 || settings.IncludeDirectories?.Any(p => !SourceSafety.ValidOverride(p)) == true) throw new DomainException("Include only root-relative build, Library, or Saved directories, such as MyTool/build. Credential and cache exclusions cannot be overridden.");
        if (settings.IntervalHours < 0.05 || settings.IntervalHours > 8760 || settings.AuditDays < 1 || settings.AuditDays > 365 || settings.MaxFiles < 1 || settings.MaxFiles > 300000 || settings.MaxDepth < 1 || settings.MaxDepth > 100 || settings.MaxBytes < 1 || settings.MaxBytes > 8L * 1024 * 1024 * 1024 || settings.MaxTextBytes < 1 || settings.MaxTextBytes > 4 * 1024 * 1024 || settings.MaxMinutes < 1 || settings.MaxMinutes > 1440 || settings.QuietStartHourUtc is < 0 or > 23 || settings.QuietEndHourUtc is < 0 or > 23) throw new DomainException("Scanner settings exceed supported limits.");
        return db.Mutate("settings", key, settings, c =>
        {
            var currentJson = c.Scalar("SELECT value_json FROM settings WHERE key='scan'") as string;
            var current = currentJson is null ? new ScanSettings() : JsonSerializer.Deserialize<ScanSettings>(currentJson, Rules.Json)!;
            Rules.Version(current.ContentVersion, settings.ContentVersion);
            settings = settings with { ContentVersion = settings.ContentVersion + 1 };
            c.Exec("INSERT INTO settings VALUES('scan',$json) ON CONFLICT(key) DO UPDATE SET value_json=excluded.value_json", ("json", JsonSerializer.Serialize(settings, Rules.Json)));
            c.Exec("UPDATE scan_roots SET next_scan_utc=$next WHERE enabled=1", ("next", clock.GetUtcNow().AddHours(settings.IntervalHours).ToString("O")));
            db.Audit(c, "settings", "human", "settings", null, settings); return (object)settings;
        });
    }
    public object Roots() { using var c = db.Open(); return c.Rows("SELECT id,alias,enabled,availability,content_version AS contentVersion,last_success_utc AS lastSuccessUtc,last_audit_utc AS lastAuditUtc,next_scan_utc AS nextScanUtc FROM scan_roots ORDER BY alias"); }
    public object Projects(string query = "", string? kind = null, int page = 1)
    {
        using var c = db.Open();
        return c.Rows("SELECT id,name,kind,relative_path AS relativePath,status,revision,updated_utc AS updatedUtc,analysis_due AS analysisDue FROM projects WHERE name LIKE $q AND ($kind IS NULL OR kind=$kind) ORDER BY name LIMIT 50 OFFSET $offset", ("q", "%" + Rules.Clip(query, 200) + "%"), ("kind", kind), ("offset", Math.Clamp(page - 1, 0, 10000) * 50));
    }
    public object Profile(string id)
    {
        using var c = db.Open(); var p = c.One("SELECT p.*,r.availability FROM projects p JOIN scan_roots r ON r.id=p.root_id WHERE p.id=$id", ("id", id));
        return new { id, name = p.S("name"), kind = p.S("kind"), relativePath = p.S("relative_path"), revision = p.L("revision"), sourceUnavailable = p.S("availability") == "unavailable", profile = JsonSerializer.Deserialize<JsonElement>(p.S("profile_json")), files = c.Rows("SELECT rel_path AS relativePath,sha256 AS fileSha256,bytes,removed,substr(extract,1,1000) AS excerpt FROM file_index WHERE project_id=$id ORDER BY rel_path LIMIT 50", ("id", id)) };
    }
    public object Changes(string id) { using var c = db.Open(); return c.Rows("SELECT event_type AS eventType,old_path AS oldPath,new_path AS newPath,old_hash AS oldHash,new_hash AS newHash,created_utc AS createdUtc FROM change_events WHERE project_id=$id ORDER BY id DESC LIMIT 100", ("id", id)); }
    public object Candidates(int limit = 10, bool changedOnly = false)
    {
        using var c = db.Open(); return c.Rows("SELECT id,name,kind,revision FROM projects WHERE analysis_due=1 AND ($changed=0 OR revision>1) ORDER BY updated_utc DESC LIMIT $limit", ("changed", changedOnly ? 1 : 0), ("limit", Math.Clamp(limit, 1, 10)));
    }
}

