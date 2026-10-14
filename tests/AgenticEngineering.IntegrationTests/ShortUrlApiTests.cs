using System.Net;
using System.Net.Http.Json;
using AgenticEngineering.Domain.Workflows;
using AgenticEngineering.Application.ShortUrls;
using AgenticEngineering.Application.Workflows;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        Assert.Null(created.ExpiresAtUtc);
        Assert.False(created.IsExpired);

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

    [Fact]
    public async Task Repeated_clicks_are_counted_and_timestamps_are_utc_and_ordered()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/urls", new { url = "https://example.com/repeated" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        var beforeClicks = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Redirect, (await _client.GetAsync($"/{created.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await _client.GetAsync($"/{created.Code}")).StatusCode);
        var afterClicks = DateTimeOffset.UtcNow;
        var analytics = await _client.GetFromJsonAsync<AnalyticsResponse>($"/api/urls/{created.Code}/analytics");

        Assert.NotNull(analytics);
        Assert.Equal(2, analytics.TotalClicks);
        Assert.Equal(2, analytics.ClickTimestampsUtc.Count);
        Assert.Equal(analytics.ClickTimestampsUtc.OrderBy(timestamp => timestamp), analytics.ClickTimestampsUtc);
        Assert.All(analytics.ClickTimestampsUtc, timestamp =>
        {
            Assert.Equal(TimeSpan.Zero, timestamp.Offset);
            Assert.InRange(timestamp, beforeClicks, afterClicks);
        });
    }

    [Fact]
    public async Task Concurrent_redirects_each_persist_one_analytics_event()
    {
        const int requestCount = 16;
        var createResponse = await _client.PostAsJsonAsync("/api/urls", new { url = "https://example.com/concurrent" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        var responses = await Task.WhenAll(Enumerable.Range(0, requestCount)
            .Select(_ => _client.GetAsync($"/{created.Code}")));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Redirect, response.StatusCode));

        var analytics = await _client.GetFromJsonAsync<AnalyticsResponse>($"/api/urls/{created.Code}/analytics");
        Assert.NotNull(analytics);
        Assert.Equal(requestCount, analytics.TotalClicks);
        Assert.Equal(requestCount, analytics.ClickTimestampsUtc.Count);
    }

    [Fact]
    public async Task Expired_url_returns_gone_and_does_not_record_a_click()
    {
        var expiration = DateTimeOffset.UtcNow.AddSeconds(2);
        var createResponse = await _client.PostAsJsonAsync("/api/urls", new
        {
            url = "https://example.com/temporary",
            expiresAtUtc = expiration
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ExpiredUrlResponse>();
        Assert.NotNull(created);
        Assert.Equal(expiration, created.ExpiresAtUtc);

        Assert.Equal(HttpStatusCode.Redirect, (await _client.GetAsync($"/{created.Code}")).StatusCode);
        var activeAnalytics = await _client.GetFromJsonAsync<AnalyticsResponse>($"/api/urls/{created.Code}/analytics");
        Assert.Equal(1, activeAnalytics?.TotalClicks);

        await Task.Delay(expiration - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(50));
        var redirect = await _client.GetAsync($"/{created.Code}");

        Assert.Equal(HttpStatusCode.Gone, redirect.StatusCode);
        var metadata = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/urls/{created.Code}");
        Assert.True(metadata.GetProperty("isExpired").GetBoolean());
        var analytics = await _client.GetFromJsonAsync<AnalyticsResponse>($"/api/urls/{created.Code}/analytics");
        Assert.Equal(1, analytics?.TotalClicks);
    }

    [Fact]
    public async Task Create_rejects_expiration_that_is_not_later_than_creation()
    {
        var response = await _client.PostAsJsonAsync("/api/urls", new
        {
            url = "https://example.com/expired-on-arrival",
            expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unsupported_implementation_does_not_complete_as_a_successful_plan()
    {
        var response = await _client.PostAsJsonAsync("/api/workflows", new
        {
            requirement = "Add a project-wide Redis cache."
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal((int)WorkflowRunState.Failed, runDocument.RootElement.GetProperty("state").GetInt32());
        Assert.Null(runDocument.RootElement.GetProperty("summary").GetString());

        var tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        Assert.NotNull(tasks);
        Assert.Single(tasks!, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Implementation
            && task.GetProperty("state").GetInt32() == (int)WorkflowTaskState.Failed);
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
        Assert.Equal((int)WorkflowRunState.AwaitingClarification, runDocument.RootElement.GetProperty("state").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, runDocument.RootElement.GetProperty("completedAtUtc").ValueKind);

        var tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        Assert.NotNull(tasks);
        Assert.Equal(10, tasks.Length);
        Assert.Single(tasks!, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Clarification
            && task.GetProperty("state").GetInt32() == (int)WorkflowTaskState.AwaitingClarification);
        Assert.Single(tasks!, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Implementation
            && task.GetProperty("state").GetInt32() == (int)WorkflowTaskState.Pending);
        Assert.Contains(tasks, task => task.GetProperty("dependencies").GetArrayLength() > 0);

        var audit = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.NotNull(audit);
        Assert.Contains(audit, item => item.GetProperty("eventType").GetString() == "workflow.state_changed");
        Assert.Contains(audit, item => item.GetProperty("eventType").GetString() == "task.succeeded");
        Assert.Contains(audit, item => item.GetProperty("eventType").GetString() == "workflow.clarification_requested");

        var metrics = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/workflows/{runId}/metrics");
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
        var persistedRejection = Assert.Single(approvals!, item => item.GetProperty("id").GetGuid() == approvalId);
        Assert.Equal(2, persistedRejection.GetProperty("state").GetInt32());

        var secondRequest = await _client.PostAsJsonAsync("/api/approvals", new
        {
            runId,
            action = 1,
            description = "Approve a generated-code change"
        });
        Assert.Equal(HttpStatusCode.Created, secondRequest.StatusCode);
        using var secondApprovalDocument = System.Text.Json.JsonDocument.Parse(await secondRequest.Content.ReadAsStringAsync());
        var approvedId = secondApprovalDocument.RootElement.GetProperty("id").GetGuid();
        var approveResponse = await _client.PostAsJsonAsync($"/api/approvals/{approvedId}/approve", new
        {
            reviewer = "test-reviewer",
            notes = "Approved after review."
        });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        approvals = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/approvals");
        var persistedApproval = Assert.Single(approvals!, item => item.GetProperty("id").GetGuid() == approvedId);
        Assert.Equal(1, persistedApproval.GetProperty("state").GetInt32());
        var audit = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.Equal(2, audit!.Count(item => item.GetProperty("eventType").GetString() == "approval.decided"));
    }

    [Fact]
    public async Task Brownfield_scenario_verifies_persisted_analytics_without_claiming_a_defect()
    {
        var response = await _client.PostAsync("/api/scenarios/brownfield-analytics-defect/run", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();

        var tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        Assert.NotNull(tasks);
        var taskDetails = string.Join("; ", tasks!.Select(task =>
            $"{task.GetProperty("type").GetInt32()}: state {task.GetProperty("state").GetInt32()} {task.GetProperty("error").GetString()}"));
        Assert.True(runDocument.RootElement.GetProperty("state").GetInt32() == (int)WorkflowRunState.Completed, taskDetails);
        var validationTask = Assert.Single(tasks!, task =>
            task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.TestingValidation);
        Assert.Equal((int)WorkflowTaskState.Succeeded, validationTask.GetProperty("state").GetInt32());
        Assert.Equal(1, validationTask.GetProperty("attemptCount").GetInt32());

        var implementationTask = Assert.Single(tasks, task =>
            task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Implementation);
        var shortUrlArtifact = implementationTask.GetProperty("result").GetProperty("artifactReferences").EnumerateArray()
            .Select(item => item.GetString())
            .Single(item => item!.StartsWith("shorturl-code:", StringComparison.Ordinal));
        var code = shortUrlArtifact!["shorturl-code:".Length..];
        var analytics = await _client.GetFromJsonAsync<AnalyticsResponse>($"/api/urls/{code}/analytics");
        Assert.NotNull(analytics);
        Assert.Equal(2, analytics.TotalClicks);
        Assert.Equal(2, analytics.ClickTimestampsUtc.Count);
        Assert.Equal(analytics.ClickTimestampsUtc.OrderBy(timestamp => timestamp), analytics.ClickTimestampsUtc);

        Assert.Contains("Analytics reported 2 events and 2 persisted timestamps", string.Join(" ",
            validationTask.GetProperty("result").GetProperty("validationEvidence").EnumerateArray()
                .Select(item => item.GetString())), StringComparison.OrdinalIgnoreCase);

        var audit = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.DoesNotContain(audit!, item => item.GetProperty("eventType").GetString() is "task.failed" or "task.retry_scheduled");
        Assert.Equal(1, audit!.Count(item => item.GetProperty("eventType").GetString() == "task.attempt_started"
            && item.GetProperty("taskId").GetGuid() == validationTask.GetProperty("id").GetGuid()));
        Assert.Single(audit!, item => item.GetProperty("eventType").GetString() == "task.succeeded"
            && item.GetProperty("taskId").GetGuid() == validationTask.GetProperty("id").GetGuid());

        var metrics = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/workflows/{runId}/metrics");
        Assert.Equal(0, metrics.GetProperty("retryCount").GetInt32());
    }

    [Fact]
    public async Task Test_injected_transient_failure_retries_and_persists_audit_events()
    {
        await using var factory = new TransientRetryApiFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/workflows", new
        {
            requirement = TransientFirstAttemptExecutor.TestRequirement
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal((int)WorkflowRunState.Completed, runDocument.RootElement.GetProperty("state").GetInt32());

        var tasks = await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        var validationTask = Assert.Single(tasks!, task =>
            task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.TestingValidation);
        Assert.Equal((int)WorkflowTaskState.Succeeded, validationTask.GetProperty("state").GetInt32());
        Assert.Equal(2, validationTask.GetProperty("attemptCount").GetInt32());

        var audit = await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.Single(audit!, item => item.GetProperty("eventType").GetString() == "task.failed"
            && item.GetProperty("taskId").GetGuid() == validationTask.GetProperty("id").GetGuid());
        Assert.Single(audit!, item => item.GetProperty("eventType").GetString() == "task.retry_scheduled"
            && item.GetProperty("taskId").GetGuid() == validationTask.GetProperty("id").GetGuid());
        Assert.Equal(2, audit!.Count(item => item.GetProperty("eventType").GetString() == "task.attempt_started"
            && item.GetProperty("taskId").GetGuid() == validationTask.GetProperty("id").GetGuid()));
        Assert.Single(audit!, item => item.GetProperty("eventType").GetString() == "task.succeeded"
            && item.GetProperty("taskId").GetGuid() == validationTask.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Greenfield_scenario_executes_and_validates_persisted_expiration_behavior()
    {
        var response = await _client.PostAsync("/api/scenarios/greenfield-expiration/run", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal((int)WorkflowRunState.Completed, runDocument.RootElement.GetProperty("state").GetInt32());

        var tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        Assert.NotNull(tasks);
        var implementation = Assert.Single(tasks!, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Implementation);
        var artifact = implementation.GetProperty("result").GetProperty("artifactReferences").EnumerateArray()
            .Select(item => item.GetString()).Single(item => item!.StartsWith("shorturl-code:", StringComparison.Ordinal));
        var code = artifact!["shorturl-code:".Length..];
        var metadata = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/urls/{code}");
        Assert.True(metadata.GetProperty("expiresAtUtc").ValueKind != System.Text.Json.JsonValueKind.Null);

        var validation = Assert.Single(tasks!, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.TestingValidation);
        var evidence = string.Join(" ", validation.GetProperty("result").GetProperty("validationEvidence").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Contains("Expired URL remained expired", evidence, StringComparison.Ordinal);
        Assert.Contains("click count stayed 1", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ambiguous_scenario_waits_for_and_persists_clarification_before_implementation()
    {
        var createResponse = await _client.PostAsync("/api/scenarios/ambiguous-link-expiration/run", content: null);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var runDocument = System.Text.Json.JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var runId = runDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal((int)WorkflowRunState.AwaitingClarification, runDocument.RootElement.GetProperty("state").GetInt32());

        var tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        Assert.NotNull(tasks);
        var clarification = Assert.Single(tasks!, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Clarification);
        var implementation = Assert.Single(tasks, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Implementation);
        Assert.Equal((int)WorkflowTaskState.AwaitingClarification, clarification.GetProperty("state").GetInt32());
        Assert.Equal((int)WorkflowTaskState.Pending, implementation.GetProperty("state").GetInt32());
        Assert.Equal(4, clarification.GetProperty("result").GetProperty("decisions").GetArrayLength());

        var audit = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/audit");
        Assert.Contains(audit!, item => item.GetProperty("eventType").GetString() == "workflow.clarification_requested");

        var resumeResponse = await _client.PostAsJsonAsync($"/api/workflows/{runId}/clarifications", new
        {
            response = "Expiration is optional per link; existing links remain permanent and expired links return HTTP 410 without counting clicks."
        });
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        using var resumedRun = System.Text.Json.JsonDocument.Parse(await resumeResponse.Content.ReadAsStringAsync());
        Assert.Equal((int)WorkflowRunState.Completed, resumedRun.RootElement.GetProperty("state").GetInt32());

        tasks = await _client.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"/api/workflows/{runId}/tasks");
        Assert.NotNull(tasks);
        clarification = Assert.Single(tasks!, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Clarification);
        implementation = Assert.Single(tasks, task => task.GetProperty("type").GetInt32() == (int)WorkflowTaskType.Implementation);
        Assert.Contains("Clarification response:", string.Join(" ",
            clarification.GetProperty("result").GetProperty("validationEvidence").EnumerateArray()
                .Select(item => item.GetString())), StringComparison.Ordinal);
        Assert.Equal((int)WorkflowTaskState.Succeeded, implementation.GetProperty("state").GetInt32());
    }

    private sealed record ShortUrlResponse(
        string Code,
        string OriginalUrl,
        long ClickCount,
        DateTimeOffset? ExpiresAtUtc,
        bool IsExpired);

    private sealed record ExpiredUrlResponse(string Code, DateTimeOffset ExpiresAtUtc);

    private sealed record AnalyticsResponse(string Code, long TotalClicks, IReadOnlyList<DateTimeOffset> ClickTimestampsUtc);
}

public class ShortUrlApiFactory : WebApplicationFactory<Program>
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

public sealed class TransientRetryApiFactory : ShortUrlApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWorkflowTaskExecutor>();
            services.AddScoped<IWorkflowTaskExecutor, TransientFirstAttemptExecutor>();
        });
    }
}

public sealed class TransientFirstAttemptExecutor(IShortUrlService shortUrlService) : IWorkflowTaskExecutor
{
    public const string TestRequirement = "Add configurable URL expiration for the retry audit test.";

    private readonly RuleBasedWorkflowTaskExecutor _normalExecutor = new(shortUrlService);

    public Task<WorkflowAgentResult> ExecuteAsync(
        WorkflowTaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.OriginalRequirement == TestRequirement
            && context.Task.Type == WorkflowTaskType.TestingValidation
            && context.Attempt == 1)
        {
            return Task.FromResult(new WorkflowAgentResult(
                false,
                "Test fixture injected one transient validation failure.",
                [],
                [],
                [],
                ["Test fixture transient failure."],
                [],
                IsTransient: true));
        }

        return _normalExecutor.ExecuteAsync(context, cancellationToken);
    }
}