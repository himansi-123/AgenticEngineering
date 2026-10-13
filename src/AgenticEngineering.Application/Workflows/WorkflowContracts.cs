using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.Application.Workflows;

public sealed record CreateWorkflowRequest(string Requirement);

public sealed record WorkflowScenarioDto(string Id, string Kind, string Requirement, bool DemonstratesTransientFailure);

public sealed record WorkflowRunDto(
    Guid Id,
    string OriginalRequirement,
    WorkflowRunState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Summary);

public sealed record WorkflowTaskDto(
    Guid Id,
    WorkflowTaskType Type,
    int Priority,
    WorkflowTaskState State,
    IReadOnlyList<Guid> Dependencies,
    IReadOnlyList<string> InputArtifacts,
    IReadOnlyList<string> OutputArtifacts,
    int AttemptCount,
    string? Error,
    WorkflowAgentResult? Result,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkflowAuditEventDto(
    long Id,
    Guid RunId,
    Guid? TaskId,
    string EventType,
    string? PreviousState,
    string? NewState,
    string Detail,
    DateTimeOffset OccurredAtUtc);

public sealed record WorkflowMetricsDto(
    int TerminalWorkflowCount,
    int SuccessfulWorkflowCount,
    double? WorkflowSuccessRate,
    int TerminalTaskCount,
    int FailedTaskCount,
    double? TaskFailureRate,
    int TaskAttemptCount,
    int RetryCount,
    double? RetryFrequency,
    int RollbackCount,
    double? MeanTimeToRecoveryMilliseconds,
    long? EndToEndLatencyMilliseconds);

public interface IWorkflowRepository
{
    Task CreateAsync(WorkflowRun run, IReadOnlyCollection<WorkflowTask> tasks, CancellationToken cancellationToken);

    Task SaveRunTransitionAsync(WorkflowRun run, WorkflowRunState previousState, string detail, CancellationToken cancellationToken);

    Task SaveTaskTransitionAsync(WorkflowTask task, CancellationToken cancellationToken);

    Task<WorkflowRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowTask>> GetTasksAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowAuditEvent>> GetAuditEventsAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(CancellationToken cancellationToken);
}

public interface IWorkflowService
{
    Task<WorkflowRunDto> CreateAndExecuteAsync(CreateWorkflowRequest request, CancellationToken cancellationToken);

    Task<WorkflowRunDto?> GetAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowTaskDto>?> GetTasksAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowAuditEventDto>?> GetAuditAsync(Guid runId, CancellationToken cancellationToken);

    Task<WorkflowMetricsDto?> GetMetricsAsync(Guid runId, CancellationToken cancellationToken);
}