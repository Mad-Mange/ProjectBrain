using System.Text.RegularExpressions;
using ProjectBrain.Domain;
namespace ProjectBrain.Infrastructure;

// Narrow metadata reads only: no Git process, hooks, objects, remotes, or worktree redirects.
public static partial class GitMetadata
{
    public static string? ReadHead(string project, Action<long> consume)
    {
        var directory = Path.Combine(project, ".git");
        if (!Directory.Exists(directory)) return null;
        SourceSafety.Contained(project, directory);
        string? Read(string relative, int cap)
        {
            var path = SourceSafety.Contained(directory, Path.Combine(directory, relative), false);
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > cap) return null;
            consume(stream.Length);
            var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
            SourceSafety.Contained(directory, path);
            return System.Text.Encoding.UTF8.GetString(bytes).Trim();
        }
        var head = Read("HEAD", 4096);
        if (head is null) return null;
        if (Commit().IsMatch(head)) return head.ToUpperInvariant();
        if (!head.StartsWith("ref: refs/", StringComparison.Ordinal)) return null;
        var reference = head[5..];
        if (reference.Split('/').Any(p => p is "" or "." or "..") || reference.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.'))) return null;
        var loose = Read(reference, 4096);
        if (loose is not null && Commit().IsMatch(loose)) return loose.ToUpperInvariant();
        foreach (var line in (Read("packed-refs", 1048576) ?? "").Split('\n'))
        {
            var parts = line.Trim().Split(' ', 2);
            if (parts.Length == 2 && parts[1] == reference && Commit().IsMatch(parts[0])) return parts[0].ToUpperInvariant();
        }
        return null;
    }
    [GeneratedRegex("\\A(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\\z")]
    private static partial Regex Commit();
}
