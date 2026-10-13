using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.Application.Workflows;

public sealed record CreateApprovalRequestDto(Guid RunId, WorkflowProtectedAction Action, string Description);

public sealed record ApprovalDecisionDto(string Reviewer, string? Notes);

public sealed record WorkflowApprovalDto(
    Guid Id,
    Guid RunId,
    WorkflowProtectedAction Action,
    string Description,
    WorkflowApprovalState State,
    bool ValidationPassed,
    bool SecurityReviewPassed,
    string? Reviewer,
    string? DecisionNotes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DecidedAtUtc);

public interface IWorkflowApprovalRepository
{
    Task CreateAsync(WorkflowApprovalRequest approval, CancellationToken cancellationToken);

    Task SaveDecisionAsync(WorkflowApprovalRequest approval, WorkflowApprovalState previousState, CancellationToken cancellationToken);

    Task<WorkflowApprovalRequest?> GetAsync(Guid approvalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowApprovalRequest>> GetAllAsync(CancellationToken cancellationToken);
}

public interface IWorkflowApprovalService
{
    Task<WorkflowApprovalDto> RequestAsync(CreateApprovalRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowApprovalDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<WorkflowApprovalDto?> ApproveAsync(Guid approvalId, ApprovalDecisionDto decision, CancellationToken cancellationToken);

    Task<WorkflowApprovalDto?> RejectAsync(Guid approvalId, ApprovalDecisionDto decision, CancellationToken cancellationToken);
}