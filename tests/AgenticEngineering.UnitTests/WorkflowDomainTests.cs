using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.UnitTests;

public sealed class WorkflowDomainTests
{
    [Fact]
    public void Task_graph_orders_dependencies_and_prefers_higher_priority_ready_tasks()
    {
        var first = Task(Guid.Parse("00000000-0000-0000-0000-000000000001"), 1);
        var second = Task(Guid.Parse("00000000-0000-0000-0000-000000000002"), 5);
        var dependent = Task(Guid.Parse("00000000-0000-0000-0000-000000000003"), 100, first.Id, second.Id);

        var ordered = WorkflowTaskGraph.TopologicalOrder([first, second, dependent]);

        Assert.Equal([second.Id, first.Id, dependent.Id], ordered.Select(task => task.Id));
    }

    [Fact]
    public void Task_graph_rejects_unknown_dependencies()
    {
        var task = Task(Guid.NewGuid(), 1, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => WorkflowTaskGraph.TopologicalOrder([task]));
    }

    [Fact]
    public void Task_graph_rejects_cycles()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = Task(firstId, 1, secondId);
        var second = Task(secondId, 1, firstId);

        Assert.Throws<ArgumentException>(() => WorkflowTaskGraph.TopologicalOrder([first, second]));
    }

    [Fact]
    public void Task_graph_rejects_duplicate_task_identifiers()
    {
        var taskId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => WorkflowTaskGraph.TopologicalOrder([
            Task(taskId, 1),
            Task(taskId, 2)]));
    }

    [Fact]
    public void Run_state_machine_rejects_illegal_transitions()
    {
        var now = DateTimeOffset.UtcNow;
        var run = new WorkflowRun("Add URL expiration", now);
        run.TransitionTo(WorkflowRunState.Planning, now.AddSeconds(1));
        run.TransitionTo(WorkflowRunState.Executing, now.AddSeconds(2));

        Assert.Throws<InvalidOperationException>(() => run.TransitionTo(WorkflowRunState.Created, now.AddSeconds(3)));
    }

    [Fact]
    public void Task_state_machine_allows_retry_but_not_success_regression()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new WorkflowTask(Guid.NewGuid(), Task(Guid.NewGuid(), 1));
        task.TransitionTo(WorkflowTaskState.Ready, now);
        task.TransitionTo(WorkflowTaskState.Running, now.AddSeconds(1));
        task.TransitionTo(WorkflowTaskState.Failed, now.AddSeconds(2), "transient");
        task.TransitionTo(WorkflowTaskState.Ready, now.AddSeconds(3));
        task.TransitionTo(WorkflowTaskState.Running, now.AddSeconds(4));
        task.TransitionTo(WorkflowTaskState.Succeeded, now.AddSeconds(5));

        Assert.Throws<InvalidOperationException>(() => task.TransitionTo(WorkflowTaskState.Ready, now.AddSeconds(6)));
    }

    private static WorkflowTaskDefinition Task(Guid id, int priority, params Guid[] dependencies)
    {
        return new WorkflowTaskDefinition(id, WorkflowTaskType.Implementation, priority, dependencies, [], []);
    }
}