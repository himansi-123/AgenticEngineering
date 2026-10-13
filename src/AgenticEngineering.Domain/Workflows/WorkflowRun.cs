namespace AgenticEngineering.Domain.Workflows;

public sealed class WorkflowRun
{
    private WorkflowRun()
    {
    }

    public WorkflowRun(string originalRequirement, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(originalRequirement))
        {
            throw new ArgumentException("A workflow requirement is required.", nameof(originalRequirement));
        }

        Id = Guid.NewGuid();
        OriginalRequirement = originalRequirement;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        State = WorkflowRunState.Created;
    }

    public Guid Id { get; private set; }

    public string OriginalRequirement { get; private set; } = string.Empty;

    public WorkflowRunState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public void TransitionTo(WorkflowRunState next, DateTimeOffset changedAtUtc)
    {
        var allowed = State switch
        {
            WorkflowRunState.Created => next is WorkflowRunState.Planning or WorkflowRunState.Failed or WorkflowRunState.Cancelled,
            WorkflowRunState.Planning => next is WorkflowRunState.Executing or WorkflowRunState.AwaitingApproval or WorkflowRunState.Failed or WorkflowRunState.Cancelled,
            WorkflowRunState.Executing => next is WorkflowRunState.AwaitingApproval or WorkflowRunState.Validating or WorkflowRunState.Completed or WorkflowRunState.Failed or WorkflowRunState.Cancelled,
            WorkflowRunState.AwaitingApproval => next is WorkflowRunState.Executing or WorkflowRunState.Failed or WorkflowRunState.Cancelled,
            WorkflowRunState.Validating => next is WorkflowRunState.AwaitingApproval or WorkflowRunState.Completed or WorkflowRunState.Failed or WorkflowRunState.Cancelled,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"Workflow run cannot transition from {State} to {next}.");
        }

        State = next;
        UpdatedAtUtc = changedAtUtc;
        if (next is WorkflowRunState.Completed or WorkflowRunState.Failed or WorkflowRunState.Cancelled)
        {
            CompletedAtUtc = changedAtUtc;
        }
    }
}