using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.Application.Workflows;

public sealed class WorkflowApprovalService(
    IWorkflowRepository workflowRepository,
    IWorkflowApprovalRepository approvalRepository) : IWorkflowApprovalService
{
    public async Task<WorkflowApprovalDto> RequestAsync(
        CreateApprovalRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000)
        {
            throw new ArgumentException("Approval description must contain between 1 and 2000 characters.", nameof(request));
        }

        var run = await workflowRepository.GetRunAsync(request.RunId, cancellationToken);
        if (run is null)
        {
            throw new KeyNotFoundException("Workflow run was not found.");
        }

        if (run.State != WorkflowRunState.Completed)
        {
            throw new InvalidOperationException("Approval cannot be requested before the workflow completes validation.");
        }

        var tasks = await workflowRepository.GetTasksAsync(request.RunId, cancellationToken);
        var validationPassed = tasks.Any(task => task.Type == WorkflowTaskType.TestingValidation && task.State == WorkflowTaskState.Succeeded);
        var securityReviewPassed = tasks.Any(task => task.Type == WorkflowTaskType.SecurityReview && task.State == WorkflowTaskState.Succeeded);
        var releaseReviewPassed = tasks.Any(task => task.Type == WorkflowTaskType.ReleaseReadiness && task.State == WorkflowTaskState.Succeeded);

        if (!validationPassed || !securityReviewPassed
            || (request.Action == WorkflowProtectedAction.Release && !releaseReviewPassed))
        {
            throw new InvalidOperationException("Required validation and security review tasks have not succeeded.");
        }

        var approval = new WorkflowApprovalRequest(
            request.RunId, request.Action, request.Description.Trim(), validationPassed, securityReviewPassed, DateTimeOffset.UtcNow);
        await approvalRepository.CreateAsync(approval, cancellationToken);
        return ToDto(approval);
    }

    public async Task<IReadOnlyList<WorkflowApprovalDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var approvals = await approvalRepository.GetAllAsync(cancellationToken);
        return approvals.Select(ToDto).ToArray();
    }

    public Task<WorkflowApprovalDto?> ApproveAsync(
        Guid approvalId,
        ApprovalDecisionDto decision,
        CancellationToken cancellationToken)
    {
        return DecideAsync(approvalId, decision, approve: true, cancellationToken);
    }

    public Task<WorkflowApprovalDto?> RejectAsync(
        Guid approvalId,
        ApprovalDecisionDto decision,
        CancellationToken cancellationToken)
    {
        return DecideAsync(approvalId, decision, approve: false, cancellationToken);
    }

    private async Task<WorkflowApprovalDto?> DecideAsync(
        Guid approvalId,
        ApprovalDecisionDto decision,
        bool approve,
        CancellationToken cancellationToken)
    {
        var approval = await approvalRepository.GetAsync(approvalId, cancellationToken);
        if (approval is null)
        {
            return null;
        }

        var previousState = approval.State;
        approval.Decide(approve, decision.Reviewer, decision.Notes, DateTimeOffset.UtcNow);
        await approvalRepository.SaveDecisionAsync(approval, previousState, cancellationToken);
        return ToDto(approval);
    }

    private static WorkflowApprovalDto ToDto(WorkflowApprovalRequest approval)
    {
        return new WorkflowApprovalDto(
            approval.Id, approval.RunId, approval.Action, approval.Description, approval.State,
            approval.ValidationPassed, approval.SecurityReviewPassed, approval.Reviewer,
            approval.DecisionNotes, approval.CreatedAtUtc, approval.DecidedAtUtc);
    }
}

public sealed class WorkflowApprovalGate(IWorkflowApprovalRepository repository)
{
    public async Task ExecuteAsync(
        Guid approvalId,
        Func<CancellationToken, Task> protectedAction,
        CancellationToken cancellationToken)
    {
        var approval = await repository.GetAsync(approvalId, cancellationToken);
        if (approval is null || approval.State != WorkflowApprovalState.Approved
            || !approval.ValidationPassed || !approval.SecurityReviewPassed)
        {
            throw new InvalidOperationException("Protected action requires an approved request with successful validation and security evidence.");
        }

        await protectedAction(cancellationToken);
    }
}