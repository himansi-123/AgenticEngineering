using AgenticEngineering.Application.Workflows;
using AgenticEngineering.Domain.Workflows;
using AgenticEngineering.Infrastructure.ShortUrls;
using Microsoft.EntityFrameworkCore;

namespace AgenticEngineering.Infrastructure.Workflows;

public sealed class SqliteWorkflowRepository(IDbContextFactory<ShortUrlDbContext> contextFactory) : IWorkflowRepository
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task CreateAsync(WorkflowRun run, IReadOnlyCollection<WorkflowTask> tasks, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.WorkflowRuns.Add(run);
            dbContext.WorkflowTasks.AddRange(tasks);
            dbContext.WorkflowAuditEvents.Add(new WorkflowAuditEvent(
                run.Id, null, "workflow.created", null, run.State.ToString(),
                "Workflow requirement and validated task graph persisted.", DateTimeOffset.UtcNow));
            foreach (var task in tasks)
            {
                dbContext.WorkflowAuditEvents.Add(new WorkflowAuditEvent(
                    run.Id, task.Id, "task.created", null, task.State.ToString(),
                    $"Created {task.Type} task with priority {task.Priority}.", DateTimeOffset.UtcNow));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveRunTransitionAsync(
        WorkflowRun run,
        WorkflowRunState previousState,
        string detail,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.WorkflowRuns.Update(run);
            dbContext.WorkflowAuditEvents.Add(new WorkflowAuditEvent(
                run.Id, null, "workflow.state_changed", previousState.ToString(), run.State.ToString(), detail, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveTaskTransitionAsync(WorkflowTask task, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var previous = await dbContext.WorkflowTasks.AsNoTracking()
                .SingleAsync(item => item.Id == task.Id, cancellationToken);
            dbContext.WorkflowTasks.Update(task);
            var eventType = task.State switch
            {
                WorkflowTaskState.Running => "task.attempt_started",
                WorkflowTaskState.Failed => "task.failed",
                WorkflowTaskState.Succeeded => "task.succeeded",
                WorkflowTaskState.Skipped => "task.skipped",
                WorkflowTaskState.Cancelled => "task.cancelled",
                WorkflowTaskState.Ready when previous.State == WorkflowTaskState.Failed => "task.retry_scheduled",
                WorkflowTaskState.AwaitingApproval => "approval.requested",
                _ => "task.state_changed"
            };
            var detail = System.Text.Json.JsonSerializer.Serialize(new
            {
                task.AttemptCount,
                task.Error,
                task.OutputJson
            });
            dbContext.WorkflowAuditEvents.Add(new WorkflowAuditEvent(
                task.RunId, task.Id, eventType, previous.State.ToString(), task.State.ToString(), detail, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<WorkflowRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(run => run.Id == runId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowTask>> GetTasksAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.WorkflowTasks.AsNoTracking()
            .Where(task => task.RunId == runId)
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowAuditEvent>> GetAuditEventsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.WorkflowAuditEvents.AsNoTracking()
            .Where(item => item.RunId == runId)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.WorkflowRuns.AsNoTracking().ToArrayAsync(cancellationToken);
    }
}