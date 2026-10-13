namespace AgenticEngineering.Domain.Workflows;

public sealed record WorkflowTaskDefinition(
    Guid Id,
    WorkflowTaskType Type,
    int Priority,
    IReadOnlyList<Guid> Dependencies,
    IReadOnlyList<string> InputArtifacts,
    IReadOnlyList<string> OutputArtifacts);