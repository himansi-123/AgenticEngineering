using AgenticEngineering.Domain.Workflows;

namespace AgenticEngineering.Application.Workflows;

public sealed class WorkflowPlanFactory
{
    public IReadOnlyList<WorkflowTaskDefinition> Create(string requirement)
    {
        var requirementAnalysis = Definition(WorkflowTaskType.RequirementAnalysis, 100);
        var clarification = Definition(WorkflowTaskType.Clarification, 90, requirementAnalysis.Id);
        var decomposition = Definition(WorkflowTaskType.TaskDecomposition, 80, clarification.Id);
        var architecture = Definition(WorkflowTaskType.ArchitectureDesign, 70, decomposition.Id);
        var implementation = Definition(WorkflowTaskType.Implementation, 60, architecture.Id);
        var validation = Definition(WorkflowTaskType.TestingValidation, 50, implementation.Id);
        var documentation = Definition(WorkflowTaskType.Documentation, 40, decomposition.Id);
        var security = Definition(WorkflowTaskType.SecurityReview, 30, validation.Id, documentation.Id);
        var release = Definition(WorkflowTaskType.ReleaseReadiness, 20, security.Id);
        var summary = Definition(WorkflowTaskType.FinalSummary, 10, release.Id);

        return [requirementAnalysis, clarification, decomposition, architecture,
            implementation, validation, documentation, security, release, summary];
    }

    private static WorkflowTaskDefinition Definition(WorkflowTaskType type, int priority, params Guid[] dependencies)
    {
        return new WorkflowTaskDefinition(Guid.NewGuid(), type, priority, dependencies, [], []);
    }
}