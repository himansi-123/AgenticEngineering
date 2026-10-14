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
    Cancelled,
    AwaitingClarification
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
    Cancelled,
    AwaitingClarification
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