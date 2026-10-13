namespace AgenticEngineering.Application.Workflows;

public sealed class RuleBasedWorkflowTaskExecutor : IWorkflowTaskExecutor
{
    public Task<WorkflowAgentResult> ExecuteAsync(
        WorkflowTaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requirement = context.OriginalRequirement;
        var normalized = requirement.ToLowerInvariant();
        if (context.Task.Type == Domain.Workflows.WorkflowTaskType.TestingValidation
            && normalized.Contains("analytics-counting defect", StringComparison.Ordinal)
            && context.Attempt == 1)
        {
            return Task.FromResult(new WorkflowAgentResult(
                false,
                "Controlled demonstration failure: simulated transient regression-test runner interruption.",
                [],
                [],
                ["No external test process was launched."],
                ["Deterministic transient failure for brownfield recovery demonstration."],
                [],
                IsTransient: true));
        }

        var artifact = $"workflow://{context.RunId}/tasks/{context.Task.Id}/output";
        var result = context.Task.Type switch
        {
            Domain.Workflows.WorkflowTaskType.RequirementAnalysis => Analyze(requirement, normalized, artifact),
            Domain.Workflows.WorkflowTaskType.Clarification => Clarify(requirement, normalized, artifact),
            Domain.Workflows.WorkflowTaskType.TaskDecomposition => Result(
                "Created a dependency graph covering analysis, clarification, design, implementation, validation, documentation, security, release review, and summary.", artifact,
                "Graph structure was validated for unique identifiers, known dependencies, and acyclicity."),
            Domain.Workflows.WorkflowTaskType.ArchitectureDesign => Result(
                "Prepared a modular-monolith design proposal for the requirement.", artifact,
                "Design output is stored with this task record."),
            Domain.Workflows.WorkflowTaskType.Implementation => Result(
                "Prepared an implementation plan. This prototype does not apply generated source-code changes.", artifact,
                "Implementation plan is stored with this task record.",
                ["Generated code changes are not applied by this workflow executor."]),
            Domain.Workflows.WorkflowTaskType.TestingValidation => Result(
                "Validated the structured workflow result contract; repository tests were not invoked by this deterministic agent.", artifact,
                "Task output is stored with this task record."),
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
                "Workflow planning and deterministic review stages completed. No source-code changes or external commands were executed.", artifact,
                "Final summary is stored with this task record.",
                ["Implementation is plan-only in this milestone."]),
            _ => new WorkflowAgentResult(false, "Unsupported task type.", [], [], [], ["Unsupported task type."], [])
        };

        return Task.FromResult(result);
    }

    private static WorkflowAgentResult Analyze(string requirement, string normalized, string artifact)
    {
        IReadOnlyList<string> decisions = IsExpirationRequirement(normalized)
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
        IReadOnlyList<string> decisions = IsExpirationRequirement(normalized)
            ? ["Clarify or record an explicit default expiration.", "Clarify whether expiration is configurable for each new link.", "Clarify how existing links are handled.", "Clarify the response for expired links."]
            : ["No deterministic clarification rule matched; human review may still be needed."];

        return Result(
            "Recorded open questions without silently selecting product defaults.", artifact,
            "Clarification output is stored with this task record.", decisions);
    }

    private static bool IsExpirationRequirement(string normalized)
    {
        return normalized.Contains("expire", StringComparison.Ordinal)
            || normalized.Contains("expiration", StringComparison.Ordinal);
    }

    private static WorkflowAgentResult Result(
        string summary,
        string artifact,
        string evidence,
        IReadOnlyList<string>? risks = null,
        IReadOnlyList<string>? decisions = null)
    {
        return new WorkflowAgentResult(true, summary, [artifact], [evidence], risks ?? [], [], decisions ?? []);
    }
}