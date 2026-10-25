using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProjectBrain.Domain;
namespace ProjectBrain.Infrastructure;

public sealed class Database
{
    public const int SchemaVersion = 4;
    public string Path { get; }
    public object Gate { get; } = new();
    public Database(string path) { Path = System.IO.Path.GetFullPath(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!); }
    public SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, ForeignKeys = true, DefaultTimeout = 30, Pooling = false }.ToString());
        c.Open(); c.Exec("PRAGMA busy_timeout=30000;"); return c;
    }
    public void Migrate(int target = SchemaVersion)
    {
        lock (Gate)
        {
            using var c = Open();
            c.Exec("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);");
            var current = Convert.ToInt32(c.Scalar("SELECT coalesce(max(version),0) FROM schema_migrations"));
            if (current > target) throw new InvalidOperationException("Database is newer than this application.");
            if (current > 0 && current < target) BackupTo(Path + ".before-upgrade");
            var assembly = typeof(Database).Assembly;
            for (var version = current + 1; version <= target; version++)
            {
                var resource = assembly.GetManifestResourceNames().Single(n => n.Contains($"Migrations.{version:000}_"));
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                c.Exec("BEGIN IMMEDIATE;");
                try { c.Exec(reader.ReadToEnd()); c.Exec("INSERT INTO schema_migrations VALUES($v,$now)", ("v", version), ("now", DateTimeOffset.UtcNow.ToString("O"))); c.Exec("COMMIT;"); }
                catch { c.Exec("ROLLBACK;"); throw; }
            }
            if ((string?)c.Scalar("PRAGMA integrity_check") != "ok") throw new InvalidOperationException("Database integrity check failed.");
        }
    }
    public T Write<T>(Func<SqliteConnection, T> action)
    {
        lock (Gate) { using var c = Open(); c.Exec("BEGIN IMMEDIATE;"); try { var result = action(c); c.Exec("COMMIT;"); return result; } catch { c.Exec("ROLLBACK;"); throw; } }
    }
    public object Mutate(string scope, string? key, object request, Func<SqliteConnection, object> action)
    {
        if (key is not null) Rules.Required(key, 200);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Rules.Json))));
        return Write(c =>
        {
            if (key is not null)
            {
                var previous = c.Rows("SELECT * FROM mutations WHERE key=$key", ("key", key)).FirstOrDefault();
                if (previous is not null)
                {
                    if (previous.S("scope") != scope || previous.S("request_hash") != hash) throw new DomainException("Idempotency key was used for a different request.", 409);
                    return JsonSerializer.Deserialize<JsonElement>(previous.S("response_json"));
                }
            }
            var response = action(c);
            if (key is not null) c.Exec("INSERT INTO mutations VALUES($key,$scope,$hash,$response)", ("key", key), ("scope", scope), ("hash", hash), ("response", JsonSerializer.Serialize(response, Rules.Json)));
            Audit(c, scope, scope.StartsWith("agent:") ? "agent" : "human", scope, null, response);
            return response;
        });
    }
    public void BackupTo(string destination)
    {
        lock (Gate) { using var source = Open(); using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()); target.Open(); source.BackupDatabase(target); }
    }
    public void Audit(SqliteConnection c, string id, string actor, string action, object? before, object after, string? reason = null) => c.Exec(
        "INSERT INTO audit(entity_id,actor,action,previous_json,new_json,reason,created_utc) VALUES($id,$actor,$action,$before,$after,$reason,$now)",
        ("id", id), ("actor", actor), ("action", action), ("before", before is null ? null : JsonSerializer.Serialize(before, Rules.Json)), ("after", JsonSerializer.Serialize(after, Rules.Json)), ("reason", reason), ("now", DateTimeOffset.UtcNow.ToString("O")));
}
public static class Sql
{
    public static SqliteCommand Command(this SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    { var cmd = c.CreateCommand(); cmd.CommandText = sql; foreach (var (name, value) in args) cmd.Parameters.AddWithValue("$" + name, value ?? DBNull.Value); return cmd; }
    public static int Exec(this SqliteConnection c, string sql, params (string Name, object? Value)[] args) { using var cmd = c.Command(sql, args); return cmd.ExecuteNonQuery(); }
    public static object? Scalar(this SqliteConnection c, string sql, params (string Name, object? Value)[] args) { using var cmd = c.Command(sql, args); var value = cmd.ExecuteScalar(); return value is DBNull ? null : value; }
    public static List<Dictionary<string, object?>> Rows(this SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.Command(sql, args); using var reader = cmd.ExecuteReader(); var rows = new List<Dictionary<string, object?>>();
        while (reader.Read()) { var row = new Dictionary<string, object?>(); for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i); rows.Add(row); }
        return rows;
    }
    public static Dictionary<string, object?> One(this SqliteConnection c, string sql, params (string Name, object? Value)[] args) => c.Rows(sql, args).FirstOrDefault() ?? throw new DomainException("Record not found.", 404);
    public static string S(this Dictionary<string, object?> r, string key) => Convert.ToString(r.GetValueOrDefault(key)) ?? "";
    public static long L(this Dictionary<string, object?> r, string key) => Convert.ToInt64(r.GetValueOrDefault(key));
}

