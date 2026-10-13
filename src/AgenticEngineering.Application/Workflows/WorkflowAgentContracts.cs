using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.Application.Workflows;

public sealed record WorkflowAgentResult(
    bool Succeeded,
    string Summary,
    IReadOnlyList<string> ArtifactReferences,
    IReadOnlyList<string> ValidationEvidence,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Decisions,
    bool IsTransient = false);

public sealed record WorkflowTaskExecutionContext(
    Guid RunId,
    string OriginalRequirement,
    WorkflowTaskDefinition Task,
    int Attempt);

public interface IWorkflowTaskExecutor
{
    Task<WorkflowAgentResult> ExecuteAsync(
        WorkflowTaskExecutionContext context,
        CancellationToken cancellationToken);
}

public sealed record WorkflowSchedulerOptions(
    int MaxConcurrency = 3,
    int MaxRetries = 1,
    TimeSpan? TaskTimeout = null,
    TimeSpan? RetryBaseDelay = null)
{
    public TimeSpan EffectiveTaskTimeout => TaskTimeout ?? TimeSpan.FromSeconds(30);

    public TimeSpan EffectiveRetryBaseDelay => RetryBaseDelay ?? TimeSpan.FromMilliseconds(100);

    public void Validate()
    {
        if (MaxConcurrency is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), "Concurrency must be between 1 and 32.");
        }

        if (MaxRetries is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "Retries must be between 0 and 5.");
        }

        if (EffectiveTaskTimeout <= TimeSpan.Zero || EffectiveTaskTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(TaskTimeout), "Task timeout must be positive and no longer than five minutes.");
        }

        if (EffectiveRetryBaseDelay < TimeSpan.Zero || EffectiveRetryBaseDelay > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(RetryBaseDelay), "Retry delay must be between zero and 30 seconds.");
        }
    }
}

public sealed record WorkflowScheduleResult(
    IReadOnlyList<WorkflowTask> Tasks,
    bool Succeeded,
    bool Cancelled);