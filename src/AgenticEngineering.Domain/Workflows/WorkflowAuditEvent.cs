namespace AgenticEngineering.Domain.Workflows;

public sealed class WorkflowAuditEvent
{
    private WorkflowAuditEvent()
    {
    }

    public WorkflowAuditEvent(
        Guid runId,
        Guid? taskId,
        string eventType,
        string? previousState,
        string? newState,
        string detail,
        DateTimeOffset occurredAtUtc)
    {
        RunId = runId;
        TaskId = taskId;
        EventType = eventType;
        PreviousState = previousState;
        NewState = newState;
        Detail = detail;
        OccurredAtUtc = occurredAtUtc;
    }

    public long Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid? TaskId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string? PreviousState { get; private set; }

    public string? NewState { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }
}