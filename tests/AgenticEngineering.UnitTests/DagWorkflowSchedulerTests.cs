using System.Collections.Concurrent;
using AgenticEngineering.Application.Workflows;
using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.UnitTests;

public sealed class DagWorkflowSchedulerTests
{
    [Fact]
    public async Task Independent_branch_runs_while_another_branch_is_blocked()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondObservedFirstRunning = false;
        var executor = new DelegateExecutor(async (context, cancellationToken) =>
        {
            if (context.Task.Type == WorkflowTaskType.Implementation)
            {
                firstStarted.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }
            else if (context.Task.Type == WorkflowTaskType.Documentation)
            {
                await firstStarted.Task.WaitAsync(cancellationToken);
                secondObservedFirstRunning = !releaseFirst.Task.IsCompleted;
                releaseFirst.SetResult();
            }

            return Success(context.Task.Type.ToString());
        });
        var scheduler = new DagWorkflowScheduler(executor);
        var implementation = Definition(Guid.Parse("00000000-0000-0000-0000-000000000001"), WorkflowTaskType.Implementation);
        var documentation = Definition(Guid.Parse("00000000-0000-0000-0000-000000000002"), WorkflowTaskType.Documentation);
        var summary = Definition(Guid.Parse("00000000-0000-0000-0000-000000000003"), WorkflowTaskType.FinalSummary, implementation.Id, documentation.Id);

        var result = await scheduler.ExecuteAsync(
            Guid.NewGuid(), "requirement", [implementation, documentation, summary],
            new WorkflowSchedulerOptions(MaxConcurrency: 2, RetryBaseDelay: TimeSpan.Zero),
            null,
            CancellationToken.None);

        Assert.True(secondObservedFirstRunning);
        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowTaskState.Succeeded, result.Tasks.Single(task => task.Id == summary.Id).State);
    }

    [Fact]
    public async Task Scheduler_retries_transient_failure_within_configured_limit()
    {
        var attempts = 0;
        var executor = new DelegateExecutor((context, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? new WorkflowAgentResult(false, "temporary", [], [], [], ["temporary"], [], IsTransient: true)
                : Success("recovered"));
        });
        var task = Definition(Guid.NewGuid(), WorkflowTaskType.TestingValidation);
        var scheduler = new DagWorkflowScheduler(executor);

        var result = await scheduler.ExecuteAsync(
            Guid.NewGuid(), "requirement", [task],
            new WorkflowSchedulerOptions(MaxRetries: 1, RetryBaseDelay: TimeSpan.Zero),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, attempts);
        Assert.Equal(2, result.Tasks.Single().AttemptCount);
    }

    [Fact]
    public async Task Permanent_failure_skips_only_its_dependents()
    {
        var failed = Definition(Guid.NewGuid(), WorkflowTaskType.Implementation);
        var dependent = Definition(Guid.NewGuid(), WorkflowTaskType.TestingValidation, failed.Id);
        var independent = Definition(Guid.NewGuid(), WorkflowTaskType.Documentation);
        var executor = new DelegateExecutor((context, _) => Task.FromResult(
            context.Task.Type == WorkflowTaskType.Implementation
                ? new WorkflowAgentResult(false, "permanent", [], [], [], ["permanent"], [])
                : Success("done")));
        var scheduler = new DagWorkflowScheduler(executor);

        var result = await scheduler.ExecuteAsync(
            Guid.NewGuid(), "requirement", [failed, dependent, independent],
            new WorkflowSchedulerOptions(MaxConcurrency: 2), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowTaskState.Failed, result.Tasks.Single(task => task.Id == failed.Id).State);
        Assert.Equal(WorkflowTaskState.Skipped, result.Tasks.Single(task => task.Id == dependent.Id).State);
        Assert.Equal(WorkflowTaskState.Succeeded, result.Tasks.Single(task => task.Id == independent.Id).State);
    }

    [Fact]
    public async Task Scheduler_observes_task_timeout()
    {
        var executor = new DelegateExecutor(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Success("unreachable");
        });
        var scheduler = new DagWorkflowScheduler(executor);
        var task = Definition(Guid.NewGuid(), WorkflowTaskType.TestingValidation);

        var result = await scheduler.ExecuteAsync(
            Guid.NewGuid(), "requirement", [task],
            new WorkflowSchedulerOptions(MaxRetries: 0, TaskTimeout: TimeSpan.FromMilliseconds(20)),
            null,
            CancellationToken.None);

        Assert.Equal(WorkflowTaskState.Failed, result.Tasks.Single().State);
        Assert.Contains("timeout", result.Tasks.Single().Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scheduler_cancels_running_task_and_safe_stops_pending_work()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new DelegateExecutor(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Success("unreachable");
        });
        var first = Definition(Guid.NewGuid(), WorkflowTaskType.Implementation);
        var dependent = Definition(Guid.NewGuid(), WorkflowTaskType.TestingValidation, first.Id);
        var scheduler = new DagWorkflowScheduler(executor);

        var execution = scheduler.ExecuteAsync(
            Guid.NewGuid(), "requirement", [first, dependent],
            new WorkflowSchedulerOptions(MaxConcurrency: 1), null, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        var result = await execution;

        Assert.True(result.Cancelled);
        Assert.Equal(WorkflowTaskState.Cancelled, result.Tasks.Single(task => task.Id == first.Id).State);
        Assert.Equal(WorkflowTaskState.Cancelled, result.Tasks.Single(task => task.Id == dependent.Id).State);
    }

    private static WorkflowTaskDefinition Definition(Guid id, WorkflowTaskType type, params Guid[] dependencies)
    {
        return new WorkflowTaskDefinition(id, type, 0, dependencies, [], []);
    }

    private static WorkflowAgentResult Success(string summary)
        => new(true, summary, [], [], [], [], []);

    private sealed class DelegateExecutor(
        Func<WorkflowTaskExecutionContext, CancellationToken, Task<WorkflowAgentResult>> execute) : IWorkflowTaskExecutor
    {
        public Task<WorkflowAgentResult> ExecuteAsync(
            WorkflowTaskExecutionContext context,
            CancellationToken cancellationToken) => execute(context, cancellationToken);
    }
}