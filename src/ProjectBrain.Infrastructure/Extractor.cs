using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using ProjectBrain.Domain;
namespace ProjectBrain.Infrastructure;

public static class Extractor
{
    public static string Extract(string path, string text)
    {
        var name = Path.GetFileName(path).ToLowerInvariant(); var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (name == ".env.example") return "Environment variable names: " + string.Join(", ", text.Split('\n').Where(l => l.Contains('=')).Select(l => l.Split('=')[0].Trim()).Where(n => Regex.IsMatch(n, @"^[A-Z_][A-Z0-9_]*$")));
            if (name.StartsWith("dockerfile") || name is "compose.yaml" or "compose.yml" or "docker-compose.yaml" or "docker-compose.yml")
            {
                var lines = new List<string>();
                foreach (var line in text.Split('\n'))
                {
                    var service = Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
                    var variable = Regex.Match(line, @"^\s*(?:-\s*)?([A-Z_][A-Z0-9_]*)\s*[:=]");
                    if (service.Success) lines.Add("Declared service/volume: " + service.Groups[1].Value);
                    if (variable.Success) lines.Add("Environment name: " + variable.Groups[1].Value);
                    if (line.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase)) lines.Add(Rules.SafeText(line.Trim()));
                }
                return string.Join("\n", lines);
            }
            if (ext is ".csproj" or ".fsproj")
            {
                using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var doc = XDocument.Load(reader);
                return Rules.SafeText(string.Join("\n", doc.Descendants().Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks" or "PackageReference" or "ProjectReference").Select(e => e.Name.LocalName + ": " + (e.Attribute("Include")?.Value ?? e.Value) + " " + e.Attribute("Version")?.Value)));
            }
            if (name is "package.json" or "manifest.json" || ext is ".uproject" or ".uplugin")
            {
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
                var lines = new List<string>();
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Name is "dependencies" or "devDependencies") { if (p.Value.ValueKind == JsonValueKind.Object) foreach (var d in p.Value.EnumerateObject()) lines.Add("Dependency " + d.Name + " " + Rules.Clip(d.Value.ToString(), 80)); }
                    else if (p.Name is "EngineAssociation" or "FileVersion" or "name") lines.Add(p.Name + ": " + Rules.Clip(p.Value.ToString(), 100));
                    else if (p.Name is "Modules" or "Plugins") lines.Add(p.Name + ": " + Rules.Clip(p.Value.ToString(), 2000));
                    else if (p.Name == "scripts" && p.Value.ValueKind == JsonValueKind.Object) lines.Add("Script names (not executed): " + string.Join(", ", p.Value.EnumerateObject().Select(v => v.Name)));
                }
                return Rules.SafeText(string.Join("\n", lines));
            }
            return Rules.Clip(Rules.SafeText(text), 12000);
        }
        catch (Exception ex) when (ex is JsonException or XmlException or InvalidOperationException) { return "[Malformed metadata; no semantic inference]"; }
    }
    public static string Kind(IEnumerable<string> paths)
    {
        var files = paths.ToArray();
        // Root manifests describe the owning project; nested examples/fixtures must not override it.
        var rootFiles = files.Where(p => !p.Contains('/')).ToArray();
        if (files.Contains("ProjectSettings/ProjectVersion.txt", StringComparer.OrdinalIgnoreCase)) return "unity";
        if (rootFiles.Any(p => p.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))) return "unreal";
        if (rootFiles.Any(p => Path.GetExtension(p).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj" or ".fsproj")) return "dotnet";
        if (rootFiles.Contains("package.json", StringComparer.OrdinalIgnoreCase)) return "javascript";
        if (rootFiles.Contains("pyproject.toml", StringComparer.OrdinalIgnoreCase)) return "python";
        if (rootFiles.Any(p => p.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase) || p.ToLowerInvariant() is "compose.yaml" or "compose.yml" or "docker-compose.yaml" or "docker-compose.yml")) return "docker";
        if (files.Any(p => p.EndsWith("ProjectSettings/ProjectVersion.txt", StringComparison.OrdinalIgnoreCase))) return "unity";
        if (files.Any(p => p.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase))) return "unreal";
        if (files.Any(p => Path.GetExtension(p) is ".sln" or ".slnx" or ".csproj" or ".fsproj")) return "dotnet";
        if (files.Any(p => Path.GetFileName(p) == "package.json")) return "javascript";
        if (files.Any(p => Path.GetExtension(p) == ".py" || Path.GetFileName(p) == "pyproject.toml")) return "python";
        if (files.Any(p => Path.GetExtension(p) is ".blend" or ".blend1")) return "blender";
        if (files.Any(p => Path.GetFileName(p).StartsWith("Dockerfile") || Path.GetFileName(p) is "compose.yaml" or "compose.yml" or "docker-compose.yml")) return "docker";
        return "generic";
    }
}

