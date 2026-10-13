namespace AgenticEngineering.Domain.Workflows;

public enum WorkflowProtectedAction
{
    ApplyDatabaseMigration,
    ApplyGeneratedCode,
    Release
}

public enum WorkflowApprovalState
{
    Pending,
    Approved,
    Rejected
}

public sealed class WorkflowApprovalRequest
{
    private WorkflowApprovalRequest()
    {
    }

    public WorkflowApprovalRequest(
        Guid runId,
        WorkflowProtectedAction action,
        string description,
        bool validationPassed,
        bool securityReviewPassed,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        RunId = runId;
        Action = action;
        Description = description;
        ValidationPassed = validationPassed;
        SecurityReviewPassed = securityReviewPassed;
        CreatedAtUtc = createdAtUtc;
        State = WorkflowApprovalState.Pending;
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public WorkflowProtectedAction Action { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public WorkflowApprovalState State { get; private set; }

    public bool ValidationPassed { get; private set; }

    public bool SecurityReviewPassed { get; private set; }

    public string? Reviewer { get; private set; }

    public string? DecisionNotes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public void Decide(bool approve, string reviewer, string? notes, DateTimeOffset decidedAtUtc)
    {
        if (State != WorkflowApprovalState.Pending)
        {
            throw new InvalidOperationException("Only pending approval requests can be decided.");
        }

        if (string.IsNullOrWhiteSpace(reviewer))
        {
            throw new ArgumentException("A reviewer identity is required.", nameof(reviewer));
        }

        if (approve && (!ValidationPassed || !SecurityReviewPassed))
        {
            throw new InvalidOperationException("Approval requires successful validation and security review evidence.");
        }

        State = approve ? WorkflowApprovalState.Approved : WorkflowApprovalState.Rejected;
        Reviewer = reviewer;
        DecisionNotes = notes;
        DecidedAtUtc = decidedAtUtc;
    }
}