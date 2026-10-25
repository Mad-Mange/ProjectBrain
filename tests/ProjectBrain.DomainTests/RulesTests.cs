using ProjectBrain.Domain;
namespace ProjectBrain.DomainTests;

public sealed class RulesTests
{
    [Theory]
    [InlineData("approved", "passed", false, true, true)]
    [InlineData("draft", "passed", false, true, false)]
    [InlineData("proposed", "passed", false, true, false)]
    [InlineData("approved", "untested", false, true, false)]
    [InlineData("approved", "passed", true, true, false)]
    [InlineData("approved", "passed", false, false, false)]
    [InlineData("approved", "stale", false, true, false)]
    [InlineData("approved", "inconclusive", true, true, false)]
    public void VerificationRequiresAllIndependentConditions(string review, string validation, bool stale, bool proof, bool expected) => Assert.Equal(expected, Rules.Verified(review, validation, stale, proof));

    [Fact] public void VisibleHiddenSourceMetadataIsNotGloballyExcluded() => Assert.False(SourceSafety.SkipDirectory(".github"));
    [Theory]
    [InlineData(".env")]
    [InlineData(".env.production")]
    [InlineData("id_rsa")]
    [InlineData("credentials.json")]
    [InlineData("secrets.json")]
    [InlineData("certificate.pem")]
    [InlineData("key.pfx")]
    [InlineData(".npmrc")]
    public void SecretFilesAreNeverEligible(string name) => Assert.False(SourceSafety.Eligible(name));
    [Theory]
    [InlineData("node_modules")]
    [InlineData(".git")]
    [InlineData("Library")]
    [InlineData("DerivedDataCache")]
    [InlineData(".ssh")]
    [InlineData(".local")]
    [InlineData("test-results")]
    [InlineData("playwright-report")]
    [InlineData("site-packages")]
    [InlineData("trivy-cache")]
    [InlineData(".trivy")]
    public void CacheAndCredentialDirectoriesExcluded(string name) => Assert.True(SourceSafety.SkipDirectory(name));
    [Theory]
    [InlineData("model.blend")]
    [InlineData("Level.umap")]
    [InlineData("asset.uasset")]
    [InlineData("weights.onnx")]
    public void OpaqueAssetsAreMetadataOnly(string name) { Assert.True(SourceSafety.Eligible(name)); Assert.False(SourceSafety.IsText(name)); }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:password@localhost")]
    [InlineData("ftp://example.com")]
    public void UnsafeUrlRejected(string url) => Assert.Throws<DomainException>(() => Rules.Url(url));
    [Fact] public void HttpUrlIsStoredWithoutFetching() => Assert.Equal("http://127.0.0.1:1234/private", Rules.Url("http://127.0.0.1:1234/private"));
    [Fact] public void RawWhitespacePreserved() => Assert.Equal("  note\n\tcode  ", Rules.Required("  note\n\tcode  "));
    [Fact] public void UnicodeBudgetUsesUtf8Bytes() => Assert.Throws<DomainException>(() => Rules.Required(new string('界', 22000)));
    [Fact] public void EmptyCaptureRejected() => Assert.Throws<DomainException>(() => Rules.Required("  \n"));
    [Fact] public void VersionConflictReturns409() => Assert.Equal(409, Assert.Throws<DomainException>(() => Rules.Version(2, 1)).Status);
    [Fact] public void KindIsNotValidation() => Assert.Throws<DomainException>(() => Rules.Choice("verified", Rules.Kinds));
    [Fact] public void TagsNormalizedAndDeduplicated() => Assert.Equal(new[] { "unity", "csharp" }, Rules.Tags([" Unity ", "unity", "csharp"]));
    [Fact] public void TooManyTagsRejected() => Assert.Throws<DomainException>(() => Rules.Tags(Enumerable.Range(0, 31).Select(i => "tag" + i).ToArray()));
    [Fact]
    public void SensitiveLinesRedacted()
    {
        var safe = Rules.SafeText("using Serilog;\npassword=FAKE_CANARY_DO_NOT_INDEX\nvar key = \"abcdefghijklmnopqrstuvwxyz1234567890\";");
        Assert.Contains("Serilog", safe); Assert.DoesNotContain("FAKE_CANARY", safe); Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", safe);
    }
    [Fact]
    public void DiagnosticPathsPreserveDirectoriesButRedactIndividualSecrets()
    {
        const string path = "ProjectBrain/src/ProjectBrain.Domain/SourceSafety.cs";
        Assert.Equal(path, SourceSafety.DisplayPath(path));
        var secret = new string('a', 40);
        Assert.Equal("App/[redacted token]/file.cs", SourceSafety.DisplayPath("App/" + secret + "/file.cs"));
        Assert.True(SourceSafety.DisplayPath(new string('x', 600)).Length <= 500);
    }
    [Fact]
    public void ContainmentRejectsSiblingPrefixAndTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "pb-root");
        Assert.Throws<DomainException>(() => SourceSafety.Contained(root, root + "-outside", false));
        Assert.Throws<DomainException>(() => SourceSafety.Contained(root, Path.Combine(root, "..", "outside"), false));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "inside")), SourceSafety.Contained(root, Path.Combine(root, "inside"), false));
    }
}

