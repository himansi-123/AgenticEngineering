using AgenticEngineering.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace AgenticEngineering.Api.Controllers;

[ApiController]
[Route("api/scenarios")]
public sealed class WorkflowScenariosController(IWorkflowService workflowService) : ControllerBase
{
    private static readonly IReadOnlyList<WorkflowScenarioDto> Scenarios =
    [
        new("greenfield-expiration", "greenfield", "Add configurable expiration to shortened URLs.", false),
        new("brownfield-analytics-defect", "brownfield", "Fix an analytics-counting defect in the existing URL-shortener application.", true),
        new("ambiguous-link-expiration", "ambiguous", "Make links expire automatically.", false)
    ];

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkflowScenarioDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WorkflowScenarioDto>> GetScenarios() => Ok(Scenarios);

    [HttpPost("{scenarioId}/run")]
    [ProducesResponseType<WorkflowRunDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowRunDto>> Run(string scenarioId, CancellationToken cancellationToken)
    {
        var scenario = Scenarios.SingleOrDefault(item => item.Id.Equals(scenarioId, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
        {
            return NotFound();
        }

        var run = await workflowService.CreateAndExecuteAsync(
            new CreateWorkflowRequest(scenario.Requirement), cancellationToken);
        return Created($"/api/workflows/{run.Id}", run);
    }
}