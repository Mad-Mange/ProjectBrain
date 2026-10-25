namespace ProjectBrain.Domain;

public static class SourceSafety
{
    private static readonly HashSet<string> Excluded = new(".git .svn .hg .ssh .aws .azure .codex .agents .claude .copilot .npm .yarn .pnpm-store node_modules .venv venv __pycache__ .pytest_cache .mypy_cache .tox bin obj .vs .idea .next dist build coverage target .gradle Library Temp Logs UserSettings DerivedDataCache Intermediate Binaries Saved shader-cache Cache caches .cache .terraform .nuget".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> TextExtensions = new(".cs .fs .csproj .fsproj .sln .slnx .py .toml .txt .md .json .js .jsx .ts .tsx .cpp .cc .h .hpp .uproject .uplugin .ini .yaml .yml .xml .props .targets .unity .prefab .shader .sh .ps1 .sql .css .html .log".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MetadataExtensions = new(".blend .blend1 .fbx .obj .gltf .uasset .umap .png .jpg .wav .mp4 .pt .pth .onnx .safetensors".Split(' '), StringComparer.OrdinalIgnoreCase);
    public static bool SkipDirectory(string name) => Excluded.Contains(name) || name.Equals("site-packages", StringComparison.OrdinalIgnoreCase)
        || name.Equals("trivy-cache", StringComparison.OrdinalIgnoreCase) || name.Equals(".trivy", StringComparison.OrdinalIgnoreCase)
        || name.Equals(".local", StringComparison.OrdinalIgnoreCase)
        || name.Equals("test-results", StringComparison.OrdinalIgnoreCase) || name.Equals("playwright-report", StringComparison.OrdinalIgnoreCase);
    public static bool ValidOverride(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 500 && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(p => p is not ("" or "." or "..") && (!SkipDirectory(p) || SoftExcluded(p)))
        && SoftExcluded(path.Split('/').Last());
    private static bool SoftExcluded(string name) => name.Equals("build", StringComparison.OrdinalIgnoreCase) || name.Equals("Library", StringComparison.OrdinalIgnoreCase) || name.Equals("Saved", StringComparison.OrdinalIgnoreCase);
    public static bool SkipDirectory(string name, string relativePath, string[]? overrides) => SkipDirectory(name)
        && !(SoftExcluded(name) && (overrides ?? []).Contains(relativePath, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
    public static bool SecretFile(string name)
    {
        var n = name.ToLowerInvariant();
        return (n.StartsWith(".env") && n != ".env.example") || n.StartsWith("credentials") || n.StartsWith("id_rsa") || n is ".npmrc" or "secrets.json" || n.EndsWith(".pem") || n.EndsWith(".pfx") || n.EndsWith(".key");
    }
    public static bool IsText(string path) => TextExtensions.Contains(Path.GetExtension(path)) || Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path) == ".env.example";
    public static bool Eligible(string path) => !SecretFile(Path.GetFileName(path)) && (IsText(path) || MetadataExtensions.Contains(Path.GetExtension(path)));
    public static bool Critical(string path)
    {
        var n = Path.GetFileName(path).ToLowerInvariant();
        return n.StartsWith("readme") || n.StartsWith("dockerfile") || n is "package.json" or "pyproject.toml" or "manifest.json" || Path.GetExtension(n) is ".csproj" or ".fsproj" or ".uproject" or ".uplugin" or ".sln" or ".slnx";
    }
    public static string Contained(string mount, string candidate, bool mustExist = true)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mount));
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.Equals(root, comparison) && !path.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new DomainException("Choose a path inside the configured source mount.");
        var current = path;
        var candidateExists = false;
        while (current is not null)
        {
            var attributes = Attributes(current);
            if (current == path) candidateExists = attributes is not null;
            if (attributes is not null)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new DomainException("Symbolic links and junctions are not permitted.");
            }
            if (current.Equals(root, comparison)) break;
            current = Path.GetDirectoryName(current);
        }
        // Also reject a linked ancestor of the approved mount.
        current = Path.GetDirectoryName(root);
        while (current is not null) { if (Attributes(current) is { } attributes && (attributes & FileAttributes.ReparsePoint) != 0) throw new DomainException("Mount ancestor is a link."); current = Path.GetDirectoryName(current); }
        if (mustExist && !candidateExists) throw new DomainException("Source path is unavailable.", 503);
        return path;
    }
    // One metadata lookup per segment. Do not cache across calls: links can change during a scan.
    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    public static string DisplayPath(string relativePath) => Rules.Clip(string.Join('/', relativePath.Split('/').Select(Rules.SafeText)), 500);
}

