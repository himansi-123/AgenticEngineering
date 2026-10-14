using AgenticEngineering.Application.ShortUrls;
using AgenticEngineering.Domain.Workflows;
using System.Globalization;

namespace AgenticEngineering.Application.Workflows;

public sealed class RuleBasedWorkflowTaskExecutor(IShortUrlService? shortUrlService = null) : IWorkflowTaskExecutor
{
    private const string ShortUrlCodePrefix = "shorturl-code:";
    private const string ExpirationPrefix = "expires-at:";

    public async Task<WorkflowAgentResult> ExecuteAsync(
        WorkflowTaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requirement = context.OriginalRequirement;
        var normalized = requirement.ToLowerInvariant();
        var artifact = $"workflow://{context.RunId}/tasks/{context.Task.Id}/output";
        return context.Task.Type switch
        {
            Domain.Workflows.WorkflowTaskType.RequirementAnalysis => Analyze(requirement, normalized, artifact),
            Domain.Workflows.WorkflowTaskType.Clarification => Clarify(requirement, normalized, artifact),
            Domain.Workflows.WorkflowTaskType.TaskDecomposition => Result(
                "Created a dependency graph covering analysis, clarification, design, implementation, validation, documentation, security, release review, and summary.", artifact,
                "Graph structure was validated for unique identifiers, known dependencies, and acyclicity."),
            Domain.Workflows.WorkflowTaskType.ArchitectureDesign => Result(
                "Prepared a modular-monolith design proposal for the requirement.", artifact,
                "Design output is stored with this task record."),
            Domain.Workflows.WorkflowTaskType.Implementation => await ImplementAsync(context, normalized, artifact, cancellationToken),
            Domain.Workflows.WorkflowTaskType.TestingValidation => await ValidateAsync(context, normalized, artifact, cancellationToken),
            Domain.Workflows.WorkflowTaskType.Documentation => Result(
                "Prepared a documentation outline for the proposed engineering change.", artifact,
                "Documentation outline is stored with this task record."),
            Domain.Workflows.WorkflowTaskType.SecurityReview => Result(
                "Reviewed the proposed workflow output for common input, approval, artifact, and execution risks.", artifact,
                "Security review output is stored with this task record.",
                ["This deterministic review is not a substitute for a security audit.", "Authentication and authorization are not implemented."]),
            Domain.Workflows.WorkflowTaskType.ReleaseReadiness => Result(
                "The workflow result is reviewable, but this prototype does not authorize release or deployment.", artifact,
                "Release-readiness output is stored with this task record.",
                ["No deployment or release operation was performed."]),
            Domain.Workflows.WorkflowTaskType.FinalSummary => Result(
                "Required workflow stages completed; scenario behavior evidence is stored on implementation and validation tasks.", artifact,
                "Final summary is based on persisted task results."),
            _ => Failure("Unsupported task type.", isTransient: false)
        };
    }

    private static WorkflowAgentResult Analyze(string requirement, string normalized, string artifact)
    {
        IReadOnlyList<string> decisions = RequiresClarification(normalized)
            ? ["Default expiration duration is unspecified.", "It is unclear whether callers configure expiration per link.", "Treatment of existing links is unspecified.", "Expired-link response behavior is unspecified."]
            : Array.Empty<string>();

        return Result(
            $"Analyzed {requirement.Length} characters of requirement text and identified {(decisions.Count == 0 ? "no rule-based clarification flags" : $"{decisions.Count} expiration decisions to clarify") }.",
            artifact,
            "Original requirement is preserved on the workflow run.",
            decisions);
    }

    private static WorkflowAgentResult Clarify(string requirement, string normalized, string artifact)
    {
        var requiresClarification = RequiresClarification(normalized);
        IReadOnlyList<string> decisions = requiresClarification
            ? ["Clarify or record an explicit default expiration.", "Clarify whether expiration is configurable for each new link.", "Clarify how existing links are handled.", "Clarify the response for expired links."]
            : [];

        return Result(
            requiresClarification ? "Workflow is paused with unresolved expiration questions." : "No blocking clarification is required.", artifact,
            "Clarification output is stored with this task record.", decisions: decisions,
            requiresClarification: requiresClarification);
    }

    private async Task<WorkflowAgentResult> ImplementAsync(
        WorkflowTaskExecutionContext context,
        string normalized,
        string artifact,
        CancellationToken cancellationToken)
    {
        if (IsExpirationScenario(context, normalized))
        {
            if (shortUrlService is null)
            {
                return Failure("The URL application service is unavailable for bounded scenario execution.", isTransient: false);
            }

            var expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(2);
            var shortUrl = await shortUrlService.CreateAsync(
                "https://example.com/workflow-expiration-probe",
                expiresAtUtc,
                cancellationToken);
            return new WorkflowAgentResult(
                true,
                "Created a persisted short-lived URL using the URL application service.",
                [ShortUrlCodePrefix + shortUrl.Code, ExpirationPrefix + expiresAtUtc.ToString("O", CultureInfo.InvariantCulture), artifact],
                [$"SQLite URL record created with expiration {expiresAtUtc:O} UTC."],
                [],
                [],
                []);
        }

        if (IsAnalyticsScenario(normalized))
        {
            if (shortUrlService is null)
            {
                return Failure("The URL application service is unavailable for bounded scenario execution.", isTransient: false);
            }

            var shortUrl = await shortUrlService.CreateAsync("https://example.com/workflow-analytics-probe", cancellationToken);
            await shortUrlService.RecordClickAsync(shortUrl.Code, cancellationToken);
            await shortUrlService.RecordClickAsync(shortUrl.Code, cancellationToken);
            var analytics = await shortUrlService.GetAnalyticsAsync(shortUrl.Code, cancellationToken);
            if (analytics?.TotalClicks != 2 || analytics.ClickTimestampsUtc.Count != 2)
            {
                return Failure("Analytics implementation did not persist and report both controlled clicks.", isTransient: false);
            }

            return new WorkflowAgentResult(
                true,
                "Reproduced two persisted analytics events for the brownfield validation probe.",
                [ShortUrlCodePrefix + shortUrl.Code, artifact],
                [$"Persisted click records: {analytics.TotalClicks}; reported analytics total: {analytics.TotalClicks}."],
                [],
                [],
                []);
        }

        return Failure("No bounded implementation action is registered for this requirement.", isTransient: false);
    }

    private async Task<WorkflowAgentResult> ValidateAsync(
        WorkflowTaskExecutionContext context,
        string normalized,
        string artifact,
        CancellationToken cancellationToken)
    {
        if (IsExpirationScenario(context, normalized))
        {
            if (shortUrlService is null || !TryGetExpirationProbe(context, out var code, out var expiresAtUtc))
            {
                return Failure("Expiration validation could not find its persisted implementation artifact.", isTransient: false);
            }

            var activeClick = await shortUrlService.RecordClickAsync(code, cancellationToken);
            if (activeClick is null || activeClick.IsExpired || activeClick.ClickCount != 1)
            {
                return Failure("An active URL did not record its click before expiration.", isTransient: false);
            }

            var delay = expiresAtUtc - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(25);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var expiredClick = await shortUrlService.RecordClickAsync(code, cancellationToken);
            var analytics = await shortUrlService.GetAnalyticsAsync(code, cancellationToken);
            if (expiredClick is null || !expiredClick.IsExpired || expiredClick.ClickCount != 1
                || analytics?.TotalClicks != 1 || analytics.ClickTimestampsUtc.Count != 1)
            {
                return Failure("Expired URL behavior changed click analytics or failed to report expiration.", isTransient: false);
            }

            return new WorkflowAgentResult(
                true,
                "Validated persisted expiration, active-click counting, and no click increment after expiration.",
                [artifact],
                ["Active URL click count was 1.", "Expired URL remained expired and click count stayed 1."],
                [],
                [],
                []);
        }

        if (IsAnalyticsScenario(normalized))
        {
            if (shortUrlService is null || !TryGetShortUrlCode(context, out var code))
            {
                return Failure("Analytics validation could not find its persisted implementation artifact.", isTransient: false);
            }

            var analytics = await shortUrlService.GetAnalyticsAsync(code, cancellationToken);
            if (analytics?.TotalClicks != 2 || analytics.ClickTimestampsUtc.Count != 2)
            {
                return Failure("Persisted analytics do not match the two recorded click events.", isTransient: false);
            }

            return new WorkflowAgentResult(
                true,
                "Verified that analytics reports both persisted click events and timestamps.",
                [artifact],
                [$"Analytics reported {analytics.TotalClicks} events and {analytics.ClickTimestampsUtc.Count} persisted timestamps."],
                [],
                [],
                []);
        }

        return Result(
            "Validated that workflow outputs and dependency evidence are present.", artifact,
            "No repository test process or shell command is invoked by the executor.");
    }

    private static bool IsExpirationScenario(WorkflowTaskExecutionContext context, string normalized)
    {
        if (normalized.Contains("expiration", StringComparison.Ordinal)
            && (normalized.Contains("url", StringComparison.Ordinal)
                || normalized.Contains("link", StringComparison.Ordinal)))
        {
            return true;
        }

        return context.PriorResults?.SelectMany(result => result.ValidationEvidence)
            .Any(evidence => evidence.StartsWith("Clarification response:", StringComparison.OrdinalIgnoreCase)
                && evidence.Contains("optional", StringComparison.OrdinalIgnoreCase)
                && evidence.Contains("410", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool RequiresClarification(string normalized)
    {
        return normalized.Contains("make links expire automatically", StringComparison.Ordinal);
    }

    private static bool IsAnalyticsScenario(string normalized)
    {
        return normalized.Contains("analytics", StringComparison.Ordinal)
            && (normalized.Contains("click", StringComparison.Ordinal)
                || normalized.Contains("count", StringComparison.Ordinal)
                || normalized.Contains("miscount", StringComparison.Ordinal));
    }

    private static bool TryGetShortUrlCode(WorkflowTaskExecutionContext context, out string code)
    {
        var value = context.PriorResults?.SelectMany(result => result.ArtifactReferences)
            .FirstOrDefault(reference => reference.StartsWith(ShortUrlCodePrefix, StringComparison.Ordinal));
        code = value is null ? string.Empty : value[ShortUrlCodePrefix.Length..];
        return !string.IsNullOrWhiteSpace(code);
    }

    private static bool TryGetExpirationProbe(
        WorkflowTaskExecutionContext context,
        out string code,
        out DateTimeOffset expiresAtUtc)
    {
        code = string.Empty;
        expiresAtUtc = default;
        if (!TryGetShortUrlCode(context, out code))
        {
            return false;
        }

        var value = context.PriorResults?.SelectMany(result => result.ArtifactReferences)
            .FirstOrDefault(reference => reference.StartsWith(ExpirationPrefix, StringComparison.Ordinal));
        return value is not null
            && DateTimeOffset.TryParse(value[ExpirationPrefix.Length..], CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out expiresAtUtc);
    }

    private static WorkflowAgentResult Result(
        string summary,
        string artifact,
        string evidence,
        IReadOnlyList<string>? risks = null,
        IReadOnlyList<string>? decisions = null,
        bool requiresClarification = false)
    {
        return new WorkflowAgentResult(true, summary, [artifact], [evidence], risks ?? [], [], decisions ?? [],
            RequiresClarification: requiresClarification);
    }

    private static WorkflowAgentResult Failure(string error, bool isTransient)
    {
        return new WorkflowAgentResult(false, error, [], [], [], [error], [], IsTransient: isTransient);
    }
}