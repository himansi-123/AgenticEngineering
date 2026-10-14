using System.Text.Json;
using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.Application.Workflows;

public sealed class DagWorkflowScheduler(IWorkflowTaskExecutor executor)
{
    public async Task<WorkflowScheduleResult> ExecuteAsync(
        Guid runId,
        string originalRequirement,
        IReadOnlyCollection<WorkflowTaskDefinition> definitions,
        WorkflowSchedulerOptions options,
        Func<WorkflowTask, CancellationToken, Task>? onTaskChanged,
        CancellationToken cancellationToken,
        IReadOnlyCollection<WorkflowTask>? existingTasks = null)
    {
        options.Validate();
        var orderedDefinitions = WorkflowTaskGraph.TopologicalOrder(definitions);
        var tasks = existingTasks is null
            ? orderedDefinitions.ToDictionary(
                definition => definition.Id,
                definition => new WorkflowTask(runId, definition))
            : existingTasks.ToDictionary(task => task.Id);
        if (tasks.Count != orderedDefinitions.Count || orderedDefinitions.Any(definition => !tasks.ContainsKey(definition.Id)))
        {
            throw new ArgumentException("Existing task state must match the workflow definition graph.", nameof(existingTasks));
        }
        var running = new Dictionary<Guid, Task>();
        var cancelled = false;

        while (tasks.Values.Any(task => task.State is WorkflowTaskState.Pending or WorkflowTaskState.Ready or WorkflowTaskState.Running)
               && !tasks.Values.Any(task => task.State == WorkflowTaskState.AwaitingClarification))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                foreach (var task in tasks.Values.Where(task => task.State is WorkflowTaskState.Pending or WorkflowTaskState.Ready))
                {
                    await TransitionAsync(task, WorkflowTaskState.Cancelled, null, onTaskChanged, CancellationToken.None);
                }
            }

            if (!cancelled)
            {
                foreach (var task in orderedDefinitions.Select(definition => tasks[definition.Id])
                             .Where(task => task.State == WorkflowTaskState.Pending))
                {
                    var dependencies = task.DependenciesJson;
                    var dependencyIds = JsonSerializer.Deserialize<Guid[]>(dependencies) ?? [];
                    var dependencyStates = dependencyIds.Select(id => tasks[id].State).ToArray();
                    if (dependencyIds.Any(id =>
                        tasks[id].State is WorkflowTaskState.Failed or WorkflowTaskState.Skipped or WorkflowTaskState.Cancelled
                        && !running.ContainsKey(id)))
                    {
                        await TransitionAsync(task, WorkflowTaskState.Skipped, "A dependency did not succeed.", onTaskChanged, cancellationToken);
                    }
                    else if (dependencyStates.All(state => state == WorkflowTaskState.Succeeded))
                    {
                        await TransitionAsync(task, WorkflowTaskState.Ready, null, onTaskChanged, cancellationToken);
                    }
                }

                foreach (var task in orderedDefinitions
                             .Select(definition => tasks[definition.Id])
                             .Where(task => task.State == WorkflowTaskState.Ready)
                             .OrderByDescending(task => task.Priority)
                             .ThenBy(task => task.Id)
                             .Take(options.MaxConcurrency - running.Count))
                {
                    await TransitionAsync(task, WorkflowTaskState.Running, null, onTaskChanged, cancellationToken);
                    running.Add(task.Id, ExecuteTaskAsync(
                        task,
                        tasks[task.Id].OriginalRequirementOr(originalRequirement),
                        executor,
                        tasks.Values
                            .Where(previousTask => previousTask.State == WorkflowTaskState.Succeeded)
                            .Select(previousTask => ReadResult(previousTask.OutputJson))
                            .Where(result => result is not null)
                            .Cast<WorkflowAgentResult>()
                            .ToArray(),
                        options,
                        onTaskChanged,
                        cancellationToken));
                }
            }

            if (running.Count > 0)
            {
                var completed = await Task.WhenAny(running.Values);
                var completedId = running.First(pair => pair.Value == completed).Key;
                running.Remove(completedId);
                await completed;
            }
            else if (!tasks.Values.Any(task => task.State is WorkflowTaskState.Pending or WorkflowTaskState.Ready or WorkflowTaskState.Running))
            {
                break;
            }
            else if (cancelled)
            {
                break;
            }
            else
            {
                throw new InvalidOperationException("The workflow scheduler could not make progress.");
            }
        }

        var finalTasks = orderedDefinitions.Select(definition => tasks[definition.Id]).ToArray();
        return new WorkflowScheduleResult(
            finalTasks,
            !cancelled && finalTasks.All(task => task.State == WorkflowTaskState.Succeeded),
            cancelled || finalTasks.Any(task => task.State == WorkflowTaskState.Cancelled),
            finalTasks.Any(task => task.State == WorkflowTaskState.AwaitingClarification));
    }

    private static async Task ExecuteTaskAsync(
        WorkflowTask task,
        string originalRequirement,
        IWorkflowTaskExecutor executor,
        IReadOnlyList<WorkflowAgentResult> priorResults,
        WorkflowSchedulerOptions options,
        Func<WorkflowTask, CancellationToken, Task>? onTaskChanged,
        CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            WorkflowAgentResult result;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(options.EffectiveTaskTimeout);
                try
                {
                    result = await executor.ExecuteAsync(
                        new WorkflowTaskExecutionContext(task.RunId, originalRequirement, DefinitionOf(task), task.AttemptCount, priorResults),
                        timeout.Token).WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await TransitionAsync(task, WorkflowTaskState.Cancelled, "Execution was cancelled.", onTaskChanged, CancellationToken.None);
                    return;
                }
                catch (OperationCanceledException)
                {
                    result = Failure("Task exceeded its configured timeout.", isTransient: true);
                }
                catch (Exception exception)
                {
                    result = Failure(exception.Message, isTransient: false);
                }
            }

            if (result.Succeeded)
            {
                task.SetOutput(JsonSerializer.Serialize(result));
                if (result.RequiresClarification)
                {
                    await TransitionAsync(task, WorkflowTaskState.AwaitingClarification, null, onTaskChanged, cancellationToken);
                    return;
                }

                await TransitionAsync(task, WorkflowTaskState.Succeeded, result.Summary, onTaskChanged, cancellationToken);
                return;
            }

            var error = string.Join(Environment.NewLine, result.Errors);
            if (string.IsNullOrWhiteSpace(error))
            {
                error = result.Summary;
            }

            await TransitionAsync(task, WorkflowTaskState.Failed, error, onTaskChanged, cancellationToken);
            if (!result.IsTransient || retry >= options.MaxRetries || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var delay = TimeSpan.FromMilliseconds(
                options.EffectiveRetryBaseDelay.TotalMilliseconds * Math.Pow(2, retry));
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await TransitionAsync(task, WorkflowTaskState.Cancelled, "Execution was cancelled during retry delay.", onTaskChanged, CancellationToken.None);
                return;
            }

            await TransitionAsync(task, WorkflowTaskState.Ready, null, onTaskChanged, cancellationToken);
            await TransitionAsync(task, WorkflowTaskState.Running, null, onTaskChanged, cancellationToken);
        }
    }

    private static async Task TransitionAsync(
        WorkflowTask task,
        WorkflowTaskState next,
        string? error,
        Func<WorkflowTask, CancellationToken, Task>? onTaskChanged,
        CancellationToken cancellationToken)
    {
        task.TransitionTo(next, DateTimeOffset.UtcNow, next == WorkflowTaskState.Failed ? error : null);
        if (onTaskChanged is not null)
        {
            await onTaskChanged(task, cancellationToken);
        }
    }

    private static WorkflowTaskDefinition DefinitionOf(WorkflowTask task)
    {
        return new WorkflowTaskDefinition(
            task.Id,
            task.Type,
            task.Priority,
            JsonSerializer.Deserialize<Guid[]>(task.DependenciesJson) ?? [],
            JsonSerializer.Deserialize<string[]>(task.InputArtifactsJson) ?? [],
            JsonSerializer.Deserialize<string[]>(task.OutputArtifactsJson) ?? []);
    }

    private static WorkflowAgentResult? ReadResult(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<WorkflowAgentResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static WorkflowAgentResult Failure(string error, bool isTransient)
    {
        return new WorkflowAgentResult(false, error, [], [], [], [error], [], isTransient);
    }
}

internal static class WorkflowTaskExtensions
{
    public static string OriginalRequirementOr(this WorkflowTask task, string originalRequirement) => originalRequirement;
}