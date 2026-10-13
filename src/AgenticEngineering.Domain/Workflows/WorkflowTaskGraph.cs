namespace AgenticEngineering.Domain.Workflows;

public static class WorkflowTaskGraph
{
    public static IReadOnlyList<WorkflowTaskDefinition> TopologicalOrder(
        IEnumerable<WorkflowTaskDefinition> taskDefinitions)
    {
        var tasks = taskDefinitions.ToArray();
        var byId = new Dictionary<Guid, WorkflowTaskDefinition>();
        foreach (var task in tasks)
        {
            if (task.Id == Guid.Empty || !byId.TryAdd(task.Id, task))
            {
                throw new ArgumentException("Task identifiers must be non-empty and unique.", nameof(taskDefinitions));
            }

            if (task.Dependencies.Distinct().Count() != task.Dependencies.Count)
            {
                throw new ArgumentException($"Task {task.Id} contains duplicate dependencies.", nameof(taskDefinitions));
            }
        }

        var dependents = tasks.ToDictionary(task => task.Id, _ => new List<Guid>());
        var remainingDependencies = new Dictionary<Guid, int>();
        foreach (var task in tasks)
        {
            remainingDependencies[task.Id] = task.Dependencies.Count;
            foreach (var dependencyId in task.Dependencies)
            {
                if (!byId.ContainsKey(dependencyId))
                {
                    throw new ArgumentException($"Task {task.Id} depends on unknown task {dependencyId}.", nameof(taskDefinitions));
                }

                if (dependencyId == task.Id)
                {
                    throw new ArgumentException($"Task {task.Id} cannot depend on itself.", nameof(taskDefinitions));
                }

                dependents[dependencyId].Add(task.Id);
            }
        }

        var ready = tasks
            .Where(task => remainingDependencies[task.Id] == 0)
            .ToList();
        var ordered = new List<WorkflowTaskDefinition>(tasks.Length);

        while (ready.Count > 0)
        {
            ready.Sort(ComparePriority);
            var task = ready[0];
            ready.RemoveAt(0);
            ordered.Add(task);

            foreach (var dependentId in dependents[task.Id])
            {
                remainingDependencies[dependentId]--;
                if (remainingDependencies[dependentId] == 0)
                {
                    ready.Add(byId[dependentId]);
                }
            }
        }

        if (ordered.Count != tasks.Length)
        {
            throw new ArgumentException("Workflow task dependencies contain a cycle.", nameof(taskDefinitions));
        }

        return ordered;
    }

    private static int ComparePriority(WorkflowTaskDefinition left, WorkflowTaskDefinition right)
    {
        var priorityOrder = right.Priority.CompareTo(left.Priority);
        return priorityOrder != 0 ? priorityOrder : left.Id.CompareTo(right.Id);
    }
}