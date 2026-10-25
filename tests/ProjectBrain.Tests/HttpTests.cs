using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ProjectBrain.Domain;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace ProjectBrain.Tests;

public sealed class HttpTests : IDisposable
{
    private readonly TestWorkspace workspace = new();
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    private const string HumanToken = "synthetic-human-test-token-1234567890";
    private const string AgentToken = "synthetic-agent-test-token-0987654321";
    public HttpTests()
    {
        Environment.SetEnvironmentVariable("PROJECTBRAIN_AUTH_TOKEN", HumanToken);
        Environment.SetEnvironmentVariable("PROJECTBRAIN_AGENT_TOKEN", AgentToken);
        Environment.SetEnvironmentVariable("PROJECTBRAIN_DB", workspace.Db.Path);
        Environment.SetEnvironmentVariable("PROJECTBRAIN_SOURCES", workspace.Sources);
        Environment.SetEnvironmentVariable("PROJECTBRAIN_WEB_ORIGIN", "http://127.0.0.1:14318");
        factory = new WebApplicationFactory<Program>(); client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
    }
    public void Dispose() { client.Dispose(); factory.Dispose(); workspace.Dispose(); Environment.SetEnvironmentVariable("PROJECTBRAIN_WEB_ORIGIN", null); }
    [Theory]
    [InlineData("/api/v1/dashboard")]
    [InlineData("/api/v1/knowledge/search")]
    [InlineData("/mcp")]
    public async Task ProtectedRoutesRejectMissingToken(string route) => Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
    [Fact]
    public async Task HealthPublicButDnsRebindingAndOriginRejected()
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        client.DefaultRequestHeaders.Host = "evil.example"; Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/health/live")).StatusCode);
        client.DefaultRequestHeaders.Host = "localhost"; client.DefaultRequestHeaders.Add("Origin", "https://evil.example"); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/health/live")).StatusCode);
    }
    [Theory]
    [InlineData("http://127.0.0.1:14318", HttpStatusCode.OK)]
    [InlineData("http://127.0.0.1:14319", HttpStatusCode.Forbidden)]
    [InlineData("http://127.0.0.1:14318.evil.example", HttpStatusCode.Forbidden)]
    [InlineData("null", HttpStatusCode.Forbidden)]
    public async Task ConfiguredBrowserOriginRequiresExactMatch(string origin, HttpStatusCode expected)
    {
        client.DefaultRequestHeaders.Add("Origin", origin);
        Assert.Equal(expected, (await client.GetAsync("/health/live")).StatusCode);
    }
    [Fact]
    public async Task AgentCredentialCannotApproveHumanAction()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AgentToken);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.PostAsJsonAsync("/api/v1/knowledge/1/review", new { contentVersion = 1, reviewStatus = "approved", reason = "self approval" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact]
    public async Task CaptureRoundtripAndUnknownVerificationFieldRejected()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", HumanToken);
        var response = await client.PostAsJsonAsync("/api/v1/captures", new CaptureRequest("  raw note  ", IntentFlag: "try_next")); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("knowledgeId").GetInt64();
        var item = await client.GetFromJsonAsync<JsonElement>("/api/v1/knowledge/" + saved);
        Assert.Equal("try_next", item.GetProperty("intentFlag").GetString()); Assert.False(item.GetProperty("isVerified").GetBoolean());
        response = await client.PostAsJsonAsync("/api/v1/captures", new { originalText = "untrusted", isVerified = true }); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task RealStreamableHttpMcpInitializeListSearchAndProposal()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AgentToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        async Task<string> Rpc(object message)
        {
            var response = await client.PostAsJsonAsync("/mcp", message);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadAsStringAsync();
        }
        var init = await Rpc(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "projectbrain-test-agent", version = "1.0" } } }); Assert.Contains("serverInfo", init);
        client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
        var tools = await Rpc(new { jsonrpc = "2.0", id = 2, method = "tools/list" }); Assert.Contains("search_knowledge", tools); Assert.Contains("propose_experiment_result", tools); Assert.DoesNotContain("approve_knowledge", tools);
        var toolJson = tools.StartsWith("data:", StringComparison.Ordinal) || tools.Contains("\ndata:", StringComparison.Ordinal)
            ? tools.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim() : tools;
        using var definitions = JsonDocument.Parse(toolJson);
        foreach (var tool in definitions.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray())
        {
            var annotations = tool.GetProperty("annotations");
            Assert.Equal(!tool.GetProperty("name").GetString()!.StartsWith("propose_", StringComparison.Ordinal), annotations.GetProperty("readOnlyHint").GetBoolean());
            Assert.False(annotations.GetProperty("destructiveHint").GetBoolean());
            Assert.False(annotations.GetProperty("openWorldHint").GetBoolean());
            Assert.True(annotations.GetProperty("idempotentHint").GetBoolean());
        }
        var proposed = await Rpc(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "propose_knowledge", arguments = new { kind = "lesson_learned", title = "Synthetic MCP lesson", summary = "A reusable observation", body = "Synthetic evidence still needs review", clientProposalId = "mcp-http-test" } } }); Assert.Contains("proposed", proposed);
        var searched = await Rpc(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "search_knowledge", arguments = new { query = "Synthetic", statusFilter = "all" } } }); Assert.Contains("Synthetic MCP lesson", searched);
    }
}

