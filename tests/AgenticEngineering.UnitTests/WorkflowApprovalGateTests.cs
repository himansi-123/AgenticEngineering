using AgenticEngineering.Application.Workflows;
using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.UnitTests;

public sealed class WorkflowApprovalGateTests
{
    [Fact]
    public async Task Protected_action_does_not_run_without_approval()
    {
        var approval = new WorkflowApprovalRequest(
            Guid.NewGuid(), WorkflowProtectedAction.ApplyGeneratedCode, "Apply patch", true, true, DateTimeOffset.UtcNow);
        var gate = new WorkflowApprovalGate(new FakeApprovalRepository(approval));
        var actionExecuted = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.ExecuteAsync(
            approval.Id,
            _ =>
            {
                actionExecuted = true;
                return Task.CompletedTask;
            },
            CancellationToken.None));

        Assert.False(actionExecuted);
    }

    [Fact]
    public async Task Protected_action_does_not_run_after_rejection()
    {
        var approval = new WorkflowApprovalRequest(
            Guid.NewGuid(), WorkflowProtectedAction.ApplyGeneratedCode, "Apply patch", true, true, DateTimeOffset.UtcNow);
        approval.Decide(false, "reviewer", "Rejected", DateTimeOffset.UtcNow);
        var gate = new WorkflowApprovalGate(new FakeApprovalRepository(approval));
        var actionExecuted = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.ExecuteAsync(
            approval.Id,
            _ =>
            {
                actionExecuted = true;
                return Task.CompletedTask;
            },
            CancellationToken.None));

        Assert.False(actionExecuted);
    }

    [Fact]
    public async Task Protected_action_runs_only_after_approval_and_required_checks()
    {
        var approval = new WorkflowApprovalRequest(
            Guid.NewGuid(), WorkflowProtectedAction.ApplyGeneratedCode, "Apply patch", true, true, DateTimeOffset.UtcNow);
        approval.Decide(true, "reviewer", "Reviewed", DateTimeOffset.UtcNow);
        var gate = new WorkflowApprovalGate(new FakeApprovalRepository(approval));
        var actionExecuted = false;

        await gate.ExecuteAsync(approval.Id, _ =>
        {
            actionExecuted = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(actionExecuted);
    }

    [Fact]
    public void Approval_rejects_success_decision_without_validation_evidence()
    {
        var approval = new WorkflowApprovalRequest(
            Guid.NewGuid(), WorkflowProtectedAction.Release, "Release", false, true, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => approval.Decide(true, "reviewer", null, DateTimeOffset.UtcNow));
    }

    private sealed class FakeApprovalRepository(WorkflowApprovalRequest approval) : IWorkflowApprovalRepository
    {
        public Task CreateAsync(WorkflowApprovalRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveDecisionAsync(WorkflowApprovalRequest request, WorkflowApprovalState previousState, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkflowApprovalRequest?> GetAsync(Guid approvalId, CancellationToken cancellationToken)
            => Task.FromResult<WorkflowApprovalRequest?>(approval.Id == approvalId ? approval : null);

        public Task<IReadOnlyList<WorkflowApprovalRequest>> GetAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorkflowApprovalRequest>>([approval]);
    }
}