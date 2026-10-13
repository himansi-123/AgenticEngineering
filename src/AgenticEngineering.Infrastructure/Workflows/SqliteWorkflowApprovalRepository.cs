using AgenticEngineering.Application.Workflows;
using AgenticEngineering.Domain.Workflows;
using AgenticEngineering.Infrastructure.ShortUrls;
using Microsoft.EntityFrameworkCore;

namespace AgenticEngineering.Infrastructure.Workflows;

public sealed class SqliteWorkflowApprovalRepository(IDbContextFactory<ShortUrlDbContext> contextFactory) : IWorkflowApprovalRepository
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task CreateAsync(WorkflowApprovalRequest approval, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.WorkflowApprovalRequests.Add(approval);
            dbContext.WorkflowAuditEvents.Add(new WorkflowAuditEvent(
                approval.RunId, null, "approval.requested", null, approval.State.ToString(),
                $"{approval.Action}: {approval.Description}", DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveDecisionAsync(
        WorkflowApprovalRequest approval,
        WorkflowApprovalState previousState,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.WorkflowApprovalRequests.Update(approval);
            dbContext.WorkflowAuditEvents.Add(new WorkflowAuditEvent(
                approval.RunId, null, "approval.decided", previousState.ToString(), approval.State.ToString(),
                $"Approval {approval.Id} decided by {approval.Reviewer}: {approval.DecisionNotes}", DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<WorkflowApprovalRequest?> GetAsync(Guid approvalId, CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.WorkflowApprovalRequests.AsNoTracking()
            .SingleOrDefaultAsync(approval => approval.Id == approvalId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowApprovalRequest>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        var approvals = await dbContext.WorkflowApprovalRequests.AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return approvals.OrderBy(approval => approval.CreatedAtUtc).ToArray();
    }
}