namespace AgenticEngineering.Domain.Workflows;

public enum WorkflowRunState
{
    Created,
    Planning,
    Executing,
    AwaitingApproval,
    Validating,
    Completed,
    Failed,
    Cancelled
}

public enum WorkflowTaskState
{
    Pending,
    Ready,
    Running,
    AwaitingApproval,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

public enum WorkflowTaskType
{
    RequirementAnalysis,
    Clarification,
    TaskDecomposition,
    ArchitectureDesign,
    Implementation,
    TestingValidation,
    Documentation,
    SecurityReview,
    ReleaseReadiness,
    FinalSummary
}