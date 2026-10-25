using ProjectBrain.Domain;
using ProjectBrain.Infrastructure;
namespace ProjectBrain.DomainTests;

public sealed class ExtractorTests
{
    [Theory]
    [InlineData("unity", "ProjectSettings/ProjectVersion.txt")]
    [InlineData("unreal", "Demo.uproject")]
    [InlineData("dotnet", "Demo.csproj")]
    [InlineData("javascript", "package.json")]
    [InlineData("python", "main.py")]
    [InlineData("blender", "model.blend")]
    [InlineData("docker", "Dockerfile")]
    public void DetectsEveryRequiredStack(string expected, string marker) => Assert.Equal(expected, Extractor.Kind([marker]));
    [Fact] public void UnityTakesPrecedenceOverGeneratedDotnet() => Assert.Equal("unity", Extractor.Kind(["App.csproj", "ProjectSettings/ProjectVersion.txt"]));
    [Theory]
    [InlineData("dotnet", "App.slnx")]
    [InlineData("javascript", "package.json")]
    [InlineData("python", "pyproject.toml")]
    [InlineData("docker", "compose.yaml")]
    public void RootManifestsOutrankNestedEngineFixtures(string expected, string rootManifest) => Assert.Equal(expected,
        Extractor.Kind([rootManifest, "tests/fixtures/Unity/ProjectSettings/ProjectVersion.txt", "tests/fixtures/Unreal/Demo.uproject"]));
    [Fact]
    public void PackageScriptsAreNamesOnly()
    {
        var output = Extractor.Extract("package.json", "{\"dependencies\":{\"react\":\"19.2.0\"},\"scripts\":{\"postinstall\":\"malicious-command\"}}");
        Assert.Contains("react", output); Assert.Contains("postinstall", output); Assert.DoesNotContain("malicious-command", output);
    }
    [Fact] public void DotnetMetadataRejectsExternalEntities() => Assert.Contains("Malformed", Extractor.Extract("app.csproj", "<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><Project>&x;</Project>"));
    [Fact] public void EnvironmentExampleRetainsNamesOnly() => Assert.Equal("Environment variable names: DEMO_SETTING", Extractor.Extract(".env.example", "DEMO_SETTING=do-not-store-this"));
    [Fact]
    public void ExtractsBoundedAndSecretRedacted()
    {
        var output = Extractor.Extract("README.md", "password=DO_NOT_STORE\n" + new string('x', 20000));
        Assert.DoesNotContain("DO_NOT_STORE", output); Assert.True(output.Length <= 12000);
    }
    [Fact]
    public void DockerEnvironmentValuesNeverPersist()
    {
        var text = "services:\n  worker:\n    environment:\n      - DATABASE_URL=postgresql://user:pw@localhost/db\n      - NORMAL_SETTING=private-value";
        var output = Extractor.Extract("compose.yaml", text); Assert.Contains("worker", output); Assert.Contains("DATABASE_URL", output); Assert.DoesNotContain("private-value", output); Assert.DoesNotContain("user:pw", output);
    }
    [Fact] public void CredentialUrlsAreRedacted() => Assert.DoesNotContain("user:pw", Rules.SafeText("endpoint = http://user:pw@localhost/path"));
}

