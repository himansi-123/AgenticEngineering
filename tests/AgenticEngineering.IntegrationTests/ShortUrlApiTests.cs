using System.Net;
using System.Net.Http.Json;
using AgenticEngineering.Domain.Workflows;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgenticEngineering.IntegrationTests;

public sealed class ShortUrlApiTests : IClassFixture<ShortUrlApiFactory>
{
    private readonly HttpClient _client;

    public ShortUrlApiTests(ShortUrlApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Create_redirect_and_analytics_are_persisted()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/urls", new { url = "https://example.com/path" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        var metadata = await _client.GetFromJsonAsync<ShortUrlResponse>($"/api/urls/{created.Code}");
        Assert.Equal("https://example.com/path", metadata?.OriginalUrl);
        Assert.Equal(0, metadata?.ClickCount);

        var redirect = await _client.GetAsync($"/{created.Code}");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/path", redirect.Headers.Location?.ToString());

        var analytics = await _client.GetFromJsonAsync<AnalyticsResponse>($"/api/urls/{created.Code}/analytics");
        Assert.Equal(1, analytics?.TotalClicks);
        Assert.Single(analytics!.ClickTimestampsUtc);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com")]
    public async Task Create_rejects_unsafe_destinations(string url)
    {
        var response = await _client.PostAsJsonAsync("/api/urls", new { url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_short_code_returns_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/missing-code")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/urls/missing-code")).StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Workflow_execution_persists_graph_outputs_and_audit_history()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", new
        {
            requirement = "Make links expire automatically."
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var runDocument = System.Text.Json.JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal((int)WorkflowRunState.Completed, runDocument.RootElement.GetProperty("state").GetInt32());

        var tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        Assert.NotNull(tasks);
        Assert.Equal(10, tasks.Length);
        Assert.All(tasks, task => Assert.Equal((int)WorkflowTaskState.Succeeded, task.GetProperty("state").GetInt32()));
        Assert.Contains(tasks, task => task.GetProperty("dependencies").GetArrayLength() > 0);

        var audit = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.NotNull(audit);
        Assert.Contains(audit, item => item.GetProperty("eventType").GetString() == "workflow.state_changed");
        Assert.Contains(audit, item => item.GetProperty("eventType").GetString() == "task.succeeded");

        var metrics = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/workflows/{runId}/metrics");
        Assert.Equal(1, metrics.GetProperty("workflowSuccessRate").GetDouble());
        Assert.Equal(0, metrics.GetProperty("taskFailureRate").GetDouble());
    }

    [Fact]
    public async Task Approval_decision_is_persisted_and_audited()
    {
        var createRunResponse = await _client.PostAsJsonAsync("/api/workflows", new
        {
            requirement = "Add configurable expiration to shortened URLs."
        });
        Assert.Equal(HttpStatusCode.Created, createRunResponse.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await createRunResponse.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();

        var requestResponse = await _client.PostAsJsonAsync("/api/approvals", new
        {
            runId,
            action = 1,
            description = "Review a generated-code change"
        });
        Assert.Equal(HttpStatusCode.Created, requestResponse.StatusCode);
        using var approvalDocument = System.Text.Json.JsonDocument.Parse(await requestResponse.Content.ReadAsStringAsync());
        var approvalId = approvalDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(0, approvalDocument.RootElement.GetProperty("state").GetInt32());

        var rejectResponse = await _client.PostAsJsonAsync($"/api/approvals/{approvalId}/reject", new
        {
            reviewer = "test-reviewer",
            notes = "Do not apply this change."
        });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        using var rejectedDocument = System.Text.Json.JsonDocument.Parse(await rejectResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, rejectedDocument.RootElement.GetProperty("state").GetInt32());

        var approvals = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/approvals");
        Assert.Contains(approvals!, item => item.GetProperty("id").GetGuid() == approvalId);
        var audit = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.Contains(audit!, item => item.GetProperty("eventType").GetString() == "approval.decided");
    }

    [Fact]
    public async Task Brownfield_scenario_recovers_from_controlled_failure_and_keeps_audit_history()
    {
        var response = await _client.PostAsync("/api/scenarios/brownfield-analytics-defect/run", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal((int)WorkflowRunState.Completed, runDocument.RootElement.GetProperty("state").GetInt32());

        var tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        var validationTask = Assert.Single(tasks!, task =>
            task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.TestingValidation);
        Assert.Equal((int)WorkflowTaskState.Succeeded, validationTask.GetProperty("state").GetInt32());
        Assert.Equal(2, validationTask.GetProperty("attemptCount").GetInt32());

        var audit = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.Contains(audit!, item => item.GetProperty("eventType").GetString() == "task.failed");
        Assert.Contains(audit!, item => item.GetProperty("eventType").GetString() == "task.retry_scheduled");

        var metrics = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/workflows/{runId}/metrics");
        Assert.Equal(1, metrics.GetProperty("retryCount").GetInt32());
    }

    [Theory]
    [InlineData("greenfield-expiration")]
    [InlineData("ambiguous-link-expiration")]
    public async Task Greenfield_and_ambiguous_scenarios_are_runnable(string scenarioId)
    {
        var response = await _client.PostAsync($"/api/scenarios/{scenarioId}/run", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)WorkflowRunState.Completed, runDocument.RootElement.GetProperty("state").GetInt32());
    }

    private sealed record ShortUrlResponse(string Code, string OriginalUrl, long ClickCount);

    private sealed record AnalyticsResponse(string Code, long TotalClicks, IReadOnlyList<DateTimeOffset> ClickTimestampsUtc);
}

public sealed class ShortUrlApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"agentic-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ShortUrls"] = $"Data Source={_databasePath}"
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}