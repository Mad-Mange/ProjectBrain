using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ProjectBrain.Domain;

public sealed class DomainException(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }
public static partial class Rules
{
    public static readonly string[] Kinds = ["idea", "best_practice", "lesson_learned", "reusable_solution", "project_profile"];
    public static readonly string[] Reviews = ["draft", "proposed", "approved", "rejected", "deprecated"];
    public static readonly string[] Intents = ["none", "explore", "try_next", "reuse_when_relevant"];
    public static readonly string[] Validations = ["untested", "planned", "running", "passed", "failed", "inconclusive", "stale"];
    public static readonly string[] Outcomes = ["passed", "failed", "inconclusive"];
    public static string Choice(string value, string[] allowed) => allowed.Contains(value) ? value : throw new DomainException("Invalid enum value.");
    public static string Required(string? value, int max = 65536)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > max) throw new DomainException($"Text must contain 1–{max} UTF-8 bytes.");
        return value;
    }
    public static void Version(long actual, long expected) { if (actual != expected) throw new DomainException("This record changed. Refresh before saving.", 409); }
    public static string[] Tags(string[]? tags)
    {
        if (tags?.Length > 30 || tags?.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 60) == true) throw new DomainException("Use at most 30 tags of 1–60 characters.");
        return (tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Distinct().ToArray();
    }
    public static string? Url(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) throw new DomainException("Source URL must be an HTTP(S) reference without credentials.");
        return url;
    }
    public static bool Verified(string review, string validation, bool needsReview, bool currentProof) => review == "approved" && validation == "passed" && !needsReview && currentProof;
    public static string SafeText(string text) => string.Join('\n', text.Split('\n').Select(line => Secret().IsMatch(line) ? "[redacted sensitive line]" : Token().Replace(line, "[redacted token]")));
    [GeneratedRegex(@"(?i)(password|passwd|secret|api[_-]?key|access[_-]?key|authorization|bearer\s|private.key|connection.?string|credential|token\s*[:=]|[a-z][a-z0-9+.-]*://[^\s/@:]+:[^\s/@]+@)")] private static partial Regex Secret();
    [GeneratedRegex(@"[A-Za-z0-9_+/=-]{32,}")] private static partial Regex Token();
    public static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
    public static string NewId() => Guid.NewGuid().ToString("N");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
public record Evidence(string ProjectId, string RelativePath, string? FileSha256 = null, string? Note = null);
public record CaptureRequest(string OriginalText, string? Title = null, string Kind = "idea", string? SourceUrl = null, string[]? Tags = null, string? ProjectId = null, string IntentFlag = "none");
public record ProposalRequest(string Kind, string Title, string Summary, string Body, string ClientProposalId, string[]? Tags = null, Evidence[]? Evidence = null, string[]? ProjectIds = null);
public record EditRequest(long ContentVersion, string Title, string Kind, string Summary, string Body, string[]? Tags = null, string? ProjectId = null);
public record DecisionRequest(long ContentVersion, string ReviewStatus, string Reason);
public record IntentRequest(long ContentVersion, string IntentFlag);
public record PlanRequest(long ContentVersion, string Hypothesis, string SuccessCriteria, string Procedure = "", string? ProjectId = null, string Environment = "", string Limitations = "");
public record ResultRequest(string ProposedOutcome, string Observations, Evidence[] Evidence, string ClientProposalId);
public record ResultReviewRequest(long ContentVersion, string ProposalId, string Decision, string Reason, bool EvidenceChecked);
public record SearchRequest(string Query = "", string? Kind = null, string? ReviewStatus = null, string? ValidationStatus = null, string? IntentFlag = null, string? Tag = null, string? ProjectId = null, bool? NeedsReview = null, int Limit = 20, int Page = 1, string[]? Kinds = null, string[]? Technologies = null, bool IncludeIdeas = true);
public record ScanSettings(double IntervalHours = 6, int AuditDays = 7, bool Paused = false, int MaxFiles = 300000, int MaxDepth = 20, long MaxBytes = 2147483648, int MaxTextBytes = 1048576, int MaxMinutes = 120, int? QuietStartHourUtc = null, int? QuietEndHourUtc = null, long ContentVersion = 1, string[]? IncludeDirectories = null);


public record ResolveValidationRequest(long ContentVersion, string ExperimentId, string Reason, bool EvidenceChecked);
