using System.Text.Json;
using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.Application.Workflows;

public sealed class WorkflowService(
    IWorkflowRepository repository,
    WorkflowPlanFactory planFactory,
    DagWorkflowScheduler scheduler,
    WorkflowSchedulerOptions schedulerOptions) : IWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkflowRunDto> CreateAndExecuteAsync(
        CreateWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Requirement) || request.Requirement.Length > 8000)
        {
            throw new ArgumentException("Requirement must contain between 1 and 8000 characters.", nameof(request));
        }

        var definitions = planFactory.Create(request.Requirement);
        _ = WorkflowTaskGraph.TopologicalOrder(definitions);
        var run = new WorkflowRun(request.Requirement.Trim(), DateTimeOffset.UtcNow);
        var tasks = definitions.Select(definition => new WorkflowTask(run.Id, definition)).ToArray();
        await repository.CreateAsync(run, tasks, cancellationToken);

        await TransitionRunAsync(run, WorkflowRunState.Planning, "Workflow plan created.", cancellationToken);
        await TransitionRunAsync(run, WorkflowRunState.Executing, "Deterministic workflow execution started.", cancellationToken);

        return await ExecuteRunAsync(run, definitions, tasks, cancellationToken);
    }

    public async Task<WorkflowRunDto?> ResolveClarificationAsync(
        Guid runId,
        ResolveWorkflowClarificationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Response) || request.Response.Length > 4000)
        {
            throw new ArgumentException("Clarification response must contain between 1 and 4000 characters.", nameof(request));
        }

        var run = await repository.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        if (run.State != WorkflowRunState.AwaitingClarification)
        {
            throw new InvalidOperationException("This workflow is not awaiting clarification.");
        }

        var tasks = (await repository.GetTasksAsync(runId, cancellationToken)).ToArray();
        var clarificationTask = tasks.Single(task =>
            task.Type == WorkflowTaskType.Clarification && task.State == WorkflowTaskState.AwaitingClarification);
        var previousResult = ReadResult(clarificationTask.OutputJson);
        clarificationTask.SetOutput(JsonSerializer.Serialize(new WorkflowAgentResult(
            true,
            "Clarification response recorded; workflow resumed.",
            previousResult?.ArtifactReferences ?? [],
            [$"Clarification response: {request.Response.Trim()}"],
            [],
            [],
            previousResult?.Decisions ?? []), JsonOptions));
        clarificationTask.TransitionTo(WorkflowTaskState.Succeeded, DateTimeOffset.UtcNow);
        await repository.SaveTaskTransitionAsync(clarificationTask, cancellationToken);

        var definitions = tasks.Select(task => new WorkflowTaskDefinition(
            task.Id,
            task.Type,
            task.Priority,
            JsonSerializer.Deserialize<Guid[]>(task.DependenciesJson) ?? [],
            JsonSerializer.Deserialize<string[]>(task.InputArtifactsJson) ?? [],
            JsonSerializer.Deserialize<string[]>(task.OutputArtifactsJson) ?? [])).ToArray();
        await TransitionRunAsync(run, WorkflowRunState.Executing, "Clarification was recorded; workflow execution resumed.", cancellationToken);
        return await ExecuteRunAsync(run, definitions, tasks, cancellationToken);
    }

    private async Task<WorkflowRunDto> ExecuteRunAsync(
        WorkflowRun run,
        IReadOnlyCollection<WorkflowTaskDefinition> definitions,
        IReadOnlyCollection<WorkflowTask> existingTasks,
        CancellationToken cancellationToken)
    {

        WorkflowScheduleResult schedule;
        try
        {
            schedule = await scheduler.ExecuteAsync(
                run.Id,
                run.OriginalRequirement,
                definitions,
                schedulerOptions,
                repository.SaveTaskTransitionAsync,
                cancellationToken,
                existingTasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TransitionRunAsync(run, WorkflowRunState.Cancelled, "Workflow request was cancelled.", CancellationToken.None);
            return ToDto(run, null);
        }

        if (schedule.Cancelled)
        {
            await TransitionRunAsync(run, WorkflowRunState.Cancelled, "Workflow safe-stopped after cancellation.", CancellationToken.None);
            return ToDto(run, null);
        }

        if (schedule.AwaitingClarification)
        {
            await TransitionRunAsync(run, WorkflowRunState.AwaitingClarification,
                "Workflow paused until the unresolved requirement is clarified.", CancellationToken.None);
            var clarificationSummary = schedule.Tasks
                .Where(task => task.Type == WorkflowTaskType.Clarification)
                .Select(task => ReadResult(task.OutputJson)?.Summary)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return ToDto(run, clarificationSummary);
        }

        await TransitionRunAsync(run, WorkflowRunState.Validating, "Workflow tasks reached a terminal state.", CancellationToken.None);
        var targetState = schedule.Succeeded ? WorkflowRunState.Completed : WorkflowRunState.Failed;
        var summary = schedule.Tasks
            .Where(task => task.Type == WorkflowTaskType.FinalSummary)
            .Select(task => ReadResult(task.OutputJson)?.Summary)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        await TransitionRunAsync(run, targetState, summary ?? "Workflow ended without a final summary.", CancellationToken.None);

        return ToDto(run, summary);
    }

    public async Task<WorkflowRunDto?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await repository.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var tasks = await repository.GetTasksAsync(runId, cancellationToken);
        var summary = tasks
            .Where(task => task.Type == WorkflowTaskType.FinalSummary)
            .Select(task => ReadResult(task.OutputJson)?.Summary)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return ToDto(run, summary);
    }

    public async Task<IReadOnlyList<WorkflowTaskDto>?> GetTasksAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (await repository.GetRunAsync(runId, cancellationToken) is null)
        {
            return null;
        }

        var tasks = await repository.GetTasksAsync(runId, cancellationToken);
        return tasks.Select(task => new WorkflowTaskDto(
            task.Id,
            task.Type,
            task.Priority,
            task.State,
            JsonSerializer.Deserialize<Guid[]>(task.DependenciesJson) ?? [],
            JsonSerializer.Deserialize<string[]>(task.InputArtifactsJson) ?? [],
            JsonSerializer.Deserialize<string[]>(task.OutputArtifactsJson) ?? [],
            task.AttemptCount,
            task.Error,
            ReadResult(task.OutputJson),
            task.UpdatedAtUtc)).ToArray();
    }

    public async Task<IReadOnlyList<WorkflowAuditEventDto>?> GetAuditAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (await repository.GetRunAsync(runId, cancellationToken) is null)
        {
            return null;
        }

        var events = await repository.GetAuditEventsAsync(runId, cancellationToken);
        return events.Select(item => new WorkflowAuditEventDto(
            item.Id, item.RunId, item.TaskId, item.EventType, item.PreviousState, item.NewState,
            item.Detail, item.OccurredAtUtc)).ToArray();
    }

    public async Task<WorkflowMetricsDto?> GetMetricsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await repository.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var runs = await repository.GetRunsAsync(cancellationToken);
        var terminalRuns = runs.Where(item => item.State is WorkflowRunState.Completed or WorkflowRunState.Failed or WorkflowRunState.Cancelled).ToArray();
        var tasks = await repository.GetTasksAsync(runId, cancellationToken);
        var terminalTasks = tasks.Where(item => item.State is WorkflowTaskState.Succeeded or WorkflowTaskState.Failed or WorkflowTaskState.Skipped or WorkflowTaskState.Cancelled).ToArray();
        var events = await repository.GetAuditEventsAsync(runId, cancellationToken);
        var attemptCount = events.Count(item => item.EventType == "task.attempt_started");
        var retryCount = events.Count(item => item.EventType == "task.retry_scheduled");
        var rollbackCount = events.Count(item => item.EventType.StartsWith("rollback.", StringComparison.Ordinal));
        var recoveryTimes = CalculateRecoveryTimes(events);

        return new WorkflowMetricsDto(
            terminalRuns.Length,
            terminalRuns.Count(item => item.State == WorkflowRunState.Completed),
            terminalRuns.Length == 0 ? null : (double)terminalRuns.Count(item => item.State == WorkflowRunState.Completed) / terminalRuns.Length,
            terminalTasks.Length,
            terminalTasks.Count(item => item.State == WorkflowTaskState.Failed),
            terminalTasks.Length == 0 ? null : (double)terminalTasks.Count(item => item.State == WorkflowTaskState.Failed) / terminalTasks.Length,
            attemptCount,
            retryCount,
            attemptCount == 0 ? null : (double)retryCount / attemptCount,
            rollbackCount,
            recoveryTimes.Count == 0 ? null : recoveryTimes.Average(time => time.TotalMilliseconds),
            run.CompletedAtUtc is null ? null : (long)(run.CompletedAtUtc.Value - run.CreatedAtUtc).TotalMilliseconds);
    }

    private async Task TransitionRunAsync(
        WorkflowRun run,
        WorkflowRunState next,
        string detail,
        CancellationToken cancellationToken)
    {
        var previous = run.State;
        run.TransitionTo(next, DateTimeOffset.UtcNow);
        await repository.SaveRunTransitionAsync(run, previous, detail, cancellationToken);
    }

    private static WorkflowAgentResult? ReadResult(string? json)
    {
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<WorkflowAgentResult>(json, JsonOptions);
    }

    private static WorkflowRunDto ToDto(WorkflowRun run, string? summary)
    {
        return new WorkflowRunDto(run.Id, run.OriginalRequirement, run.State,
            run.CreatedAtUtc, run.UpdatedAtUtc, run.CompletedAtUtc, summary);
    }

    private static IReadOnlyList<TimeSpan> CalculateRecoveryTimes(IReadOnlyList<WorkflowAuditEvent> events)
    {
        var recoveries = new List<TimeSpan>();
        foreach (var taskEvents in events.Where(item => item.TaskId.HasValue).GroupBy(item => item.TaskId))
        {
            DateTimeOffset? failureAt = null;
            foreach (var item in taskEvents.OrderBy(item => item.OccurredAtUtc))
            {
                if (item.EventType == "task.failed")
                {
                    failureAt = item.OccurredAtUtc;
                }
                else if (failureAt.HasValue && item.EventType == "task.attempt_started")
                {
                    recoveries.Add(item.OccurredAtUtc - failureAt.Value);
                    failureAt = null;
                }
            }
        }

        return recoveries;
    }
}