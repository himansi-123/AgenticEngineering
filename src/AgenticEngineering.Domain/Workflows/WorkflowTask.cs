namespace AgenticEngineering.Domain.Workflows;

public sealed class WorkflowTask
{
    private WorkflowTask()
    {
    }

    public WorkflowTask(Guid runId, WorkflowTaskDefinition definition)
    {
        Id = definition.Id;
        RunId = runId;
        Type = definition.Type;
        Priority = definition.Priority;
        State = WorkflowTaskState.Pending;
        DependenciesJson = System.Text.Json.JsonSerializer.Serialize(definition.Dependencies);
        InputArtifactsJson = System.Text.Json.JsonSerializer.Serialize(definition.InputArtifacts);
        OutputArtifactsJson = System.Text.Json.JsonSerializer.Serialize(definition.OutputArtifacts);
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public WorkflowTaskType Type { get; private set; }

    public int Priority { get; private set; }

    public WorkflowTaskState State { get; private set; }

    public int AttemptCount { get; private set; }

    public string DependenciesJson { get; private set; } = "[]";

    public string InputArtifactsJson { get; private set; } = "[]";

    public string OutputArtifactsJson { get; private set; } = "[]";

    public string? Error { get; private set; }

    public string? OutputJson { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void TransitionTo(WorkflowTaskState next, DateTimeOffset changedAtUtc, string? error = null)
    {
        var allowed = State switch
        {
            WorkflowTaskState.Pending => next is WorkflowTaskState.Ready or WorkflowTaskState.Skipped or WorkflowTaskState.Cancelled,
            WorkflowTaskState.Ready => next is WorkflowTaskState.Running or WorkflowTaskState.Skipped or WorkflowTaskState.Cancelled,
            WorkflowTaskState.Running => next is WorkflowTaskState.AwaitingApproval or WorkflowTaskState.AwaitingClarification or WorkflowTaskState.Succeeded or WorkflowTaskState.Failed or WorkflowTaskState.Cancelled,
            WorkflowTaskState.AwaitingApproval => next is WorkflowTaskState.Ready or WorkflowTaskState.Failed or WorkflowTaskState.Cancelled,
            WorkflowTaskState.AwaitingClarification => next is WorkflowTaskState.Succeeded or WorkflowTaskState.Failed or WorkflowTaskState.Cancelled,
            WorkflowTaskState.Failed => next is WorkflowTaskState.Ready or WorkflowTaskState.Skipped or WorkflowTaskState.Cancelled,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"Workflow task cannot transition from {State} to {next}.");
        }

        State = next;
        UpdatedAtUtc = changedAtUtc;
        Error = next == WorkflowTaskState.Failed ? error : null;
        if (next == WorkflowTaskState.Running)
        {
            AttemptCount++;
        }
    }

    public void SetOutput(string outputJson)
    {
        OutputJson = outputJson;
    }
}